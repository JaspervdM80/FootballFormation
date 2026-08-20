using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchInjuries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameInjuries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    GamePeriodId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    AtSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    SlotIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameInjuries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameInjuries_GamePeriods_GamePeriodId",
                        column: x => x.GamePeriodId,
                        principalTable: "GamePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameInjuries_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameInjuries_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameInjuries_GameId_PlayerId",
                table: "GameInjuries",
                columns: new[] { "GameId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameInjuries_GamePeriodId",
                table: "GameInjuries",
                column: "GamePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_GameInjuries_PlayerId",
                table: "GameInjuries",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameInjuries");
        }
    }
}
