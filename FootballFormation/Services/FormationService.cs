using FootballFormation.Enums;
using FootballFormation.Models;

namespace FootballFormation.Services;

public class FormationService : IFormationService
{
    private readonly List<Player> _players = [];
    private readonly List<Formation> _formations = [];
    private readonly List<Substitution> _substitutions = [];
    private const int TARGET_MINUTES_PER_PLAYER = 45;
    private const int MIN_MINUTES_PER_PLAYER = 30;
    private const int TOTAL_GAME_MINUTES = 60;
    private const int REQUIRED_FIELD_PLAYERS = 10;

    public IReadOnlyList<Player> Players => _players;
    public IReadOnlyList<Formation> Formations => _formations;
    public IReadOnlyList<Substitution> Substitutions => _substitutions;

    public void LoadPlayers(List<Player> players)
    {
        Console.WriteLine("LoadPlayers called with " + players.Count + " players");
        _players.Clear();
        _players.AddRange(players);
        GenerateFormations();
    }

    private void GenerateFormations()
    {
        Console.WriteLine("Generating formations...");
        _formations.Clear();
        _substitutions.Clear();

        var availablePlayers = _players.Where(p => !p.IsAbsent).ToList();
        Console.WriteLine($"Available players: {availablePlayers.Count}");

        var keepers = availablePlayers.Where(p => p.IsKeeper).ToList();
        Console.WriteLine($"Keepers: {keepers.Count}");

        var fieldPlayers = availablePlayers.Where(p => !p.IsKeeper).ToList();
        Console.WriteLine($"Field players: {fieldPlayers.Count}");

        foreach (var player in availablePlayers)
        {
            player.MinutesPlayed = 0;
        }

        var keeper1 = keepers.FirstOrDefault();
        var keeper2 = keepers.Count > 1 ? keepers[1] : keeper1;

        // Create equal groups based on number of players and required minutes
        var equalPlayingGroups = CreateEqualPlayingGroups(fieldPlayers);

        // Create formations using these groups
        CreateFormation(equalPlayingGroups[0], keeper1, "Eerste Helft - Start", 0, 15);
        CreateFormation(equalPlayingGroups[1], keeper1, "Eerste Helft - Na wissels", 15, 30);
        CreateFormation(equalPlayingGroups[2], keeper2, "Tweede Helft - Start", 30, 45);
        CreateFormation(equalPlayingGroups[3], keeper2, "Tweede Helft - Na wissels", 45, 60);

        // Remove substitutions that would reduce playing time equality
        OptimizeSubstitutions();
        CalculateMinutesPlayed();
    }

    private List<List<Player>> CreateEqualPlayingGroups(List<Player> fieldPlayers)
    {
        var groups = new List<List<Player>>();
        for (int i = 0; i < 4; i++)
        {
            groups.Add(new List<Player>());
        }

        // Calculate how many quarters each player should play to achieve equal time
        int totalQuarters = 4 * REQUIRED_FIELD_PLAYERS; // 40 player-quarters available
        int quartersPerPlayer = totalQuarters / fieldPlayers.Count;
        int extraQuarters = totalQuarters % fieldPlayers.Count;

        // Sort players by their current minutes played
        var sortedPlayers = fieldPlayers
            .OrderBy(p => p.MinutesPlayed)
            .ToList();

        // Create a schedule for each player
        var playerSchedule = sortedPlayers.ToDictionary(
            p => p,
            p => quartersPerPlayer + (extraQuarters-- > 0 ? 1 : 0)
        );

        // Fill groups ensuring each player gets their allocated quarters
        foreach (var player in sortedPlayers)
        {
            var quartersToPlay = playerSchedule[player];
            var possibleGroups = Enumerable.Range(0, 4)
                .Where(i => groups[i].Count < REQUIRED_FIELD_PLAYERS)
                .OrderBy(i => groups[i].Count)
                .Take(quartersToPlay)
                .ToList();

            foreach (var groupIndex in possibleGroups)
            {
                groups[groupIndex].Add(player);
            }
        }

        // Balance positions within each group
        foreach (var group in groups)
        {
            BalancePositionsInGroup(group);
        }

        return groups;
    }

    private void BalancePositionsInGroup(List<Player> group)
    {
        var positions = new[] {
            Position.CB, Position.CB,
            Position.LB, Position.RB,
            Position.CDM, Position.CDM,
            Position.CAM,
            Position.LW, Position.ST, Position.RW
        };

        var originalGroup = new List<Player>(group);
        var balancedGroup = new List<Player>();

        foreach (var position in positions)
        {
            var bestPlayer = originalGroup
                .OrderByDescending(p => p.GetPositionScore(position))
                .FirstOrDefault();

            if (bestPlayer != null)
            {
                balancedGroup.Add(bestPlayer);
                originalGroup.Remove(bestPlayer);
            }
        }

        group.Clear();
        group.AddRange(balancedGroup);
    }

    private void OptimizeSubstitutions()
    {
        // Remove all existing substitutions
        _substitutions.Clear();

        // Only create substitutions for players who haven't played enough
        var playerMinutes = _players
            .Where(p => !p.IsKeeper && !p.IsAbsent)
            .ToDictionary(p => p, p => CalculateActualMinutesPlayed(p));

        var targetMinutes = TOTAL_GAME_MINUTES / Math.Ceiling((double)playerMinutes.Count / REQUIRED_FIELD_PLAYERS);

        foreach (var formation in _formations.Skip(1)) // Skip first formation
        {
            var playersNeedingTime = playerMinutes
                .Where(kv => kv.Value < targetMinutes - 5) // Allow 5 minutes variance
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key)
                .Take(3) // Limit to 3 substitutions per period
                .ToList();

            foreach (var playerIn in playersNeedingTime)
            {
                var playerOut = formation.PositionedPlayers
                    .Where(kvp => kvp.Value != null && 
                                playerMinutes[kvp.Value] > targetMinutes)
                    .OrderByDescending(kvp => playerMinutes[kvp.Value])
                    .FirstOrDefault();

                if (!playerOut.Equals(default(KeyValuePair<string, Player>)))
                {
                    // Make substitution
                    var position = playerOut.Key;
                    formation.PositionedPlayers[position] = playerIn;

                    _substitutions.Add(new Substitution
                    {
                        Minute = formation.StartMinute,
                        PlayerOut = playerOut.Value!,
                        PlayerIn = playerIn,
                        FromPosition = position,
                        ToPosition = position
                    });

                    // Update minutes
                    var minutesInPeriod = formation.EndMinute - formation.StartMinute;
                    playerMinutes[playerOut.Value!] -= minutesInPeriod;
                    playerMinutes[playerIn] += minutesInPeriod;
                }
            }
        }
    }

    private int CalculateActualMinutesPlayed(Player player)
    {
        int minutes = 0;
        foreach (var formation in _formations)
        {
            if (formation.PositionedPlayers.Values.Contains(player))
            {
                minutes += formation.EndMinute - formation.StartMinute;
            }
        }
        return minutes;
    }

    private void CreateFormation(List<Player> availablePlayers, Player? keeper, string period, int startMinute, int endMinute)
    {
        if (keeper == null)
            throw new InvalidOperationException("A goalkeeper is required");

        var formation = new Formation
        {
            Period = period,
            StartMinute = startMinute,
            EndMinute = endMinute,
            Goalkeeper = keeper
        };

        var usedPlayers = new HashSet<Player>();
        var allAvailablePlayers = _players
            .Where(p => !p.IsAbsent && !p.IsKeeper)
            .OrderBy(p => p.MinutesPlayed)
            .ThenByDescending(p => p.Skills.AverageSkill)
            .ToList();

        // Fill all required positions
        var positions = new[] {
            ("CB1", Position.CB), ("CB2", Position.CB),
            ("LB", Position.LB), ("RB", Position.RB),
            ("CDM1", Position.CDM), ("CDM2", Position.CDM),
            ("CAM", Position.CAM),
            ("LW", Position.LW), ("ST", Position.ST), ("RW", Position.RW)
        };

        // First try to assign from available players
        foreach (var (key, pos) in positions)
        {
            var player = AssignPositionWithPriority(availablePlayers, pos, usedPlayers);
            if (player != null)
            {
                formation.PositionedPlayers[key] = player;
            }
        }

        // Check if we need to fill missing positions
        var missingPositions = positions
            .Where(p => !formation.PositionedPlayers.ContainsKey(p.Item1) || 
                       formation.PositionedPlayers[p.Item1] == null)
            .ToList();

        if (missingPositions.Any())
        {
            Console.WriteLine($"Formation {period} has {missingPositions.Count} missing positions. Attempting to fill...");
            
            foreach (var (key, pos) in missingPositions)
            {
                // Try to find a player from all available players who isn't used in this formation
                var player = allAvailablePlayers
                    .Where(p => !usedPlayers.Contains(p))
                    .OrderByDescending(p => CalculatePositionPriority(p, pos))
                    .FirstOrDefault();

                if (player != null)
                {
                    formation.PositionedPlayers[key] = player;
                    usedPlayers.Add(player);
                    Console.WriteLine($"Filled position {key} with player {player.Name}");
                }
                else
                {
                    // If we still can't find a player, we need to borrow one from another position
                    var playerToBorrow = formation.PositionedPlayers
                        .Where(kvp => kvp.Value != null && 
                                    GetPositionImportance(ParsePosition(kvp.Key)) < 
                                    GetPositionImportance(pos))
                        .OrderBy(kvp => GetPositionImportance(ParsePosition(kvp.Key)))
                        .FirstOrDefault();

                    if (playerToBorrow.Value != null)
                    {
                        // Move the player to the new position
                        formation.PositionedPlayers[key] = playerToBorrow.Value;
                        formation.PositionedPlayers.Remove(playerToBorrow.Key);

                        // Try to fill the now-empty position
                        var replacementPlayer = allAvailablePlayers
                            .Where(p => !usedPlayers.Contains(p))
                            .OrderByDescending(p => CalculatePositionPriority(p, ParsePosition(playerToBorrow.Key)))
                            .FirstOrDefault();

                        if (replacementPlayer != null)
                        {
                            formation.PositionedPlayers[playerToBorrow.Key] = replacementPlayer;
                            usedPlayers.Add(replacementPlayer);
                        }
                    }
                }
            }
        }

        // Final validation
        ValidateAndFixFormation(formation);

        // Select remaining players for bench
        formation.Bench = allAvailablePlayers
            .Where(p => !usedPlayers.Contains(p))
            .OrderBy(p => p.MinutesPlayed)
            .ToList();

        _formations.Add(formation);
        Console.WriteLine($"Created formation {period} with {formation.PositionedPlayers.Count} players and {formation.Bench.Count} on bench");

        if (_formations.Count > 1)
        {
            CreateSubstitutionsForFormation(formation, _formations[^2], startMinute);
        }
    }

    private Position ParsePosition(string positionKey)
    {
        return positionKey switch
        {
            "LB" => Position.LB,
            "CB1" or "CB2" => Position.CB,
            "RB" => Position.RB,
            "CDM1" or "CDM2" => Position.CDM,
            "CAM" => Position.CAM,
            "LW" => Position.LW,
            "ST" => Position.ST,
            "RW" => Position.RW,
            _ => throw new ArgumentException($"Invalid position key: {positionKey}")
        };
    }

    private int GetPositionImportance(Position position)
    {
        return position switch
        {
            Position.CB => 5,  // Most important - central defense
            Position.CDM => 4, // Defensive midfield
            Position.ST => 4,  // Striker
            Position.CAM => 3, // Attacking midfield
            Position.LB or Position.RB => 2, // Full backs
            Position.LW or Position.RW => 1, // Wings
            _ => 0
        };
    }

    private void ValidateAndFixFormation(Formation formation)
    {
        if (formation.PositionedPlayers.Count < REQUIRED_FIELD_PLAYERS)
        {
            var availablePlayers = _players
                .Where(p => !p.IsAbsent && !p.IsKeeper && 
                          !formation.PositionedPlayers.Values.Contains(p))
                .OrderBy(p => p.MinutesPlayed)
                .ToList();

            var positions = new[] {
                ("CB1", Position.CB), ("CB2", Position.CB),
                ("LB", Position.LB), ("RB", Position.RB),
                ("CDM1", Position.CDM), ("CDM2", Position.CDM),
                ("CAM", Position.CAM),
                ("LW", Position.LW), ("ST", Position.ST), ("RW", Position.RW)
            };

            foreach (var (key, pos) in positions)
            {
                if (!formation.PositionedPlayers.ContainsKey(key) || 
                    formation.PositionedPlayers[key] == null)
                {
                    var player = availablePlayers
                        .OrderByDescending(p => CalculatePositionPriority(p, pos))
                        .FirstOrDefault();

                    if (player != null)
                    {
                        formation.PositionedPlayers[key] = player;
                        availablePlayers.Remove(player);
                    }
                }
            }

            // Final check
            if (formation.PositionedPlayers.Count != REQUIRED_FIELD_PLAYERS)
            {
                throw new InvalidOperationException($"Unable to create valid formation: Has {formation.PositionedPlayers.Count} players but needs {REQUIRED_FIELD_PLAYERS}");
            }

            if (formation.PositionedPlayers.Values.Any(p => p == null))
            {
                throw new InvalidOperationException("Unable to fill all positions in formation");
            }
        }
    }

    private Player? AssignPositionWithPriority(List<Player> players, Position position, HashSet<Player> usedPlayers)
    {
        var player = players
            .Where(p => !usedPlayers.Contains(p))
            .OrderBy(p => p.MinutesPlayed)
            .ThenByDescending(p => CalculatePositionPriority(p, position))
            .FirstOrDefault();

        if (player != null)
        {
            usedPlayers.Add(player);
        }

        return player;
    }

    private void CreateSubstitutionsForFormation(Formation currentFormation, Formation previousFormation, int minute)
    {
        var playersNeedingTime = currentFormation.Bench
            .Where(p => p.MinutesPlayed < MIN_MINUTES_PER_PLAYER)
            .OrderBy(p => p.MinutesPlayed)
            .Take(3)
            .ToList();

        foreach (var playerIn in playersNeedingTime)
        {
            // Find player to substitute out
            var playerOut = currentFormation.PositionedPlayers
                .Where(kvp => kvp.Value?.MinutesPlayed >= TARGET_MINUTES_PER_PLAYER / 2)
                .OrderByDescending(kvp => kvp.Value?.MinutesPlayed)
                .FirstOrDefault();

            if (playerOut.Value == null)
                continue; // Skip if no suitable player to substitute out

            var position = playerOut.Key;

            // Make the substitution
            currentFormation.PositionedPlayers[position] = playerIn;

            _substitutions.Add(new Substitution
            {
                Minute = minute,
                PlayerOut = playerOut.Value,
                PlayerIn = playerIn,
                FromPosition = position,
                ToPosition = position
            });

            Console.WriteLine($"Substitution at {minute}': {playerOut.Value.Name} -> {playerIn.Name} ({position})");
        }

        // Validate formation after substitutions
        if (currentFormation.PositionedPlayers.Count != REQUIRED_FIELD_PLAYERS)
            throw new InvalidOperationException($"Invalid substitutions: Has {currentFormation.PositionedPlayers.Count} players but needs exactly {REQUIRED_FIELD_PLAYERS}");

        if (currentFormation.PositionedPlayers.Values.Any(p => p == null))
            throw new InvalidOperationException("All positions must be filled after substitutions");

        if (currentFormation.PositionedPlayers.Values.Distinct().Count() != REQUIRED_FIELD_PLAYERS)
            throw new InvalidOperationException("Invalid substitutions: Contains duplicate players");
    }

    private void BalancePlayingTime(List<Player> fieldPlayers)
    {
        // First handle players with no playing time
        var playersWithNoTime = fieldPlayers
            .Where(p => p.MinutesPlayed == 0)
            .OrderBy(p => p.MinutesPlayed)
            .ToList();

        foreach (var player in playersWithNoTime)
        {
            foreach (var formation in _formations.OrderByDescending(f => f.StartMinute))
            {
                var playerToReplace = formation.PositionedPlayers
                    .OrderByDescending(kvp => kvp.Value?.MinutesPlayed)
                    .FirstOrDefault(kvp => kvp.Value?.MinutesPlayed > MIN_MINUTES_PER_PLAYER);

                if (playerToReplace.Value != null)
                {
                    var position = playerToReplace.Key;
                    formation.PositionedPlayers[position] = player;

                    _substitutions.Add(new Substitution
                    {
                        Minute = formation.StartMinute,
                        PlayerOut = playerToReplace.Value,
                        PlayerIn = player,
                        FromPosition = position,
                        ToPosition = position
                    });

                    if (formation.PositionedPlayers.Values.Any(p => p == null))
                        throw new InvalidOperationException("All positions must remain filled after balancing");
                    break;
                }
            }
        }

        // Then balance players with less than minimum minutes
        var underplayedPlayers = fieldPlayers
            .Where(p => p.MinutesPlayed < MIN_MINUTES_PER_PLAYER && p.MinutesPlayed > 0)
            .OrderBy(p => p.MinutesPlayed)
            .ToList();

        foreach (var player in underplayedPlayers)
        {
            foreach (var formation in _formations.OrderByDescending(f => f.StartMinute))
            {
                var playerToReplace = formation.PositionedPlayers
                    .OrderByDescending(kvp => kvp.Value?.MinutesPlayed)
                    .FirstOrDefault(kvp => kvp.Value?.MinutesPlayed > TARGET_MINUTES_PER_PLAYER);

                if (playerToReplace.Value != null)
                {
                    var position = playerToReplace.Key;
                    formation.PositionedPlayers[position] = player;

                    _substitutions.Add(new Substitution
                    {
                        Minute = formation.StartMinute,
                        PlayerOut = playerToReplace.Value,
                        PlayerIn = player,
                        FromPosition = position,
                        ToPosition = position
                    });

                    if (formation.PositionedPlayers.Values.Any(p => p == null))
                        throw new InvalidOperationException("All positions must remain filled after balancing");
                    break;
                }
            }
        }
    }

    private List<List<Player>> CreateRotationGroups(List<Player> fieldPlayers)
    {
        var groups = new List<List<Player>>();
        for (int i = 0; i < 4; i++)
        {
            groups.Add(new List<Player>());
        }

        // Sort players primarily by minutes played, then by skill
        var sortedPlayers = fieldPlayers
            .OrderBy(p => p.MinutesPlayed)
            .ThenByDescending(p => p.Skills.AverageSkill)
            .ToList();

        // Distribute players evenly across groups ensuring equal playing time distribution
        for (int i = 0; i < sortedPlayers.Count; i++)
        {
            var targetGroup = i % 4;
            groups[targetGroup].Add(sortedPlayers[i]);
        }

        return groups;
    }

    private double CalculatePositionPriority(Player player, Position position)
    {
        var baseScore = player.GetPositionScore(position);

        // Heavily weight minutes played to ensure equal distribution
        if (player.MinutesPlayed < MIN_MINUTES_PER_PLAYER)
            baseScore *= 4.0; // Increased from 2.0 to give more priority
        else if (player.MinutesPlayed < TARGET_MINUTES_PER_PLAYER)
            baseScore *= 2.0; // Added medium priority tier
        else if (player.MinutesPlayed >= TARGET_MINUTES_PER_PLAYER)
            baseScore *= 0.25; // Decreased from 0.5 to more strongly discourage overplaying

        // Reduce the impact of preferred positions to prioritize playing time
        if (player.PreferredPositions.Contains(position))
            baseScore *= 1.2; // Reduced from 1.5

        return baseScore;
    }

    private void CalculateMinutesPlayed()
    {
        foreach (var player in _players)
        {
            player.MinutesPlayed = 0;
        }

        foreach (var formation in _formations)
        {
            var duration = formation.EndMinute - formation.StartMinute;

            // Count minutes for all 10 field players
            foreach (var player in formation.PositionedPlayers.Values)
            {
                player!.MinutesPlayed += duration;
            }

            // Count minutes for goalkeeper
            formation.Goalkeeper!.MinutesPlayed += duration;
        }

        foreach (var sub in _substitutions.OrderBy(s => s.Minute))
        {
            var formation = _formations.First(f => f.StartMinute <= sub.Minute && f.EndMinute > sub.Minute);
            var minutesLeftInPeriod = formation.EndMinute - sub.Minute;

            sub.PlayerOut.MinutesPlayed -= minutesLeftInPeriod;
            sub.PlayerIn.MinutesPlayed += minutesLeftInPeriod;
        }
    }
}
