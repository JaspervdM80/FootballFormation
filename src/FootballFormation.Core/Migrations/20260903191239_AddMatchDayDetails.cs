using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchDayDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Games",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DressingRoom",
                table: "Games",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DressingRoomDuty",
                table: "Games",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldName",
                table: "Games",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlagDuty",
                table: "Games",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "MeetTime",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SportsPark",
                table: "Games",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "WarmUpTime",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WashDuty",
                table: "Games",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "City",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "DressingRoom",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "DressingRoomDuty",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FieldName",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FlagDuty",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "MeetTime",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "SportsPark",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "WarmUpTime",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "WashDuty",
                table: "Games");
        }
    }
}
