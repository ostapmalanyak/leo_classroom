using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "leo_classroom");

            migrationBuilder.CreateTable(
                name: "Roster",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roster", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    LdapLastSeen = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Course",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    RosterId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    IsReadOnly = table.Column<bool>(type: "boolean", nullable: false),
                    StudentsRetainAccess = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Course", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Course_Roster_RosterId",
                        column: x => x.RosterId,
                        principalSchema: "leo_classroom",
                        principalTable: "Roster",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Course_User_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "leo_classroom",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RosterUser",
                schema: "leo_classroom",
                columns: table => new
                {
                    MembersId = table.Column<long>(type: "bigint", nullable: false),
                    RostersId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterUser", x => new { x.MembersId, x.RostersId });
                    table.ForeignKey(
                        name: "FK_RosterUser_Roster_RostersId",
                        column: x => x.RostersId,
                        principalSchema: "leo_classroom",
                        principalTable: "Roster",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RosterUser_User_MembersId",
                        column: x => x.MembersId,
                        principalSchema: "leo_classroom",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Course_OwnerId",
                schema: "leo_classroom",
                table: "Course",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Course_RosterId_Title",
                schema: "leo_classroom",
                table: "Course",
                columns: new[] { "RosterId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_RosterUser_RostersId",
                schema: "leo_classroom",
                table: "RosterUser",
                column: "RostersId");

            migrationBuilder.CreateIndex(
                name: "IX_User_StudentId",
                schema: "leo_classroom",
                table: "User",
                column: "StudentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Course",
                schema: "leo_classroom");

            migrationBuilder.DropTable(
                name: "RosterUser",
                schema: "leo_classroom");

            migrationBuilder.DropTable(
                name: "Roster",
                schema: "leo_classroom");

            migrationBuilder.DropTable(
                name: "User",
                schema: "leo_classroom");
        }
    }
}
