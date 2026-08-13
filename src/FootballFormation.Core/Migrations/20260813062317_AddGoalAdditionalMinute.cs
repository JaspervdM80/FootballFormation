using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <summary>
    /// Splits stoppage time out of a goal's minute, so the timeline can order 35+2 before 36.
    /// <para>
    /// Deliberately not backfilled. A goal already on file stored the minute counted straight on
    /// past the end of the half, and nothing left in the row says whether a 37 in a 35-minute half
    /// was stoppage time or a minute typed in by hand on the result page. Zero leaves those goals
    /// reading and sorting exactly as they do today; only goals logged from here on carry the split.
    /// </para>
    /// </summary>
    public partial class AddGoalAdditionalMinute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdditionalMinute",
                table: "GameGoals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdditionalMinute",
                table: "GameGoals");
        }
    }
}
