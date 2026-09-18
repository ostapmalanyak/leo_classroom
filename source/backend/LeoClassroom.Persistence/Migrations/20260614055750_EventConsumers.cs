using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace LeoClassroom.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventConsumers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Analytics_ActiveDayCount",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Analytics_CommitCount",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Instant>(
                name: "Analytics_FirstPushAt",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "Analytics_LastPushAt",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Analytics_LastPushHeadSha",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Analytics_PushCount",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "FeedbackPrNumber",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "FeedbackReadAt",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedbackState",
                schema: "leo_classroom",
                table: "Acceptance",
                type: "text",
                nullable: false,
                defaultValue: "None");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Acceptance_Late",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "Analytics_ActiveDayCount",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "Analytics_CommitCount",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "Analytics_FirstPushAt",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "Analytics_LastPushAt",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "Analytics_LastPushHeadSha",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "Analytics_PushCount",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "FeedbackPrNumber",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "FeedbackReadAt",
                schema: "leo_classroom",
                table: "Acceptance");

            migrationBuilder.DropColumn(
                name: "FeedbackState",
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
    }
}
