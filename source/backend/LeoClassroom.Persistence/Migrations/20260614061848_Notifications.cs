using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notification",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventKey = table.Column<string>(type: "text", nullable: false),
                    RecipientUserId = table.Column<long>(type: "bigint", nullable: false),
                    RecipientEmail = table.Column<string>(type: "text", nullable: false),
                    Trigger = table.Column<string>(type: "text", nullable: false),
                    AssignmentId = table.Column<long>(type: "bigint", nullable: false),
                    AssignmentTitle = table.Column<string>(type: "text", nullable: false),
                    Deadline = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notification_User_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalSchema: "leo_classroom",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreference",
                schema: "leo_classroom",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    NewAssignment = table.Column<bool>(type: "boolean", nullable: false),
                    DeadlineChanged = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreference", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationPreference_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "leo_classroom",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notification_EventKey_RecipientUserId",
                schema: "leo_classroom",
                table: "Notification",
                columns: new[] { "EventKey", "RecipientUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notification_RecipientUserId",
                schema: "leo_classroom",
                table: "Notification",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notification_Status_NextAttemptAt",
                schema: "leo_classroom",
                table: "Notification",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreference_UserId",
                schema: "leo_classroom",
                table: "NotificationPreference",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notification",
                schema: "leo_classroom");

            migrationBuilder.DropTable(
                name: "NotificationPreference",
                schema: "leo_classroom");
        }
    }
}
