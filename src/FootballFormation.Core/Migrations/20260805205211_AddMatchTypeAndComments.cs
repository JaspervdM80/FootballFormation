using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <summary>
    /// Games gain a type (competition/cup/practice) and a comment thread, and lose the single Notes
    /// field the comments replace.
    /// <para>
    /// Reordered by hand. The scaffolded version dropped Notes first, before GameComments existed,
    /// which would have thrown away whatever coaches had already typed into it. Here the table is
    /// created first and every non-empty Notes value is carried over as one admin-only comment, so
    /// nothing written before this migration disappears.
    /// </para>
    /// </summary>
    public partial class AddMatchTypeAndComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0 is MatchType.Competition, so every existing fixture reads as a competition match.
            migrationBuilder.AddColumn<int>(
                name: "MatchType",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GameComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    IsPublic = table.Column<bool>(type: "INTEGER", nullable: false),
                    AuthorId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameComments_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameComments_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameComments_AuthorId",
                table: "GameComments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_GameComments_GameId_CreatedAt",
                table: "GameComments",
                columns: new[] { "GameId", "CreatedAt" });

            // Notes carried over as private (IsPublic 0) — it was never shown to anyone, so
            // publishing it here would put text on the public site nobody chose to publish.
            // No author: the old field never recorded who wrote it. Dated to the match itself,
            // which is the closest thing to a real timestamp available.
            migrationBuilder.Sql("""
                INSERT INTO GameComments (GameId, Body, IsPublic, AuthorId, CreatedAt)
                SELECT Id, Notes, 0, NULL, Date
                FROM Games
                WHERE Notes IS NOT NULL AND TRIM(Notes) <> '';
                """);

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Games");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Games",
                type: "TEXT",
                nullable: true);

            // Notes only ever held one value per game, so going back can only keep one comment:
            // the oldest private one, which is what Up would have put there.
            migrationBuilder.Sql("""
                UPDATE Games SET Notes = (
                    SELECT c.Body FROM GameComments c
                    WHERE c.GameId = Games.Id AND c.IsPublic = 0
                    ORDER BY c.CreatedAt, c.Id
                    LIMIT 1
                );
                """);

            migrationBuilder.DropTable(
                name: "GameComments");

            migrationBuilder.DropColumn(
                name: "MatchType",
                table: "Games");
        }
    }
}
