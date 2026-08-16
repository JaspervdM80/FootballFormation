using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <summary>
    /// The whole schema in one step — the twenty migrations that built it up between March and
    /// August 2026 were folded into this one, since none of the databases that exist need the path
    /// through them any more.
    /// <para>
    /// It deliberately keeps the id of the original <c>InitialCreate</c>, <c>20260322100416</c>,
    /// rather than the timestamp it was scaffolded at. The live volume has that id in its
    /// <c>__EFMigrationsHistory</c> already, so it boots with nothing pending and this file never
    /// runs against it — where a fresh id would have re-run <c>CREATE TABLE</c> over a season of
    /// data and failed the deploy. Rescaffolding this migration must therefore restore the id by
    /// hand, here and in the <c>[Migration]</c> attribute in the designer file.
    /// </para>
    /// </summary>
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Surname = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ShirtNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    PreferredPosition = table.Column<int>(type: "INTEGER", nullable: false),
                    AlternativePositions = table.Column<string>(type: "TEXT", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Opponent = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchType = table.Column<int>(type: "INTEGER", nullable: false),
                    FormationType = table.Column<int>(type: "INTEGER", nullable: false),
                    SplitType = table.Column<int>(type: "INTEGER", nullable: false),
                    GameDurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    IsHomeGame = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScoreHome = table.Column<int>(type: "INTEGER", nullable: true),
                    ScoreAway = table.Column<int>(type: "INTEGER", nullable: true),
                    MatchState = table.Column<int>(type: "INTEGER", nullable: false),
                    ClockRunningSince = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClockAccumulatedSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    LivePeriodId = table.Column<int>(type: "INTEGER", nullable: true),
                    UnavailablePlayerIds = table.Column<string>(type: "TEXT", nullable: false),
                    GuestPlayerIds = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Games_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeasonId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameDurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultSplitType = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultFormation = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchDay = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchPreferences_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeasonSquadMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeasonId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsGuest = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonSquadMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeasonSquadMembers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeasonSquadMembers_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    IsPublic = table.Column<bool>(type: "INTEGER", nullable: false),
                    AuthorId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameComments_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameComments_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GamePeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodType = table.Column<int>(type: "INTEGER", nullable: false),
                    FormationTypeOverride = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAtSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    EndedAtSeconds = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamePeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GamePeriods_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameGoals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    ScorerId = table.Column<int>(type: "INTEGER", nullable: true),
                    AssisterId = table.Column<int>(type: "INTEGER", nullable: true),
                    GamePeriodId = table.Column<int>(type: "INTEGER", nullable: true),
                    AtSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    Minute = table.Column<int>(type: "INTEGER", nullable: true),
                    IsOwnGoal = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsOpponentGoal = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameGoals_GamePeriods_GamePeriodId",
                        column: x => x.GamePeriodId,
                        principalTable: "GamePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameGoals_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameGoals_Players_AssisterId",
                        column: x => x.AssisterId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GameGoals_Players_ScorerId",
                        column: x => x.ScorerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GamePlayerPositions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GamePeriodId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    SlotIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    IsSubstitute = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamePlayerPositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GamePlayerPositions_GamePeriods_GamePeriodId",
                        column: x => x.GamePeriodId,
                        principalTable: "GamePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GamePlayerPositions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameSubstitutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    GamePeriodId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerOffId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerOnId = table.Column<int>(type: "INTEGER", nullable: false),
                    AtSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    SlotIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSubstitutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameSubstitutions_GamePeriods_GamePeriodId",
                        column: x => x.GamePeriodId,
                        principalTable: "GamePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameSubstitutions_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameSubstitutions_Players_PlayerOffId",
                        column: x => x.PlayerOffId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameSubstitutions_Players_PlayerOnId",
                        column: x => x.PlayerOnId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameComments_AuthorId",
                table: "GameComments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_GameComments_GameId_CreatedAt",
                table: "GameComments",
                columns: new[] { "GameId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GameGoals_AssisterId",
                table: "GameGoals",
                column: "AssisterId");

            migrationBuilder.CreateIndex(
                name: "IX_GameGoals_GameId",
                table: "GameGoals",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameGoals_GamePeriodId",
                table: "GameGoals",
                column: "GamePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_GameGoals_ScorerId",
                table: "GameGoals",
                column: "ScorerId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePeriods_GameId",
                table: "GamePeriods",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePlayerPositions_GamePeriodId_PlayerId",
                table: "GamePlayerPositions",
                columns: new[] { "GamePeriodId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GamePlayerPositions_PlayerId",
                table: "GamePlayerPositions",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_SeasonId",
                table: "Games",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSubstitutions_GameId",
                table: "GameSubstitutions",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSubstitutions_GamePeriodId",
                table: "GameSubstitutions",
                column: "GamePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSubstitutions_PlayerOffId",
                table: "GameSubstitutions",
                column: "PlayerOffId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSubstitutions_PlayerOnId",
                table: "GameSubstitutions",
                column: "PlayerOnId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPreferences_SeasonId",
                table: "MatchPreferences",
                column: "SeasonId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_StartDate",
                table: "Seasons",
                column: "StartDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeasonSquadMembers_PlayerId",
                table: "SeasonSquadMembers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonSquadMembers_SeasonId_PlayerId",
                table: "SeasonSquadMembers",
                columns: new[] { "SeasonId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameComments");

            migrationBuilder.DropTable(
                name: "GameGoals");

            migrationBuilder.DropTable(
                name: "GamePlayerPositions");

            migrationBuilder.DropTable(
                name: "GameSubstitutions");

            migrationBuilder.DropTable(
                name: "MatchPreferences");

            migrationBuilder.DropTable(
                name: "SeasonSquadMembers");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "GamePeriods");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "Seasons");
        }
    }
}
