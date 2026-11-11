using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymHub.Modules.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.CreateTable(
                name: "catalog_prices",
                schema: "billing",
                columns: table => new
                {
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceVersion = table.Column<int>(type: "integer", nullable: false),
                    StripeProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StripePriceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_prices", x => new { x.ProgramId, x.PriceVersion });
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "billing",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StripeCustomerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payout_accounts",
                schema: "billing",
                columns: table => new
                {
                    TrainerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StripeAccountId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ChargesEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PayoutsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DetailsSubmitted = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_accounts", x => x.TrainerId);
                });

            migrationBuilder.CreateTable(
                name: "purchases",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PriceVersion = table.Column<int>(type: "integer", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Interval = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FeePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    TrialDays = table.Column<int>(type: "integer", nullable: false),
                    CheckoutSessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CheckoutExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaymentIntentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SubscriptionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StripeStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
                    TrialEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ScheduledPriceVersion = table.Column<int>(type: "integer", nullable: true),
                    ScheduledAmountMinor = table.Column<long>(type: "bigint", nullable: true),
                    ScheduledInterval = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ConvertedToFree = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastPaymentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stripe_events",
                schema: "billing",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stripe_events", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_prices_StripePriceId",
                schema: "billing",
                table: "catalog_prices",
                column: "StripePriceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_ProcessedAt_OccurredAt",
                schema: "billing",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_payout_accounts_StripeAccountId",
                schema: "billing",
                table: "payout_accounts",
                column: "StripeAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchases_CheckoutSessionId",
                schema: "billing",
                table: "purchases",
                column: "CheckoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_purchases_PaymentIntentId",
                schema: "billing",
                table: "purchases",
                column: "PaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_purchases_ProgramId",
                schema: "billing",
                table: "purchases",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_purchases_SubscriptionId",
                schema: "billing",
                table: "purchases",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_purchases_TrainerId_CreatedAt",
                schema: "billing",
                table: "purchases",
                columns: new[] { "TrainerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_purchases_UserId_ProgramId",
                schema: "billing",
                table: "purchases",
                columns: new[] { "UserId", "ProgramId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_prices",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "payout_accounts",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "purchases",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "stripe_events",
                schema: "billing");
        }
    }
}
