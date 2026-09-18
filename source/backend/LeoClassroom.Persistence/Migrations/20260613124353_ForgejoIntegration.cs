using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ForgejoIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ForgejoOrg",
                schema: "leo_classroom",
                table: "Course",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Assignment",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Deadline = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    DeadlineKind = table.Column<string>(type: "text", nullable: false),
                    HardDeadlineRevokesRead = table.Column<bool>(type: "boolean", nullable: false),
                    StarterSourceKind = table.Column<string>(type: "text", nullable: false),
                    StarterRepoUrl = table.Column<string>(type: "text", nullable: true),
                    ArchivePath = table.Column<string>(type: "text", nullable: true),
                    ReadmeMarkdown = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assignment_Course_CourseId",
                        column: x => x.CourseId,
                        principalSchema: "leo_classroom",
                        principalTable: "Course",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookEvent",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: true),
                    RepoOwner = table.Column<string>(type: "text", nullable: true),
                    RepoName = table.Column<string>(type: "text", nullable: true),
                    ReceivedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    CommitterDate = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Acceptance",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssignmentId = table.Column<long>(type: "bigint", nullable: false),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    RepoOwner = table.Column<string>(type: "text", nullable: false),
                    RepoName = table.Column<string>(type: "text", nullable: false),
                    AcceptedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acceptance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Acceptance_Assignment_AssignmentId",
                        column: x => x.AssignmentId,
                        principalSchema: "leo_classroom",
                        principalTable: "Assignment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Acceptance_User_StudentId",
                        column: x => x.StudentId,
                        principalSchema: "leo_classroom",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Acceptance_AssignmentId_StudentId",
                schema: "leo_classroom",
                table: "Acceptance",
                columns: new[] { "AssignmentId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Acceptance_StudentId",
                schema: "leo_classroom",
                table: "Acceptance",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Assignment_CourseId_Slug",
                schema: "leo_classroom",
                table: "Assignment",
                columns: new[] { "CourseId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvent_ReceivedAt",
                schema: "leo_classroom",
                table: "WebhookEvent",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvent_RepoOwner_RepoName",
                schema: "leo_classroom",
                table: "WebhookEvent",
                columns: new[] { "RepoOwner", "RepoName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Acceptance",
                schema: "leo_classroom");

            migrationBuilder.DropTable(
                name: "WebhookEvent",
                schema: "leo_classroom");

            migrationBuilder.DropTable(
                name: "Assignment",
                schema: "leo_classroom");

            migrationBuilder.DropColumn(
                name: "ForgejoOrg",
                schema: "leo_classroom",
                table: "Course");
        }
    }
}
