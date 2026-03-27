using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <summary>
    /// Consolidates player positions:
    ///   LCB(2), RCB(4) → CB(3)
    ///   LCDM(9), RCDM(10) → CDM(11)
    ///   LCM(12), RCM(14) → CM(13)
    ///   LF(24) → LW(21)
    ///   RF(25) → RW(22)
    ///   CF(26) → ST(29)
    /// </summary>
    public partial class ConsolidatePlayerPositions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Migrate PreferredPosition (integer column) in Players
            migrationBuilder.Sql("UPDATE Players SET PreferredPosition = 3  WHERE PreferredPosition IN (2, 4);");   // LCB, RCB → CB
            migrationBuilder.Sql("UPDATE Players SET PreferredPosition = 11 WHERE PreferredPosition IN (9, 10);");  // LCDM, RCDM → CDM
            migrationBuilder.Sql("UPDATE Players SET PreferredPosition = 13 WHERE PreferredPosition IN (12, 14);"); // LCM, RCM → CM
            migrationBuilder.Sql("UPDATE Players SET PreferredPosition = 21 WHERE PreferredPosition = 24;");        // LF → LW
            migrationBuilder.Sql("UPDATE Players SET PreferredPosition = 22 WHERE PreferredPosition = 25;");        // RF → RW
            migrationBuilder.Sql("UPDATE Players SET PreferredPosition = 29 WHERE PreferredPosition = 26;");        // CF → ST

            // Migrate Position (integer column) in GamePlayerPositions
            migrationBuilder.Sql("UPDATE GamePlayerPositions SET Position = 3  WHERE Position IN (2, 4);");
            migrationBuilder.Sql("UPDATE GamePlayerPositions SET Position = 11 WHERE Position IN (9, 10);");
            migrationBuilder.Sql("UPDATE GamePlayerPositions SET Position = 13 WHERE Position IN (12, 14);");
            migrationBuilder.Sql("UPDATE GamePlayerPositions SET Position = 21 WHERE Position = 24;");
            migrationBuilder.Sql("UPDATE GamePlayerPositions SET Position = 22 WHERE Position = 25;");
            migrationBuilder.Sql("UPDATE GamePlayerPositions SET Position = 29 WHERE Position = 26;");

            // Migrate AlternativePositions (comma-separated integer string) in Players.
            // Wrap with commas for safe boundary matching, do all replacements, then trim.
            migrationBuilder.Sql(@"
                UPDATE Players SET AlternativePositions =
                    TRIM(
                        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            ',' || AlternativePositions || ',',
                            ',2,',  ',3,'),
                            ',4,',  ',3,'),
                            ',9,',  ',11,'),
                            ',10,', ',11,'),
                            ',12,', ',13,'),
                            ',14,', ',13,'),
                            ',24,', ',21,'),
                            ',25,', ',22,'),
                            ',26,', ',29,'),
                        ','
                    )
                WHERE AlternativePositions IS NOT NULL AND AlternativePositions != '';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data migration — cannot be reversed automatically.
            // The removed positions no longer exist in the enum.
        }
    }
}
