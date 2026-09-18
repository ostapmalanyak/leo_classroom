using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvent",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    ActorStudentId = table.Column<string>(type: "text", nullable: false),
                    ActorRoles = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    TargetType = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<string>(type: "text", nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvent", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvent_Action",
                schema: "leo_classroom",
                table: "AuditEvent",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvent_ActorStudentId",
                schema: "leo_classroom",
                table: "AuditEvent",
                column: "ActorStudentId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvent_At",
                schema: "leo_classroom",
                table: "AuditEvent",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvent_TargetType_TargetId",
                schema: "leo_classroom",
                table: "AuditEvent",
                columns: new[] { "TargetType", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvent",
                schema: "leo_classroom");
        }
    }
}
