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
    /// anything. Nothing left in an old row says which half a stored <c>37</c> belonged to or
    /// whether it was stoppage time, so those goals keep <c>Minute</c> and go on reading and
    /// sorting exactly as they do today; only goals logged from here on carry a clock.
    /// </para>
    /// <para>
    /// The drop is last on purpose. Both operations rebuild the table on SQLite, and a rebuild is
    /// not transactional — leaving it until the new columns and the foreign key are in place means
    /// a half-applied run has lost nothing that the next attempt needs.
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
