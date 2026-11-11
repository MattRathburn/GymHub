using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymHub.Modules.Subscriptions.Data.Migrations
{
    /// <inheritdoc />
    public partial class PaidEnrollments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseId",
                schema: "subscriptions",
                table: "enrollments",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PurchaseId",
                schema: "subscriptions",
                table: "enrollments");
        }
    }
}
