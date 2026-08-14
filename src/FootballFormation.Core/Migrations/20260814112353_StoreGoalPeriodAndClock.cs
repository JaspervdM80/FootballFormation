using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <summary>
    /// Gives a goal the two facts a substitution already carries — the half it happened in and the
    /// reading on the match clock — so the minute shown is derived rather than frozen, and
    /// correcting a half's timings corrects its goals with it.
    /// <para>
    /// Deliberately not backfilled, and <c>AdditionalMinute</c> is dropped rather than folded into
    /// anything. Nothing left in an old row says which half a stored <c>37</c> belonged to, so
    /// those goals keep <c>Minute</c> and go on sorting where they always have; only goals logged
    /// from here on carry a clock. Folding the overrun back in would have been worse than losing
    /// it — a <c>30+2</c> counted on to <c>32</c> sorts past a goal in the 31st minute of the
    /// second half, which is the bug the pair was introduced to fix.
    /// </para>
    /// <para>
    /// <strong>It does lose something.</strong> A goal logged in stoppage time between
    /// <c>AddGoalAdditionalMinute</c> shipping and this migration has a real overrun on the row,
    /// and afterwards reads as the capped minute — <c>30+2</c> becomes <c>30'</c>. Only the
    /// display: the minute it sorts on is unchanged. That window is about a day, and the
    /// alternative was a reconstruction dressed up as a reading.
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
