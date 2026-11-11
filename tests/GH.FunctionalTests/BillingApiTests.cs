using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Paging;
using GymHub.Modules.Billing.Contracts;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Domain;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Subscriptions;
using GymHub.Modules.Subscriptions.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stripe;

namespace GH.FunctionalTests;

public class BillingApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _trainerId = $"trainer-{Guid.NewGuid():N}";
    private readonly string _memberId = $"member-{Guid.NewGuid():N}";

    private HttpClient Trainer => factory.ClientFor(_trainerId, "trainer");
    private HttpClient Member => factory.ClientFor(_memberId);
    private HttpClient Admin => factory.ClientFor("admin-1", "admin");

    private sealed record IdResponse(Guid Id);
    private sealed record Redirect(Uri Url);
    private sealed record PayoutStatus(string Setup, bool ChargesEnabled);
    private sealed record PurchaseRow(
        Guid Id, Guid ProgramId, string Status, long AmountMinor, long FeeMinor, DateTime? CurrentPeriodEnd,
        bool CancelAtPeriodEnd, long? NextAmountMinor, bool CanRefund);

    private static readonly object Monthly = new { model = "Recurring", amountMinor = 1999, currency = "USD", interval = "Monthly" };
    private static readonly object OneTime = new { model = "OneTime", amountMinor = 4900, currency = "USD" };

    private async Task<Guid> ProgramAsync(object? pricing, bool publish = true)
    {
        var created = await Trainer.PostAsJsonAsync("/api/v1/programs/", new { title = $"Program {Guid.NewGuid():N}", kind = "Ongoing" }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;

        await Trainer.PostAsJsonAsync($"/api/v1/programs/{id}/weeks", new { title = "Week 1", releasesOn = Today.AddDays(-1) }, Json, Ct);

        if (pricing is not null)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PutAsJsonAsync($"/api/v1/programs/{id}/pricing", pricing, Json, Ct)).StatusCode);
        }

        if (publish)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PostAsync($"/api/v1/programs/{id}/publish", null, Ct)).StatusCode);
        }

        return id;
    }

    private async Task<Purchase> CheckoutAsync(Guid programId)
    {
        var started = await Member.PostAsync($"/api/v1/billing/programs/{programId}/checkout", null, Ct);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Assert.StartsWith("https://checkout.stripe.test/", (await started.Content.ReadFromJsonAsync<Redirect>(Json, Ct))!.Url.ToString());

        return await factory.WithDbAsync<BillingDbContext, Purchase>(db =>
            db.Purchases.AsNoTracking().OrderByDescending(p => p.CreatedAt).FirstAsync(p => p.UserId == _memberId && p.ProgramId == programId, Ct));
    }

    private async Task<HttpResponseMessage> SendEventAsync(string type, object data, string? eventId = null, string secret = ApiFactory.WebhookSecret)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = eventId ?? $"evt_{Guid.NewGuid():N}",
            @object = "event",
            api_version = StripeConfiguration.ApiVersion,
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            type,
            data = new { @object = data }
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/webhooks/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", EventUtility.GenerateSignatureHeader(payload, secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        return await factory.ClientFor(null).SendAsync(request, Ct);
    }

    private static object CompletedPayment(Purchase purchase, string paymentIntentId) => new
    {
        id = purchase.CheckoutSessionId,
        @object = "checkout.session",
        mode = "payment",
        payment_status = "paid",
        payment_intent = paymentIntentId,
        metadata = new { purchaseId = purchase.Id.ToString() }
    };

    private static object CompletedSubscription(Purchase purchase, string subscriptionId) => new
    {
        id = purchase.CheckoutSessionId,
        @object = "checkout.session",
        mode = "subscription",
        payment_status = "paid",
        subscription = subscriptionId,
        metadata = new { purchaseId = purchase.Id.ToString() }
    };

    private static object Subscription(Purchase purchase, string subscriptionId, string status, bool cancelAtPeriodEnd = false, string priceId = "price_x") => new
    {
        id = subscriptionId,
        @object = "subscription",
        status,
        cancel_at_period_end = cancelAtPeriodEnd,
        trial_end = status == "trialing" ? DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds() : (long?)null,
        metadata = new { purchaseId = purchase.Id.ToString() },
        items = new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    id = "si_1",
                    @object = "subscription_item",
                    current_period_end = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                    price = new { id = priceId, @object = "price" }
                }
            }
        }
    };

    private Task DispatchBillingAsync() => factory.DispatchOutboxAsync<BillingDbContext>();

    private async Task<HttpStatusCode> ContentStatusAsync(Guid programId) =>
        (await Member.GetAsync($"/api/v1/programs/{programId}/content", Ct)).StatusCode;

    private async Task<PurchaseRow> MyPurchaseAsync(Guid programId) =>
        (await Member.GetFromJsonAsync<List<PurchaseRow>>("/api/v1/billing/me/purchases", Json, Ct))!.Single(p => p.ProgramId == programId);

    [Fact]
    public async Task Trainers_set_up_payouts_before_publishing_paid_programs()
    {
        var draft = await ProgramAsync(OneTime, publish: false);

        var blocked = await Trainer.PostAsync($"/api/v1/programs/{draft}/publish", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var status = await Trainer.GetFromJsonAsync<PayoutStatus>("/api/v1/billing/payouts", Json, Ct);
        Assert.Equal("NotStarted", status!.Setup);

        var onboarding = await Trainer.PostAsync("/api/v1/billing/payouts/onboarding", null, Ct);
        Assert.Equal(HttpStatusCode.OK, onboarding.StatusCode);
        Assert.Contains("studio%2Fpayouts", (await onboarding.Content.ReadFromJsonAsync<Redirect>(Json, Ct))!.Url.ToString());
        Assert.Equal("InProgress", (await Trainer.GetFromJsonAsync<PayoutStatus>("/api/v1/billing/payouts", Json, Ct))!.Setup);

        // Stripe reports the finished onboarding.
        var accountId = await factory.WithDbAsync<BillingDbContext, string>(db =>
            db.PayoutAccounts.Where(a => a.TrainerId == _trainerId).Select(a => a.StripeAccountId).SingleAsync(Ct));
        var updated = await SendEventAsync("account.updated", new
        {
            id = accountId, @object = "account", charges_enabled = true, payouts_enabled = true, details_submitted = true
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        Assert.Equal("Ready", (await Trainer.GetFromJsonAsync<PayoutStatus>("/api/v1/billing/payouts", Json, Ct))!.Setup);
        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PostAsync($"/api/v1/programs/{draft}/publish", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Checkout_is_only_for_paid_programs_the_member_does_not_have()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var free = await ProgramAsync(pricing: null);
        var paid = await ProgramAsync(OneTime);

        Assert.Equal(HttpStatusCode.Conflict, (await Member.PostAsync($"/api/v1/billing/programs/{free}/checkout", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Trainer.PostAsync($"/api/v1/billing/programs/{paid}/checkout", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Member.PostAsync($"/api/v1/billing/programs/{Guid.NewGuid()}/checkout", null, Ct)).StatusCode);

        // A second click reuses the open checkout instead of starting another payable one.
        var first = await CheckoutAsync(paid);
        var second = await CheckoutAsync(paid);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task A_one_time_purchase_grants_access_and_a_refund_ends_it()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var programId = await ProgramAsync(OneTime);
        var purchase = await CheckoutAsync(programId);

        var request = factory.Stripe.Checkouts.Single(c => c.PurchaseId == purchase.Id);
        Assert.False(request.Recurring);
        Assert.Equal(490, request.ApplicationFeeMinor);

        Assert.Equal(HttpStatusCode.Forbidden, await ContentStatusAsync(programId));

        var eventId = $"evt_{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.OK, (await SendEventAsync("checkout.session.completed", CompletedPayment(purchase, "pi_one"), eventId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendEventAsync("checkout.session.completed", CompletedPayment(purchase, "pi_one"), eventId)).StatusCode);
        await DispatchBillingAsync();

        Assert.Equal(HttpStatusCode.OK, await ContentStatusAsync(programId));
        Assert.Equal(1, await factory.WithDbAsync<BillingDbContext, int>(db =>
            db.StripeEvents.CountAsync(e => e.EventId == eventId, Ct)));

        // Paid access is left from Manage billing, not with the free "Leave" button.
        Assert.Equal(HttpStatusCode.Conflict, (await Member.DeleteAsync($"/api/v1/subscriptions/programs/{programId}", Ct)).StatusCode);

        var row = await MyPurchaseAsync(programId);
        Assert.Equal("Active", row.Status);

        // Another trainer can't see or refund it; this trainer can.
        var otherTrainer = factory.ClientFor($"trainer-{Guid.NewGuid():N}", "trainer");
        Assert.Equal(HttpStatusCode.NotFound, (await otherTrainer.PostAsync($"/api/v1/billing/purchases/{purchase.Id}/refund", null, Ct)).StatusCode);

        var sales = await Trainer.GetFromJsonAsync<PagedResult<PurchaseRow>>($"/api/v1/billing/sales?programId={programId}", Json, Ct);
        Assert.True(Assert.Single(sales!.Data).CanRefund);

        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PostAsync($"/api/v1/billing/purchases/{purchase.Id}/refund", null, Ct)).StatusCode);
        Assert.Contains("pi_one", factory.Stripe.Refunds);
        await DispatchBillingAsync();

        Assert.Equal(HttpStatusCode.Forbidden, await ContentStatusAsync(programId));
        Assert.Equal(HttpStatusCode.Conflict, (await Trainer.PostAsync($"/api/v1/billing/purchases/{purchase.Id}/refund", null, Ct)).StatusCode);

        // Stripe's own charge.refunded for the same payment changes nothing more.
        await SendEventAsync("charge.refunded", new { id = "ch_1", @object = "charge", refunded = true, payment_intent = "pi_one" });
        Assert.Equal("Refunded", (await MyPurchaseAsync(programId)).Status);
    }

    [Fact]
    public async Task A_subscription_with_a_trial_grants_access_until_it_is_cancelled()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var programId = await ProgramAsync(new { model = "Recurring", amountMinor = 1999, currency = "USD", interval = "Monthly", trialDays = 7 });
        var purchase = await CheckoutAsync(programId);

        var request = factory.Stripe.Checkouts.Single(c => c.PurchaseId == purchase.Id);
        Assert.True(request.Recurring);
        Assert.Equal(7, request.TrialDays);
        Assert.Equal(10m, request.ApplicationFeePercent);

        // Stripe can send the subscription before the checkout completes.
        await SendEventAsync("customer.subscription.created", Subscription(purchase, "sub_trial", "trialing"));
        await SendEventAsync("checkout.session.completed", CompletedSubscription(purchase, "sub_trial"));
        await DispatchBillingAsync();

        Assert.Equal(HttpStatusCode.OK, await ContentStatusAsync(programId));
        var row = await MyPurchaseAsync(programId);
        Assert.Equal("Trialing", row.Status);
        Assert.NotNull(row.CurrentPeriodEnd);

        // Nothing paid yet, so nothing to refund.
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.PostAsync($"/api/v1/billing/purchases/{purchase.Id}/refund", null, Ct)).StatusCode);

        // The first invoice is paid when the trial ends.
        factory.Stripe.InvoicePayments["in_1"] = "pi_sub_1";
        await SendEventAsync("invoice.paid", new
        {
            id = "in_1", @object = "invoice", amount_paid = 1999,
            parent = new { type = "subscription_details", subscription_details = new { subscription = "sub_trial" } }
        });
        await SendEventAsync("customer.subscription.updated", Subscription(purchase, "sub_trial", "active", cancelAtPeriodEnd: true));
        Assert.Equal("Active", (await MyPurchaseAsync(programId)).Status);
        Assert.True((await MyPurchaseAsync(programId)).CancelAtPeriodEnd);

        await SendEventAsync("customer.subscription.deleted", Subscription(purchase, "sub_trial", "canceled"));
        await DispatchBillingAsync();

        Assert.Equal(HttpStatusCode.Forbidden, await ContentStatusAsync(programId));
        Assert.Equal("Ended", (await MyPurchaseAsync(programId)).Status);

        // One trial per program: buying again starts without one.
        var again = await CheckoutAsync(programId);
        Assert.Equal(0, factory.Stripe.Checkouts.Single(c => c.PurchaseId == again.Id).TrialDays);
    }

    [Fact]
    public async Task An_event_for_an_old_purchase_does_not_end_a_newer_one()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var programId = await ProgramAsync(OneTime);
        var purchase = await CheckoutAsync(programId);
        await SendEventAsync("checkout.session.completed", CompletedPayment(purchase, $"pi_{Guid.NewGuid():N}"));
        await DispatchBillingAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<PaidAccessEndedHandler>(scope.ServiceProvider);
            await handler.Handle(new PaidAccessEnded(_memberId, programId, Guid.NewGuid(), PaidAccessEndReason.Cancelled));
        }

        Assert.Equal(HttpStatusCode.OK, await ContentStatusAsync(programId));
    }

    [Fact]
    public async Task Subscribers_move_to_a_new_price_at_renewal_and_archiving_stops_renewals()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var programId = await ProgramAsync(Monthly);
        var purchase = await CheckoutAsync(programId);
        await SendEventAsync("checkout.session.completed", CompletedSubscription(purchase, $"sub_{purchase.Id:N}"));
        await DispatchBillingAsync();

        var raised = await Trainer.PutAsJsonAsync($"/api/v1/programs/{programId}/pricing",
            new { model = "Recurring", amountMinor = 2499, currency = "USD", interval = "Monthly" }, Json, Ct);
        Assert.Equal(HttpStatusCode.NoContent, raised.StatusCode);
        await factory.DispatchOutboxAsync<ProgramsDbContext>();

        var (subscriptionId, newPriceId) = Assert.Single(factory.Stripe.ScheduledChanges, c => c.SubscriptionId == $"sub_{purchase.Id:N}");
        Assert.Equal(2499, (await MyPurchaseAsync(programId)).NextAmountMinor);

        // The schedule switches the subscription at renewal.
        await SendEventAsync("customer.subscription.updated", Subscription(purchase, subscriptionId, "active", priceId: newPriceId));
        var renewed = await MyPurchaseAsync(programId);
        Assert.Equal(2499, renewed.AmountMinor);
        Assert.Null(renewed.NextAmountMinor);

        // The currency of a published subscription program is fixed.
        var euros = await Trainer.PutAsJsonAsync($"/api/v1/programs/{programId}/pricing",
            new { model = "Recurring", amountMinor = 2499, currency = "EUR", interval = "Monthly" }, Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, euros.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PostAsync($"/api/v1/programs/{programId}/archive", null, Ct)).StatusCode);
        await factory.DispatchOutboxAsync<ProgramsDbContext>();

        Assert.Contains(subscriptionId, factory.Stripe.CancelledAtPeriodEnd);
        Assert.True((await MyPurchaseAsync(programId)).CancelAtPeriodEnd);
        Assert.Equal(HttpStatusCode.OK, await ContentStatusAsync(programId));
    }

    [Fact]
    public async Task Subscribers_keep_access_for_free_when_the_program_becomes_free()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var programId = await ProgramAsync(Monthly);
        var purchase = await CheckoutAsync(programId);
        var subscriptionId = $"sub_{purchase.Id:N}";
        await SendEventAsync("checkout.session.completed", CompletedSubscription(purchase, subscriptionId));
        await DispatchBillingAsync();

        await Trainer.PutAsJsonAsync($"/api/v1/programs/{programId}/pricing", new { model = "Free" }, Json, Ct);
        await factory.DispatchOutboxAsync<ProgramsDbContext>();
        await DispatchBillingAsync();
        Assert.Contains(subscriptionId, factory.Stripe.CancelledAtPeriodEnd);

        // When the paid subscription runs out, the free enrollment remains.
        await SendEventAsync("customer.subscription.deleted", Subscription(purchase, subscriptionId, "canceled"));
        await DispatchBillingAsync();

        Assert.Equal(HttpStatusCode.OK, await ContentStatusAsync(programId));
        Assert.Equal(HttpStatusCode.NoContent, (await Member.DeleteAsync($"/api/v1/subscriptions/programs/{programId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_cancelled_subscriber_keeps_access_when_the_program_becomes_free()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var programId = await ProgramAsync(Monthly);
        var purchase = await CheckoutAsync(programId);
        var subscriptionId = $"sub_{purchase.Id:N}";
        await SendEventAsync("checkout.session.completed", CompletedSubscription(purchase, subscriptionId));
        await SendEventAsync("customer.subscription.updated", Subscription(purchase, subscriptionId, "active", cancelAtPeriodEnd: true));
        await DispatchBillingAsync();

        await Trainer.PutAsJsonAsync($"/api/v1/programs/{programId}/pricing", new { model = "Free" }, Json, Ct);
        await factory.DispatchOutboxAsync<ProgramsDbContext>();
        await DispatchBillingAsync();

        await SendEventAsync("customer.subscription.deleted", Subscription(purchase, subscriptionId, "canceled"));
        await DispatchBillingAsync();

        Assert.Equal(HttpStatusCode.OK, await ContentStatusAsync(programId));
    }

    [Fact]
    public async Task A_first_invoice_paid_before_checkout_completes_is_still_recorded()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var programId = await ProgramAsync(Monthly);
        var purchase = await CheckoutAsync(programId);
        var subscriptionId = $"sub_{purchase.Id:N}";
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        var invoiceId = $"in_{Guid.NewGuid():N}";

        factory.Stripe.InvoicePayments[invoiceId] = paymentIntentId;
        await SendEventAsync("invoice.paid", new
        {
            id = invoiceId, @object = "invoice", amount_paid = 1999,
            parent = new
            {
                type = "subscription_details",
                subscription_details = new { subscription = subscriptionId, metadata = new { purchaseId = purchase.Id.ToString() } }
            }
        });
        await SendEventAsync("checkout.session.completed", CompletedSubscription(purchase, subscriptionId));
        await DispatchBillingAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await Admin.PostAsync($"/api/v1/billing/purchases/{purchase.Id}/refund", null, Ct)).StatusCode);
        Assert.Contains(paymentIntentId, factory.Stripe.Refunds);
    }

    [Fact]
    public async Task Concurrent_checkouts_share_one_session()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var programId = await ProgramAsync(OneTime);

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Member.PostAsync($"/api/v1/billing/programs/{programId}/checkout", null, Ct)));
        var urls = new HashSet<Uri>();

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            urls.Add((await response.Content.ReadFromJsonAsync<Redirect>(Json, Ct))!.Url);
        }

        Assert.Single(urls);
        Assert.Equal(1, await factory.WithDbAsync<BillingDbContext, int>(db =>
            db.Purchases.CountAsync(p => p.UserId == _memberId && p.ProgramId == programId, Ct)));
    }

    [Fact]
    public async Task Checkouts_at_an_old_price_or_for_an_archived_program_are_closed()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var programId = await ProgramAsync(OneTime);
        var first = await CheckoutAsync(programId);

        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PutAsJsonAsync($"/api/v1/programs/{programId}/pricing",
            new { model = "OneTime", amountMinor = 5900, currency = "USD" }, Json, Ct)).StatusCode);
        await factory.DispatchOutboxAsync<ProgramsDbContext>();
        Assert.Contains(first.CheckoutSessionId, factory.Stripe.ExpiredCheckouts);

        var second = await CheckoutAsync(programId);
        Assert.NotEqual(first.Id, second.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PostAsync($"/api/v1/programs/{programId}/archive", null, Ct)).StatusCode);
        await factory.DispatchOutboxAsync<ProgramsDbContext>();
        Assert.Contains(second.CheckoutSessionId, factory.Stripe.ExpiredCheckouts);
    }

    [Fact]
    public async Task Refunds_of_older_or_partial_payments_are_logged_and_disputes_of_any_payment_end_access()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var programId = await ProgramAsync(Monthly);
        var purchase = await CheckoutAsync(programId);
        var subscriptionId = $"sub_{purchase.Id:N}";
        await SendEventAsync("checkout.session.completed", CompletedSubscription(purchase, subscriptionId));

        var (month1, month2) = ($"pi_{Guid.NewGuid():N}", $"pi_{Guid.NewGuid():N}");

        foreach (var paymentIntentId in new[] { month1, month2 })
        {
            var invoiceId = $"in_{Guid.NewGuid():N}";
            factory.Stripe.InvoicePayments[invoiceId] = paymentIntentId;
            await SendEventAsync("invoice.paid", new
            {
                id = invoiceId, @object = "invoice", amount_paid = 1999,
                parent = new { type = "subscription_details", subscription_details = new { subscription = subscriptionId } }
            });
        }

        await DispatchBillingAsync();

        // Month 1 refunded in full from the dashboard, month 2 in part: both recorded, access unchanged.
        await SendEventAsync("charge.refunded", new { id = "ch_m1", @object = "charge", refunded = true, amount_refunded = 1999, payment_intent = month1 });
        await SendEventAsync("charge.refunded", new { id = "ch_m2", @object = "charge", refunded = false, amount_refunded = 500, payment_intent = month2 });
        await DispatchBillingAsync();

        Assert.Equal(HttpStatusCode.OK, await ContentStatusAsync(programId));
        Assert.Equal("Active", (await MyPurchaseAsync(programId)).Status);

        var refunded = await factory.WithDbAsync<BillingDbContext, List<PurchasePayment>>(db =>
            db.Payments.AsNoTracking().Where(p => p.PurchaseId == purchase.Id).ToListAsync(Ct));
        Assert.Equal(1999, refunded.Single(p => p.PaymentIntentId == month1).RefundedAmountMinor);
        Assert.Equal(500, refunded.Single(p => p.PaymentIntentId == month2).RefundedAmountMinor);

        // A dispute of the older payment still ends access and stops the subscription.
        await SendEventAsync("charge.dispute.created", new { id = "dp_m1", @object = "dispute", charge = "ch_m1", payment_intent = month1 });
        await DispatchBillingAsync();

        Assert.Equal(HttpStatusCode.Forbidden, await ContentStatusAsync(programId));
        Assert.Contains(subscriptionId, factory.Stripe.CancelledNow);
    }

    [Fact]
    public async Task A_stale_purchase_write_fails_instead_of_overwriting()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var programId = await ProgramAsync(OneTime);
        var purchase = await CheckoutAsync(programId);

        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var db1 = first.ServiceProvider.GetRequiredService<BillingDbContext>();
        var db2 = second.ServiceProvider.GetRequiredService<BillingDbContext>();
        var copy1 = await db1.Purchases.SingleAsync(p => p.Id == purchase.Id, Ct);
        var copy2 = await db2.Purchases.SingleAsync(p => p.Id == purchase.Id, Ct);

        copy1.CompletePayment("pi_a", DateTime.UtcNow);
        await db1.SaveChangesAsync(Ct);

        copy2.CompletePayment("pi_b", DateTime.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Disputes_end_access()
    {
        await factory.MarkPayoutsReadyAsync(_trainerId);
        var programId = await ProgramAsync(OneTime);
        var purchase = await CheckoutAsync(programId);
        var paymentIntent = $"pi_{Guid.NewGuid():N}";
        await SendEventAsync("checkout.session.completed", CompletedPayment(purchase, paymentIntent));
        await DispatchBillingAsync();

        await SendEventAsync("charge.dispute.created", new { id = "dp_1", @object = "dispute", charge = "ch_1", payment_intent = paymentIntent });
        await DispatchBillingAsync();

        Assert.Equal(HttpStatusCode.Forbidden, await ContentStatusAsync(programId));
        var all = await Admin.GetFromJsonAsync<PagedResult<PurchaseRow>>($"/api/v1/billing/purchases?status=Disputed&programId={programId}", Json, Ct);
        Assert.Single(all!.Data);
    }

    [Fact]
    public async Task Webhooks_with_a_bad_signature_are_rejected()
    {
        var response = await SendEventAsync("account.updated", new { id = "acct_x", @object = "account" }, secret: "whsec_wrong");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_admins_list_every_purchase()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Trainer.GetAsync("/api/v1/billing/purchases", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Admin.GetAsync("/api/v1/billing/purchases", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_portal_needs_a_previous_purchase()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Member.PostAsync("/api/v1/billing/portal", null, Ct)).StatusCode);
    }
}
