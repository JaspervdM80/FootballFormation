using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveMatchTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GameGoals_Players_ScorerId",
                table: "GameGoals");

            migrationBuilder.AddColumn<int>(
                name: "ClockAccumulatedSeconds",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClockRunningSince",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LivePeriodId",
                table: "Games",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MatchState",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EndedAtSeconds",
                table: "GamePeriods",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartedAtSeconds",
                table: "GamePeriods",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ScorerId",
                table: "GameGoals",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<bool>(
                name: "IsOpponentGoal",
                table: "GameGoals",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GameSubstitutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    GamePeriodId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerOffId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerOnId = table.Column<int>(type: "INTEGER", nullable: false),
                    AtSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    SlotIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSubstitutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameSubstitutions_GamePeriods_GamePeriodId",
                        column: x => x.GamePeriodId,
                        principalTable: "GamePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameSubstitutions_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameSubstitutions_Players_PlayerOffId",
                        column: x => x.PlayerOffId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameSubstitutions_Players_PlayerOnId",
                        column: x => x.PlayerOnId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameSubstitutions_GameId",
                table: "GameSubstitutions",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSubstitutions_GamePeriodId",
                table: "GameSubstitutions",
                column: "GamePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSubstitutions_PlayerOffId",
                table: "GameSubstitutions",
                column: "PlayerOffId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSubstitutions_PlayerOnId",
                table: "GameSubstitutions",
                column: "PlayerOnId");

            migrationBuilder.AddForeignKey(
                name: "FK_GameGoals_Players_ScorerId",
                table: "GameGoals",
                column: "ScorerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GameGoals_Players_ScorerId",
                table: "GameGoals");

            migrationBuilder.DropTable(
                name: "GameSubstitutions");

            migrationBuilder.DropColumn(
                name: "ClockAccumulatedSeconds",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ClockRunningSince",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "LivePeriodId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "MatchState",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "EndedAtSeconds",
                table: "GamePeriods");

            migrationBuilder.DropColumn(
                name: "StartedAtSeconds",
                table: "GamePeriods");

            migrationBuilder.DropColumn(
                name: "IsOpponentGoal",
                table: "GameGoals");

            migrationBuilder.AlterColumn<int>(
                name: "ScorerId",
                table: "GameGoals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GameGoals_Players_ScorerId",
                table: "GameGoals",
                column: "ScorerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
