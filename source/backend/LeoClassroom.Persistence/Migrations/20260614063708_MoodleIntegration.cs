using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoodleIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MoodleItemMapping",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssignmentId = table.Column<long>(type: "bigint", nullable: false),
                    CourseId = table.Column<long>(type: "bigint", nullable: false),
                    MoodleItemId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoodleItemMapping", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MoodleLink",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseId = table.Column<long>(type: "bigint", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    MoodleBaseUrl = table.Column<string>(type: "text", nullable: false),
                    MoodleCourseId = table.Column<long>(type: "bigint", nullable: false),
                    TokenCipher = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoodleLink", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MoodleLink_Course_CourseId",
                        column: x => x.CourseId,
                        principalSchema: "leo_classroom",
                        principalTable: "Course",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MoodleSyncOp",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseId = table.Column<long>(type: "bigint", nullable: false),
                    AssignmentId = table.Column<long>(type: "bigint", nullable: false),
                    OpType = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Deadline = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoodleSyncOp", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MoodleItemMapping_AssignmentId",
                schema: "leo_classroom",
                table: "MoodleItemMapping",
                column: "AssignmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MoodleItemMapping_CourseId",
                schema: "leo_classroom",
                table: "MoodleItemMapping",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_MoodleLink_CourseId",
                schema: "leo_classroom",
                table: "MoodleLink",
                column: "CourseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MoodleSyncOp_AssignmentId",
                schema: "leo_classroom",
                table: "MoodleSyncOp",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MoodleSyncOp_Status_NextAttemptAt",
                schema: "leo_classroom",
                table: "MoodleSyncOp",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MoodleItemMapping",
                schema: "leo_classroom");

            migrationBuilder.DropTable(
                name: "MoodleLink",
                schema: "leo_classroom");

            migrationBuilder.DropTable(
                name: "MoodleSyncOp",
                schema: "leo_classroom");
        }
    }
}
