using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CourseSpecificMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourseMember",
                schema: "leo_classroom",
                columns: table => new
                {
                    CourseId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseMember", x => new { x.CourseId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CourseMember_Course_CourseId",
                        column: x => x.CourseId,
                        principalSchema: "leo_classroom",
                        principalTable: "Course",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseMember_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "leo_classroom",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseMember_UserId",
                schema: "leo_classroom",
                table: "CourseMember",
                column: "UserId");

            migrationBuilder.Sql("""
                INSERT INTO leo_classroom."CourseMember" ("CourseId", "UserId")
                SELECT c."Id", ru."MembersId"
                FROM leo_classroom."Course" AS c
                INNER JOIN leo_classroom."RosterUser" AS ru ON ru."RostersId" = c."RosterId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseMember",
                schema: "leo_classroom");
        }
    }
}
