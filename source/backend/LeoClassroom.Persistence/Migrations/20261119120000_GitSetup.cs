using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <summary>
    ///     The per-user flags of the one-time git login setup.
    /// </summary>
    public partial class GitSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "GitSetupConfiguredAt",
                schema: "leo_classroom",
                table: "User",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "GitSetupAuthorizedAt",
                schema: "leo_classroom",
                table: "User",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitSetupToken",
                schema: "leo_classroom",
                table: "User",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GitSetupToken",
                schema: "leo_classroom",
                table: "User");

            migrationBuilder.DropColumn(
                name: "GitSetupAuthorizedAt",
                schema: "leo_classroom",
                table: "User");

            migrationBuilder.DropColumn(
                name: "GitSetupConfiguredAt",
                schema: "leo_classroom",
                table: "User");
        }
    }
}
