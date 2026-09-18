using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GitCredential : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "GitCredentialIssuedAt",
                schema: "leo_classroom",
                table: "User",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GitCredentialIssuedAt",
                schema: "leo_classroom",
                table: "User");
        }
    }
}
