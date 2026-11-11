using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CourseIconSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IconSeed",
                schema: "leo_classroom",
                table: "Course",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE leo_classroom."Course"
                SET "IconSeed" = 'legacy-' || "Id"::text
                WHERE "IconSeed" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IconSeed",
                schema: "leo_classroom",
                table: "Course");
        }
    }
}
