using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonSquads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: the scaffolder emitted DropColumn("IsGuest") FIRST. It was moved to the end by
            // hand — the backfill below is the last statement that can read Players.IsGuest, and
            // dropping it first would silently produce an empty squad table and an app in which
            // nobody is in any roster. Do not reorder these four blocks.

            migrationBuilder.CreateTable(
                name: "SeasonSquadMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeasonId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsGuest = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonSquadMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeasonSquadMembers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeasonSquadMembers_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeasonSquadMembers_PlayerId",
                table: "SeasonSquadMembers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonSquadMembers_SeasonId_PlayerId",
                table: "SeasonSquadMembers",
                columns: new[] { "SeasonId", "PlayerId" },
                unique: true);

            // --- Backfill (hand-written) -------------------------------------------------------
            // Every player joins every existing season's squad, carrying the old global guest flag
            // over. That is the only interpretation the old data supports: before seasons existed,
            // "the squad" was global. Today that is 1 season x 18 players = 18 rows, 3 of them
            // guests. No-op on a fresh install (no seasons, no players).
            //
            // Seasons created AFTER this migration start with an empty squad on purpose — they get
            // filled by "copy squad from <previous season>" on the squad page.
            //
            // The FKs were created inline with the table above: Seasons and Players already exist,
            // and this SELECT can only produce valid pairs.
            migrationBuilder.Sql("""
                INSERT INTO SeasonSquadMembers (SeasonId, PlayerId, IsGuest)
                SELECT s.Id, p.Id, p.IsGuest
                FROM Seasons s
                CROSS JOIN Players p;
                """);
            // --- end backfill ------------------------------------------------------------------

            // Rebuilds the Players table in SQLite. Three tables hold FKs into it, and EF disables
            // foreign_keys around the rebuild without re-checking afterwards — verify with
            // PRAGMA foreign_key_check after applying. See docs/patterns.md.
            migrationBuilder.DropColumn(
                name: "IsGuest",
                table: "Players");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsGuest",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Best effort: the current season's flags become the global ones again. Per-season
            // nuance is genuinely lost on a downgrade — that is precisely what this feature added.
            migrationBuilder.Sql("""
                UPDATE Players SET IsGuest = COALESCE((
                    SELECT m.IsGuest FROM SeasonSquadMembers m
                    JOIN Seasons s ON s.Id = m.SeasonId
                    WHERE m.PlayerId = Players.Id
                    ORDER BY s.IsCurrent DESC, s.StartDate DESC
                    LIMIT 1
                ), 0);
                """);

            migrationBuilder.DropTable(
                name: "SeasonSquadMembers");
        }
    }
}
