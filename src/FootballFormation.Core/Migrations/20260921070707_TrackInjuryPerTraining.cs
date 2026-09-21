using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class TrackInjuryPerTraining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AbsencesRecorded",
                table: "Trainings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InjuredPlayerIds",
                table: "Trainings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "InjuredSince",
                table: "SeasonSquadMembers",
                type: "TEXT",
                nullable: true);

            // Deploy day, not the start of the injury nobody recorded: a standing flag with no date behind it must not reach back and
            // mark a player absent from sessions she was at. Local time, because that is what the column holds everywhere else.
            migrationBuilder.Sql(
                "UPDATE SeasonSquadMembers SET InjuredSince = datetime('now', 'localtime') WHERE IsInjured = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbsencesRecorded",
                table: "Trainings");

            migrationBuilder.DropColumn(
                name: "InjuredPlayerIds",
                table: "Trainings");

            migrationBuilder.DropColumn(
                name: "InjuredSince",
                table: "SeasonSquadMembers");
        }
    }
}
