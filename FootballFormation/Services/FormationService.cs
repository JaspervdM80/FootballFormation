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

        var fieldPlayers = availablePlayers
            .Where(p => !p.IsAbsent)
            .ToList(); // Include all players, even keepers

        foreach (var player in availablePlayers)
        {
            player.MinutesPlayed = 0;
        }

        // Calculate keeper rotations to ensure each plays a full half
        var keeperSchedule = CalculateKeeperSchedule(keepers);

        // Create groups with all players, keepers will be removed from groups where they are keeping
        var equalPlayingGroups = CreateEqualPlayingGroups(fieldPlayers);

        // Remove keepers from groups where they are scheduled as goalkeeper
        if (keepers.Count > 0)
        {
            var firstHalfKeeper = keeperSchedule[0]; // Same as keeperSchedule[1]
            var secondHalfKeeper = keeperSchedule[2]; // Same as keeperSchedule[3]

            // Remove first half keeper from first half groups
            equalPlayingGroups[0].Remove(firstHalfKeeper);
            equalPlayingGroups[1].Remove(firstHalfKeeper);

            // Remove second half keeper from second half groups
            if (secondHalfKeeper != firstHalfKeeper) // Only if we have two keepers
            {
                equalPlayingGroups[2].Remove(secondHalfKeeper);
                equalPlayingGroups[3].Remove(secondHalfKeeper);
            }
        }

        CreateFormation(equalPlayingGroups[0], keeperSchedule[0], "Eerste Helft - Start", 0, 15);
        CreateFormation(equalPlayingGroups[1], keeperSchedule[1], "Eerste Helft - Na wissels", 15, 30);
        CreateFormation(equalPlayingGroups[2], keeperSchedule[2], "Tweede Helft - Start", 30, 45);
        CreateFormation(equalPlayingGroups[3], keeperSchedule[3], "Tweede Helft - Na wissels", 45, 60);

        OptimizeSubstitutions();
        CalculateMinutesPlayed();
    }

    private Player[] CalculateKeeperSchedule(List<Player> keepers)
    {
        var schedule = new Player[4];
        
        if (keepers.Count == 0)
            throw new InvalidOperationException("No goalkeepers available");
        
        if (keepers.Count == 1)
        {
            // Single keeper plays all periods as goalkeeper
            for (int i = 0; i < 4; i++)
                schedule[i] = keepers[0];
        }
        else
        {
            // Sort keepers by minutes played
            var sortedKeepers = keepers.OrderBy(k => k.MinutesPlayed).ToList();
            
            // First keeper gets first half (periods 0 and 1)
            schedule[0] = sortedKeepers[0];
            schedule[1] = sortedKeepers[0];
            
            // Second keeper gets second half (periods 2 and 3)
            schedule[2] = sortedKeepers[1];
            schedule[3] = sortedKeepers[1];
        }

        return schedule;
    }

    private List<List<Player>> CreateEqualPlayingGroups(List<Player> allPlayers)
    {
        var groups = new List<List<Player>>();
        for (int i = 0; i < 4; i++)
        {
            groups.Add(new List<Player>());
        }

        // Calculate how many players we need per group
        int playersPerGroup = REQUIRED_FIELD_PLAYERS;
        
        // Calculate target minutes per player
        int totalMinutesAvailable = TOTAL_GAME_MINUTES * REQUIRED_FIELD_PLAYERS;
        int targetMinutesPerPlayer = totalMinutesAvailable / allPlayers.Count;

        // Track assigned minutes for each player
        var playerMinutes = allPlayers.ToDictionary(p => p, _ => 0);
        
        // Sort players by minutes already played
        var availablePlayers = allPlayers
            .OrderBy(p => p.MinutesPlayed)
            .ToList();

        // First, ensure each player gets at least minimum playing time
        foreach (var player in availablePlayers)
        {
            int periodsNeeded = Math.Max(1, MIN_MINUTES_PER_PLAYER / 15);
            int periodsAssigned = 0;

            for (int i = 0; i < 4 && periodsAssigned < periodsNeeded; i++)
            {
                if (groups[i].Count < playersPerGroup)
                {
                    groups[i].Add(player);
                    playerMinutes[player] += 15;
                    periodsAssigned++;
                }
            }
        }

        // Then distribute remaining time to reach target minutes
        for (int i = 0; i < 4; i++)
        {
            while (groups[i].Count < playersPerGroup)
            {
                var eligiblePlayers = availablePlayers
                    .Where(p => !groups[i].Contains(p) && 
                               playerMinutes[p] < targetMinutesPerPlayer)
                    .OrderBy(p => playerMinutes[p])
                    .ToList();

                if (!eligiblePlayers.Any())
                    break;

                var player = eligiblePlayers.First();
                groups[i].Add(player);
                playerMinutes[player] += 15;
            }
        }

        // If we still have spots to fill, use players with least total time
        for (int i = 0; i < 4; i++)
        {
            while (groups[i].Count < playersPerGroup)
            {
                var player = availablePlayers
                    .Where(p => !groups[i].Contains(p))
                    .OrderBy(p => playerMinutes[p])
                    .FirstOrDefault();

                if (player == null)
                    break;

                groups[i].Add(player);
                playerMinutes[player] += 15;
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
        _substitutions.Clear();
        
        // Calculate current minutes for each player
        var playerMinutes = _players
            .Where(p => !p.IsAbsent)
            .ToDictionary(p => p, p => CalculateActualMinutesPlayed(p));

        // Calculate target minutes
        int totalMinutesAvailable = TOTAL_GAME_MINUTES * REQUIRED_FIELD_PLAYERS;
        int targetMinutesPerPlayer = totalMinutesAvailable / playerMinutes.Count;
        
        // Process each formation after the first one
        foreach (var formation in _formations.Skip(1))
        {
            // Find players who are significantly below target minutes
            var underplayedPlayers = playerMinutes
                .Where(kv => kv.Value < targetMinutesPerPlayer - 5)
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key)
                .Where(p => !formation.PositionedPlayers.Values.Contains(p))
                .Take(3)
                .ToList();

            foreach (var playerIn in underplayedPlayers)
            {
                // Find player to substitute who has played more than target
                var playerOut = formation.PositionedPlayers
                    .Where(kvp => kvp.Value != null &&
                                playerMinutes[kvp.Value] > targetMinutesPerPlayer + 5)
                    .OrderByDescending(kvp => playerMinutes[kvp.Value])
                    .FirstOrDefault();

                if (!playerOut.Equals(default(KeyValuePair<string, Player>)))
                {
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

                    // Update minutes tracking
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

        // First calculate base minutes from formations
        foreach (var formation in _formations)
        {
            var duration = formation.EndMinute - formation.StartMinute;

            // Count field player minutes
            foreach (var player in formation.PositionedPlayers.Values)
            {
                player!.MinutesPlayed += duration;
            }

            // Count goalkeeper minutes
            if (formation.Goalkeeper != null)
            {
                formation.Goalkeeper.MinutesPlayed += duration;
            }
        }

        // Then adjust for substitutions
        foreach (var sub in _substitutions.OrderBy(s => s.Minute))
        {
            var formation = _formations.First(f => f.StartMinute <= sub.Minute && f.EndMinute > sub.Minute);
            var minutesLeftInPeriod = formation.EndMinute - sub.Minute;

            sub.PlayerOut.MinutesPlayed -= minutesLeftInPeriod;
            sub.PlayerIn.MinutesPlayed += minutesLeftInPeriod;
        }

        // Verify and log playing time distribution
        var totalMinutes = _players.Sum(p => p.MinutesPlayed);
        var averageMinutes = totalMinutes / _players.Count(p => !p.IsAbsent);
        Console.WriteLine($"Average minutes per player: {averageMinutes}");
        
        foreach (var player in _players.Where(p => !p.IsAbsent).OrderByDescending(p => p.MinutesPlayed))
        {
            Console.WriteLine($"{player.Name}: {player.MinutesPlayed} minutes");
        }
    }
}
