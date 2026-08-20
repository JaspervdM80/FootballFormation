using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class RecordInjuredAbsences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AbsencesRecorded",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InjuredPlayerIds",
                table: "Games",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Every match already played is settled, and there is no injury record to recover for
            // it — the flag it would have been read from has moved on. Marking them recorded is
            // what stops the first correction to an old scoreline stamping today's casualties into
            // a match from September. The condition is Game.IsComplete: Finished (MatchState 2), or
            // never run live (0) but carrying both halves of a score.
            migrationBuilder.Sql("""
                UPDATE Games
                SET AbsencesRecorded = 1
                WHERE MatchState = 2
                   OR (MatchState = 0 AND ScoreHome IS NOT NULL AND ScoreAway IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbsencesRecorded",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "InjuredPlayerIds",
                table: "Games");
        }
    }
}
