using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <summary>
    /// Hand-edited: the scaffolder cannot know that the unique index it creates may be violated by
    /// data already in the database. A lineup written before the app rebuilt periods wholesale
    /// could list a player twice in one period — which is why PlannedChangesReport still uses
    /// TryAdd — so the duplicates are collapsed first or the migration fails on startup and the
    /// app never comes up. Keep the de-duplication step if this migration is ever regenerated.
    /// </summary>
    public partial class AddMustChangePasswordAndLineupUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GamePlayerPositions_GamePeriodId",
                table: "GamePlayerPositions");

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Oldest row per (period, player) wins: it is the one the rest of the lineup was built
            // around, and a later duplicate is the artefact.
            migrationBuilder.Sql("""
                DELETE FROM GamePlayerPositions
                WHERE Id NOT IN (
                    SELECT MIN(Id) FROM GamePlayerPositions GROUP BY GamePeriodId, PlayerId
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_GamePlayerPositions_GamePeriodId_PlayerId",
                table: "GamePlayerPositions",
                columns: new[] { "GamePeriodId", "PlayerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GamePlayerPositions_GamePeriodId_PlayerId",
                table: "GamePlayerPositions");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_GamePlayerPositions_GamePeriodId",
                table: "GamePlayerPositions",
                column: "GamePeriodId");
        }
    }
}
