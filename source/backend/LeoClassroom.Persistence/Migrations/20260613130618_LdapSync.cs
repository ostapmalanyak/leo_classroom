using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LdapSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Class",
                schema: "leo_classroom",
                table: "User",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClassKey",
                schema: "leo_classroom",
                table: "Roster",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "leo_classroom",
                table: "Roster",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Roster_ClassKey",
                schema: "leo_classroom",
                table: "Roster",
                column: "ClassKey",
                unique: true,
                filter: "\"ClassKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roster_ClassKey",
                schema: "leo_classroom",
                table: "Roster");

            migrationBuilder.DropColumn(
                name: "Class",
                schema: "leo_classroom",
                table: "User");

            migrationBuilder.DropColumn(
                name: "ClassKey",
                schema: "leo_classroom",
                table: "Roster");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "leo_classroom",
                table: "Roster");
        }
    }
}
