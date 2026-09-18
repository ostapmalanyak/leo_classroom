using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DownloadAll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadJob",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssignmentId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedByStudentId = table.Column<string>(type: "text", nullable: false),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ArtifactPath = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadJob", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJob_AssignmentId",
                schema: "leo_classroom",
                table: "DownloadJob",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJob_ExpiresAt",
                schema: "leo_classroom",
                table: "DownloadJob",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJob_Status_CreatedAt",
                schema: "leo_classroom",
                table: "DownloadJob",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadJob",
                schema: "leo_classroom");
        }
    }
}
