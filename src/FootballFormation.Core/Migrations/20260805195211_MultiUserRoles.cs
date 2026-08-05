using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FootballFormation.Core.Migrations
{
    /// <summary>
    /// AdminUsers becomes Users: named accounts with a role, rather than one row that is the admin
    /// by virtue of existing.
    /// <para>
    /// Hand-written. The scaffolded version dropped AdminUsers and created Users, which on the live
    /// database would have discarded the real admin's password hash and left the startup seeder to
    /// recreate the account as admin/admin — a silent production password reset. This renames the
    /// table and backfills the new columns instead, so the existing row survives intact.
    /// </para>
    /// </summary>
    public partial class MultiUserRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "AdminUsers",
                newName: "Users");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "Users",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Accounts that predate display names are called by their login until someone renames
            // them — better than a blank name in the app bar and the user list.
            migrationBuilder.Sql(
                "UPDATE Users SET DisplayName = Username WHERE DisplayName = '';");

            // Every account needs its own stamp, or one changed password would invalidate everyone's
            // session. randomblob(16) hex-encoded matches Guid.ToString(\"N\") in shape and length.
            migrationBuilder.Sql(
                "UPDATE Users SET SecurityStamp = lower(hex(randomblob(16))) WHERE SecurityStamp = '';");

            // Two accounts sharing a login would make the credential check ambiguous. Created after
            // the backfill so it is only ever validated against settled data.
            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "Users");

            migrationBuilder.RenameTable(
                name: "Users",
                newName: "AdminUsers");
        }
    }
}
