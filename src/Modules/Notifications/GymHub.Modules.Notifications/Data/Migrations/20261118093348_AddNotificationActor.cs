using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymHub.Modules.Notifications.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationActor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorId",
                schema: "notifications",
                table: "notifications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorName",
                schema: "notifications",
                table: "notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_ActorId",
                schema: "notifications",
                table: "notifications",
                column: "ActorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_ActorId",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "ActorId",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "ActorName",
                schema: "notifications",
                table: "notifications");
        }
    }
}
