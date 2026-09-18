using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssignmentFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoDeleteEnabled",
                schema: "leo_classroom",
                table: "Assignment",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Instant>(
                name: "AutoDeleteOn",
                schema: "leo_classroom",
                table: "Assignment",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "leo_classroom",
                table: "Assignment",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DownloadSnapshotMode",
                schema: "leo_classroom",
                table: "Assignment",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HintsInstructions",
                schema: "leo_classroom",
                table: "Assignment",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OwnerId",
                schema: "leo_classroom",
                table: "Assignment",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Instant>(
                name: "LastCommitAt",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "LastPushAt",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepoUrl",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AssignmentTeacher",
                schema: "leo_classroom",
                columns: table => new
                {
                    AssignmentId = table.Column<long>(type: "bigint", nullable: false),
                    CoTeachersId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentTeacher", x => new { x.AssignmentId, x.CoTeachersId });
                    table.ForeignKey(
                        name: "FK_AssignmentTeacher_Assignment_AssignmentId",
                        column: x => x.AssignmentId,
                        principalSchema: "leo_classroom",
                        principalTable: "Assignment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssignmentTeacher_User_CoTeachersId",
                        column: x => x.CoTeachersId,
                        principalSchema: "leo_classroom",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assignment_OwnerId",
                schema: "leo_classroom",
                table: "Assignment",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTeacher_CoTeachersId",
                schema: "leo_classroom",
                table: "AssignmentTeacher",
                column: "CoTeachersId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assignment_User_OwnerId",
                schema: "leo_classroom",
                table: "Assignment",
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
                name: "FK_Assignment_User_OwnerId",
                schema: "leo_classroom",
                table: "Assignment");

            migrationBuilder.DropTable(
                name: "AssignmentTeacher",
                schema: "leo_classroom");

            migrationBuilder.DropIndex(
                name: "IX_Assignment_OwnerId",
                schema: "leo_classroom",
                table: "Assignment");

            migrationBuilder.DropColumn(
                name: "AutoDeleteEnabled",
                schema: "leo_classroom",
                table: "Assignment");

            migrationBuilder.DropColumn(
                name: "AutoDeleteOn",
                schema: "leo_classroom",
                table: "Assignment");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "leo_classroom",
                table: "Assignment");

            migrationBuilder.DropColumn(
                name: "DownloadSnapshotMode",
                schema: "leo_classroom",
                table: "Assignment");

            migrationBuilder.DropColumn(
                name: "HintsInstructions",
                schema: "leo_classroom",
                table: "Assignment");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "leo_classroom",
                table: "Assignment");

            migrationBuilder.DropColumn(
                name: "LastCommitAt",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "LastPushAt",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "RepoUrl",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "leo_classroom",
                table: "Acceptance");
        }
    }
}
