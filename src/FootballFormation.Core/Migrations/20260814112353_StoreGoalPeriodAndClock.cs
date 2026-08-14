using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <summary>
    /// Gives a goal the two facts a substitution already carries — the half it happened in and the
    /// reading on the match clock — so the minute shown is derived rather than frozen, and
    /// correcting a half's timings corrects its goals with it.
    /// <para>
    /// Backfilled for exactly the rows that can be, which is the ones carrying an overrun.
    /// <c>AdditionalMinute &gt; 0</c> is a row saying outright that it was scored in stoppage time,
    /// so nothing has to be guessed: the minute names the half, the half names its kick-off, and
    /// the clock reading falls out of the two. Those goals go on reading <c>30+2</c> afterwards,
    /// which is the point — <c>32</c> is the 32nd minute, two minutes into a second half, and not
    /// what happened.
    /// </para>
    /// <para>
    /// Every other old row is deliberately left alone. A goal with <c>AdditionalMinute = 0</c> says
    /// nothing about which half it belongs to — a stored <c>37</c> could be a minute typed in by
    /// hand on the result page — so it keeps <c>Minute</c> and goes on sorting where it always has.
    /// Guessing a half for those is how the frozen minute went wrong in the first place.
    /// </para>
    /// <para>
    /// The order below is not what SQLite runs. EF folds the <c>DropColumn</c> into the table
    /// rebuild that <c>AddForeignKey</c> already forces — the temp table is simply created without
    /// the column — so this is one rebuild whatever order the operations are written in. What that
    /// leaves is a migration that <em>cannot be retried</em>: the two <c>ADD COLUMN</c>s commit in
    /// their own transaction, and a run interrupted after that point has not recorded itself in
    /// <c>__EFMigrationsHistory</c>, so the next boot starts again from the top and dies on
    /// <c>duplicate column name: AtSeconds</c>. Recovery is the pre-migration snapshot
    /// <c>Program.cs</c> takes, which is the only thing standing behind any of this.
    /// </para>
    /// </summary>
    public partial class StoreGoalPeriodAndClock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AtSeconds",
                table: "GameGoals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GamePeriodId",
                table: "GameGoals",
                type: "INTEGER",
                nullable: true);

            // The one set of rows that can be moved across without guessing: a goal with an
            // overrun on it says outright that it was scored in stoppage time, so which half it
            // belongs to follows from its minute and the clock reading follows from the half's
            // own kick-off. Everything else keeps Minute and is left alone — see the remarks.
            //
            // Reconstructed to the minute, which is all the row ever held. Read back through
            // MatchClockReport it lands on the same 30+2 it was written as: the half's clock is
            // capped at halfSeconds and the remainder is counted alongside from 1.
            //
            // A goal whose half was never kicked off, or whose game has no duration, selects NULL
            // for both columns — which is exactly the state it would have had with no backfill.
            migrationBuilder.Sql("""
                UPDATE GameGoals
                SET AtSeconds = (
                        SELECT p.StartedAtSeconds + (g.GameDurationMinutes * 60 / 2)
                             + ((GameGoals.AdditionalMinute - 1) * 60)
                        FROM GamePeriods p
                        JOIN Games g ON g.Id = p.GameId
                        WHERE p.GameId = GameGoals.GameId
                          AND g.GameDurationMinutes > 0
                          AND p.StartedAtSeconds IS NOT NULL
                          AND (CASE WHEN p.PeriodType IN (0, 2, 3) THEN 0 ELSE 1 END)
                            = (CASE WHEN GameGoals.Minute * 60
                                       <= (g.GameDurationMinutes * 60 / 2) THEN 0 ELSE 1 END)
                        ORDER BY p.StartedAtSeconds
                        LIMIT 1),
                    GamePeriodId = (
                        SELECT p.Id
                        FROM GamePeriods p
                        JOIN Games g ON g.Id = p.GameId
                        WHERE p.GameId = GameGoals.GameId
                          AND g.GameDurationMinutes > 0
                          AND p.StartedAtSeconds IS NOT NULL
                          AND (CASE WHEN p.PeriodType IN (0, 2, 3) THEN 0 ELSE 1 END)
                            = (CASE WHEN GameGoals.Minute * 60
                                       <= (g.GameDurationMinutes * 60 / 2) THEN 0 ELSE 1 END)
                        ORDER BY p.StartedAtSeconds
                        LIMIT 1)
                WHERE AdditionalMinute > 0 AND Minute IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_GameGoals_GamePeriodId",
                table: "GameGoals",
                column: "GamePeriodId");

            migrationBuilder.AddForeignKey(
                name: "FK_GameGoals_GamePeriods_GamePeriodId",
                table: "GameGoals",
                column: "GamePeriodId",
                principalTable: "GamePeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropColumn(
                name: "AdditionalMinute",
                table: "GameGoals");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdditionalMinute",
                table: "GameGoals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.DropForeignKey(
                name: "FK_GameGoals_GamePeriods_GamePeriodId",
                table: "GameGoals");

            migrationBuilder.DropIndex(
                name: "IX_GameGoals_GamePeriodId",
                table: "GameGoals");

            migrationBuilder.DropColumn(
                name: "AtSeconds",
                table: "GameGoals");

            migrationBuilder.DropColumn(
                name: "GamePeriodId",
                table: "GameGoals");
        }
    }
}
