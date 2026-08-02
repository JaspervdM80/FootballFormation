using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class SeasonSpecificMatchPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeasonId",
                table: "MatchPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // The singleton row becomes the current season's row, and every other season gets a
            // copy of it — so no season silently starts on the hardcoded 4-4-2 / 60 minutes, and
            // past games keep the settings they were actually created under.
            migrationBuilder.Sql(@"
                UPDATE MatchPreferences
                SET SeasonId = COALESCE(
                    (SELECT Id FROM Seasons WHERE IsCurrent = 1 ORDER BY StartDate DESC LIMIT 1),
                    (SELECT Id FROM Seasons ORDER BY StartDate DESC LIMIT 1))
                WHERE SeasonId = 0 AND EXISTS (SELECT 1 FROM Seasons);");

            // A database with preferences but no seasons at all cannot attach the row to anything.
            // Dropping it costs the four defaults; the service recreates them on first read.
            migrationBuilder.Sql("DELETE FROM MatchPreferences WHERE SeasonId = 0;");

            // Copied through a temp table rather than selecting from MatchPreferences while
            // inserting into it — SQLite does not promise the SELECT is finished first.
            migrationBuilder.Sql(@"
                CREATE TEMP TABLE _prefs_seed AS
                SELECT SeasonId, GameDurationMinutes, DefaultSplitType, DefaultFormation, MatchDay
                FROM MatchPreferences ORDER BY Id LIMIT 1;");

            migrationBuilder.Sql(@"
                INSERT INTO MatchPreferences (SeasonId, GameDurationMinutes, DefaultSplitType, DefaultFormation, MatchDay)
                SELECT s.Id, d.GameDurationMinutes, d.DefaultSplitType, d.DefaultFormation, d.MatchDay
                FROM Seasons s
                CROSS JOIN _prefs_seed d
                WHERE s.Id <> d.SeasonId;");

            migrationBuilder.Sql("DROP TABLE _prefs_seed;");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPreferences_SeasonId",
                table: "MatchPreferences",
                column: "SeasonId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MatchPreferences_Seasons_SeasonId",
                table: "MatchPreferences",
                column: "SeasonId",
                principalTable: "Seasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to a singleton: keep the current season's row, or the oldest one.
            migrationBuilder.Sql(@"
                DELETE FROM MatchPreferences
                WHERE Id <> COALESCE(
                    (SELECT p.Id FROM MatchPreferences p
                     JOIN Seasons s ON s.Id = p.SeasonId
                     WHERE s.IsCurrent = 1 LIMIT 1),
                    (SELECT MIN(Id) FROM MatchPreferences));");

            migrationBuilder.DropForeignKey(
                name: "FK_MatchPreferences_Seasons_SeasonId",
                table: "MatchPreferences");

            migrationBuilder.DropIndex(
                name: "IX_MatchPreferences_SeasonId",
                table: "MatchPreferences");

            migrationBuilder.DropColumn(
                name: "SeasonId",
                table: "MatchPreferences");
        }
    }
}
