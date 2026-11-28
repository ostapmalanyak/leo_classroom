using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DerivedLateness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Acceptance_Late",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "Late",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "LateSince",
                schema: "leo_classroom",
                table: "Acceptance");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Late",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Instant>(
                name: "LateSince",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Acceptance_Late",
                schema: "leo_classroom",
                table: "Acceptance",
                column: "Late");
        }
    }
}
