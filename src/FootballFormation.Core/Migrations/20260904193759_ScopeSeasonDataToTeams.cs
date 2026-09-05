using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class ScopeSeasonDataToTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Seasons_StartDate",
                table: "Seasons");

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "Trainings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "SeasonSquadMembers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "Seasons",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClubId",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "MatchPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Backfill before the indexes and foreign keys below, so every new required column holds a real team by the time
            // foreign_key_check runs at the end of the boot's migrate. The columns are NOT NULL, so an empty Teams table cannot be left
            // to the boot repair the way ScopeAdminsToTeams left a nullable one — a database restored from before clubs and teams
            // migrates the whole chain in one boot with the table still empty, so seed the team the app has always served first. Boot's
            // TeamService.EnsureSeededAsync then finds a club and does nothing. See docs/models/settings-and-users.md.
            migrationBuilder.Sql(
                "INSERT INTO Clubs (Name, LogoUrl, ThemeName) SELECT 'GJS', NULL, 'GJS' " +
                "WHERE NOT EXISTS (SELECT 1 FROM Clubs) " +
                "AND (EXISTS (SELECT 1 FROM Seasons) OR EXISTS (SELECT 1 FROM Players));");
            migrationBuilder.Sql(
                "INSERT INTO Teams (ClubId, Name) SELECT (SELECT MIN(Id) FROM Clubs), 'MO15-2' " +
                "WHERE NOT EXISTS (SELECT 1 FROM Teams) AND EXISTS (SELECT 1 FROM Clubs) " +
                "AND (EXISTS (SELECT 1 FROM Seasons) OR EXISTS (SELECT 1 FROM Players));");

            // Seasons and players anchor to the team and club directly; everything under a season derives its team from the season it
            // already hangs off, falling back to the one team so a game whose season somehow has none is still valid.
            migrationBuilder.Sql("UPDATE Seasons SET TeamId = (SELECT MIN(Id) FROM Teams) WHERE TeamId = 0;");
            migrationBuilder.Sql("UPDATE Players SET ClubId = (SELECT MIN(Id) FROM Clubs) WHERE ClubId = 0;");
            migrationBuilder.Sql(
                "UPDATE Games SET TeamId = COALESCE((SELECT TeamId FROM Seasons WHERE Seasons.Id = Games.SeasonId), " +
                "(SELECT MIN(Id) FROM Teams)) WHERE TeamId = 0;");
            migrationBuilder.Sql(
                "UPDATE Trainings SET TeamId = COALESCE((SELECT TeamId FROM Seasons WHERE Seasons.Id = Trainings.SeasonId), " +
                "(SELECT MIN(Id) FROM Teams)) WHERE TeamId = 0;");
            migrationBuilder.Sql(
                "UPDATE MatchPreferences SET TeamId = COALESCE((SELECT TeamId FROM Seasons WHERE Seasons.Id = MatchPreferences.SeasonId), " +
                "(SELECT MIN(Id) FROM Teams)) WHERE TeamId = 0;");
            migrationBuilder.Sql(
                "UPDATE SeasonSquadMembers SET TeamId = COALESCE((SELECT TeamId FROM Seasons WHERE Seasons.Id = SeasonSquadMembers.SeasonId), " +
                "(SELECT MIN(Id) FROM Teams)) WHERE TeamId = 0;");

            migrationBuilder.CreateIndex(
                name: "IX_Trainings_TeamId",
                table: "Trainings",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonSquadMembers_TeamId",
                table: "SeasonSquadMembers",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_TeamId_StartDate",
                table: "Seasons",
                columns: new[] { "TeamId", "StartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_ClubId",
                table: "Players",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPreferences_TeamId",
                table: "MatchPreferences",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_TeamId",
                table: "Games",
                column: "TeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_Games_Teams_TeamId",
                table: "Games",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatchPreferences_Teams_TeamId",
                table: "MatchPreferences",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Clubs_ClubId",
                table: "Players",
                column: "ClubId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Seasons_Teams_TeamId",
                table: "Seasons",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SeasonSquadMembers_Teams_TeamId",
                table: "SeasonSquadMembers",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Trainings_Teams_TeamId",
                table: "Trainings",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Games_Teams_TeamId",
                table: "Games");

            migrationBuilder.DropForeignKey(
                name: "FK_MatchPreferences_Teams_TeamId",
                table: "MatchPreferences");

            migrationBuilder.DropForeignKey(
                name: "FK_Players_Clubs_ClubId",
                table: "Players");

            migrationBuilder.DropForeignKey(
                name: "FK_Seasons_Teams_TeamId",
                table: "Seasons");

            migrationBuilder.DropForeignKey(
                name: "FK_SeasonSquadMembers_Teams_TeamId",
                table: "SeasonSquadMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_Trainings_Teams_TeamId",
                table: "Trainings");

            migrationBuilder.DropIndex(
                name: "IX_Trainings_TeamId",
                table: "Trainings");

            migrationBuilder.DropIndex(
                name: "IX_SeasonSquadMembers_TeamId",
                table: "SeasonSquadMembers");

            migrationBuilder.DropIndex(
                name: "IX_Seasons_TeamId_StartDate",
                table: "Seasons");

            migrationBuilder.DropIndex(
                name: "IX_Players_ClubId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_MatchPreferences_TeamId",
                table: "MatchPreferences");

            migrationBuilder.DropIndex(
                name: "IX_Games_TeamId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "Trainings");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "SeasonSquadMembers");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "MatchPreferences");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "Games");

            // Not unique, though Up dropped a unique one: by the time a rollback runs, two teams may have shared a start date, and a
            // unique index would fail to build over the duplicates the TeamId drop above has just merged. A down-migration cannot
            // restore a uniqueness the data no longer supports without losing rows.
            migrationBuilder.CreateIndex(
                name: "IX_Seasons_StartDate",
                table: "Seasons",
                column: "StartDate");
        }
    }
}
