using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeasonId",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                });

            // --- Backfill (hand-written) -------------------------------------------------------
            // Runs between CreateTable and AddForeignKey on purpose: the FK must only be created
            // once every Games.SeasonId is valid. SQLite migrations are transactional, so the
            // column, the seasons and the assignment all land together or not at all.
            //
            // Season windows are 1 July - 30 June (KNVB amateur season), matching
            // Season.StartMonth and Season.CreateFor. All three statements are no-ops on a fresh
            // database; SeasonService.EnsureCurrentSeasonAsync seeds the first season in that case.
            //
            // Games.Date is stored as TEXT ("2026-03-14 00:00:00"), so strftime works directly.

            // One season per distinct season-year present in Games.
            migrationBuilder.Sql("""
                INSERT INTO Seasons (Name, StartDate, EndDate, IsCurrent)
                SELECT
                    y.StartYear || '/' || substr(CAST(y.StartYear + 1 AS TEXT), 3, 2),
                    y.StartYear || '-07-01 00:00:00',
                    (y.StartYear + 1) || '-06-30 00:00:00',
                    0
                FROM (
                    SELECT DISTINCT
                        CAST(strftime('%Y', Date) AS INTEGER)
                          - (CASE WHEN CAST(strftime('%m', Date) AS INTEGER) >= 7 THEN 0 ELSE 1 END)
                        AS StartYear
                    FROM Games
                ) AS y;
                """);

            // Assign every game. Matched on the derived season year rather than a BETWEEN over the
            // dates, so this is the exact inverse of the INSERT above and cannot disagree with it
            // on a window boundary or a time-of-day component.
            migrationBuilder.Sql("""
                UPDATE Games
                SET SeasonId = (
                    SELECT s.Id FROM Seasons s
                    WHERE CAST(strftime('%Y', s.StartDate) AS INTEGER) =
                          CAST(strftime('%Y', Games.Date) AS INTEGER)
                            - (CASE WHEN CAST(strftime('%m', Games.Date) AS INTEGER) >= 7 THEN 0 ELSE 1 END)
                );
                """);

            // The most recent season becomes the current one.
            migrationBuilder.Sql("""
                UPDATE Seasons SET IsCurrent = 1
                WHERE Id = (SELECT Id FROM Seasons ORDER BY StartDate DESC LIMIT 1);
                """);
            // --- end backfill ------------------------------------------------------------------

            migrationBuilder.CreateIndex(
                name: "IX_Games_SeasonId",
                table: "Games",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_StartDate",
                table: "Seasons",
                column: "StartDate",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Games_Seasons_SeasonId",
                table: "Games",
                column: "SeasonId",
                principalTable: "Seasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Games_Seasons_SeasonId",
                table: "Games");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropIndex(
                name: "IX_Games_SeasonId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "SeasonId",
                table: "Games");
        }
    }
}
