using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DidNotTakePlace",
                table: "Trainings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstTrainingDate",
                table: "MatchPreferences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTrainingDate",
                table: "MatchPreferences",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DidNotTakePlace",
                table: "Trainings");

            migrationBuilder.DropColumn(
                name: "FirstTrainingDate",
                table: "MatchPreferences");

            migrationBuilder.DropColumn(
                name: "LastTrainingDate",
                table: "MatchPreferences");
        }
    }
}
