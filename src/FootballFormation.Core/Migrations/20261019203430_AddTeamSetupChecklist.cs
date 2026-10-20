using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamSetupChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SetupChecklistHidden",
                table: "Teams",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DefaultsConfirmed",
                table: "MatchPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // A team already playing is set up, and a row that differs from the hardcoded defaults was saved by someone.
            migrationBuilder.Sql("UPDATE Teams SET SetupChecklistHidden = 1 WHERE EXISTS (SELECT 1 FROM Games WHERE Games.TeamId = Teams.Id);");
            migrationBuilder.Sql(
                "UPDATE MatchPreferences SET DefaultsConfirmed = 1 " +
                "WHERE EXISTS (SELECT 1 FROM Games WHERE Games.TeamId = MatchPreferences.TeamId) " +
                "OR GameDurationMinutes <> 60 OR DefaultSplitType <> 0 OR DefaultFormation <> 0 OR MatchDay <> 6;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SetupChecklistHidden",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "DefaultsConfirmed",
                table: "MatchPreferences");
        }
    }
}
