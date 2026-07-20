using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <summary>
    /// Consolidates more player positions:
    ///   LWB(6) → LB(1)
    ///   RWB(7) → RB(5)
    ///   LCAM(17) → CAM(19)
    ///   RCAM(18) → CAM(19)
    ///   LST(27) → ST(29)
    ///   RST(28) → ST(29)
    /// </summary>
    public partial class ConsolidatePositionsRound2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Migrate PreferredPosition (integer column) in Players
            migrationBuilder.Sql("UPDATE Players SET PreferredPosition = 1  WHERE PreferredPosition = 6;");         // LWB → LB
            migrationBuilder.Sql("UPDATE Players SET PreferredPosition = 5  WHERE PreferredPosition = 7;");         // RWB → RB
            migrationBuilder.Sql("UPDATE Players SET PreferredPosition = 19 WHERE PreferredPosition IN (17, 18);"); // LCAM, RCAM → CAM
            migrationBuilder.Sql("UPDATE Players SET PreferredPosition = 29 WHERE PreferredPosition IN (27, 28);"); // LST, RST → ST

            // Migrate Position (integer column) in GamePlayerPositions
            migrationBuilder.Sql("UPDATE GamePlayerPositions SET Position = 1  WHERE Position = 6;");
            migrationBuilder.Sql("UPDATE GamePlayerPositions SET Position = 5  WHERE Position = 7;");
            migrationBuilder.Sql("UPDATE GamePlayerPositions SET Position = 19 WHERE Position IN (17, 18);");
            migrationBuilder.Sql("UPDATE GamePlayerPositions SET Position = 29 WHERE Position IN (27, 28);");

            // Migrate AlternativePositions (comma-separated integer string) in Players
            migrationBuilder.Sql(@"
                UPDATE Players SET AlternativePositions =
                    TRIM(
                        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            ',' || AlternativePositions || ',',
                            ',6,',  ',1,'),
                            ',7,',  ',5,'),
                            ',17,', ',19,'),
                            ',18,', ',19,'),
                            ',27,', ',29,'),
                            ',28,', ',29,'),
                        ','
                    )
                WHERE AlternativePositions IS NOT NULL AND AlternativePositions != '';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data migration — cannot be reversed automatically.
        }
    }
}
