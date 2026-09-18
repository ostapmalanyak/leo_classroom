using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RosterManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "OwnerId",
                schema: "leo_classroom",
                table: "Roster",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CourseTeacher",
                schema: "leo_classroom",
                columns: table => new
                {
                    CoTeachersId = table.Column<long>(type: "bigint", nullable: false),
                    CourseId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseTeacher", x => new { x.CoTeachersId, x.CourseId });
                    table.ForeignKey(
                        name: "FK_CourseTeacher_Course_CourseId",
                        column: x => x.CourseId,
                        principalSchema: "leo_classroom",
                        principalTable: "Course",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseTeacher_User_CoTeachersId",
                        column: x => x.CoTeachersId,
                        principalSchema: "leo_classroom",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Roster_OwnerId",
                schema: "leo_classroom",
                table: "Roster",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseTeacher_CourseId",
                schema: "leo_classroom",
                table: "CourseTeacher",
                column: "CourseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Roster_User_OwnerId",
                schema: "leo_classroom",
                table: "Roster",
                column: "OwnerId",
                principalSchema: "leo_classroom",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Roster_User_OwnerId",
                schema: "leo_classroom",
                table: "Roster");

            migrationBuilder.DropTable(
                name: "CourseTeacher",
                schema: "leo_classroom");

            migrationBuilder.DropIndex(
                name: "IX_Roster_OwnerId",
                schema: "leo_classroom",
                table: "Roster");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "leo_classroom",
                table: "Roster");
        }
    }
}
