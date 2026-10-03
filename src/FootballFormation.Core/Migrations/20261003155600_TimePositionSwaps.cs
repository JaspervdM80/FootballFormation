using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class TimePositionSwaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GamePositionSwaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    GamePeriodId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerAId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerBId = table.Column<int>(type: "INTEGER", nullable: false),
                    AtSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamePositionSwaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GamePositionSwaps_GamePeriods_GamePeriodId",
                        column: x => x.GamePeriodId,
                        principalTable: "GamePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GamePositionSwaps_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GamePositionSwaps_Players_PlayerAId",
                        column: x => x.PlayerAId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GamePositionSwaps_Players_PlayerBId",
                        column: x => x.PlayerBId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GamePositionSwaps_GameId",
                table: "GamePositionSwaps",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePositionSwaps_GamePeriodId",
                table: "GamePositionSwaps",
                column: "GamePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePositionSwaps_PlayerAId",
                table: "GamePositionSwaps",
                column: "PlayerAId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePositionSwaps_PlayerBId",
                table: "GamePositionSwaps",
                column: "PlayerBId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GamePositionSwaps");
        }
    }
}
