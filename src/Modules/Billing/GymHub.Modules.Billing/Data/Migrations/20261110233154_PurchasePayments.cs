using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymHub.Modules.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class PurchasePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "billing",
                table: "purchases",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "purchase_payments",
                schema: "billing",
                columns: table => new
                {
                    PaymentIntentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RefundedAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    RefundedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_payments", x => x.PaymentIntentId);
                    table.ForeignKey(
                        name: "FK_purchase_payments_purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalSchema: "billing",
                        principalTable: "purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_payments_PurchaseId",
                schema: "billing",
                table: "purchase_payments",
                column: "PurchaseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_payments",
                schema: "billing");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "billing",
                table: "purchases");
        }
    }
}
