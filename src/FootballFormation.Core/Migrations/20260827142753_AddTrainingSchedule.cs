using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FromSchedule",
                table: "Trainings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // The start time is gone from the app, so a row keeping an invisible 19:30 would order and group against a value nothing
            // renders. substr takes the date part whichever separator it was written with; the space is what EF's SQLite mapping emits.
            migrationBuilder.Sql("UPDATE Trainings SET Date = substr(Date, 1, 10) || ' 00:00:00';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the column goes: the times the Up wiped are not coming back.
            migrationBuilder.DropColumn(
                name: "FromSchedule",
                table: "Trainings");
        }
    }
}
