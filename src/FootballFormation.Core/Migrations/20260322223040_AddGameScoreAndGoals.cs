using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddGameScoreAndGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScoreAway",
                table: "Games",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoreHome",
                table: "Games",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GameGoals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    ScorerId = table.Column<int>(type: "INTEGER", nullable: false),
                    AssisterId = table.Column<int>(type: "INTEGER", nullable: true),
                    Minute = table.Column<int>(type: "INTEGER", nullable: true),
                    IsOwnGoal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameGoals_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameGoals_Players_AssisterId",
                        column: x => x.AssisterId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GameGoals_Players_ScorerId",
                        column: x => x.ScorerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameGoals_AssisterId",
                table: "GameGoals",
                column: "AssisterId");

            migrationBuilder.CreateIndex(
                name: "IX_GameGoals_GameId",
                table: "GameGoals",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameGoals_ScorerId",
                table: "GameGoals",
                column: "ScorerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameGoals");

            migrationBuilder.DropColumn(
                name: "ScoreAway",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ScoreHome",
                table: "Games");
        }
    }
}
