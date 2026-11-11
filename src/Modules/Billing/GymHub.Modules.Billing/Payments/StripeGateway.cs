using GymHub.Modules.Programs.Contracts;
using Microsoft.Extensions.Options;
using Stripe;

namespace GymHub.Modules.Billing.Payments;

/// <summary>Stripe.net implementation of <see cref="IStripeGateway"/>, using destination charges to Express accounts.</summary>
internal sealed class StripeGateway(IOptions<BillingOptions> options) : IStripeGateway
{
    private readonly Lazy<StripeClient> _lazyClient = new(() => new StripeClient(
        options.Value.StripeSecretKey is { Length: > 0 } key
            ? key
            // A StripeException, so handlers report it like any other payment provider failure.
            : throw new StripeException("Payments aren't configured. Set the stripe-secret-key AppHost parameter.")));

    private StripeClient _client => _lazyClient.Value;

    private static RequestOptions Idempotent(string key) => new() { IdempotencyKey = key };

    public async Task<string> CreateExpressAccountAsync(string trainerId, string? email, CancellationToken cancellationToken)
    {
        var account = await new AccountService(_client).CreateAsync(new AccountCreateOptions
        {
            Type = "express",
            Email = email,
            Capabilities = new AccountCapabilitiesOptions
            {
                CardPayments = new AccountCapabilitiesCardPaymentsOptions { Requested = true },
                Transfers = new AccountCapabilitiesTransfersOptions { Requested = true }
            },
            Metadata = new Dictionary<string, string> { ["trainerId"] = trainerId }
        }, Idempotent($"account:{trainerId}"), cancellationToken);

        return account.Id;
    }

    public async Task<StripeAccountStatus> GetAccountStatusAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await new AccountService(_client).GetAsync(accountId, cancellationToken: cancellationToken);
        return new StripeAccountStatus(account.ChargesEnabled, account.PayoutsEnabled, account.DetailsSubmitted);
    }

    public async Task<Uri> CreateOnboardingLinkAsync(string accountId, Uri refreshUrl, Uri returnUrl, CancellationToken cancellationToken)
    {
        var link = await new AccountLinkService(_client).CreateAsync(new AccountLinkCreateOptions
        {
            Account = accountId,
            RefreshUrl = refreshUrl.ToString(),
            ReturnUrl = returnUrl.ToString(),
            Type = "account_onboarding"
        }, cancellationToken: cancellationToken);

        return new Uri(link.Url);
    }

    public async Task<Uri> CreateDashboardLinkAsync(string accountId, CancellationToken cancellationToken)
    {
        var link = await new AccountLoginLinkService(_client).CreateAsync(accountId, cancellationToken: cancellationToken);
        return new Uri(link.Url);
    }

    public async Task<string> CreateCustomerAsync(string userId, string? email, CancellationToken cancellationToken)
    {
        var customer = await new CustomerService(_client).CreateAsync(new CustomerCreateOptions
        {
            Email = email,
            Metadata = new Dictionary<string, string> { ["userId"] = userId }
        }, Idempotent($"customer:{userId}"), cancellationToken);

        return customer.Id;
    }

    public async Task<string> CreateProductAsync(Guid programId, string title, CancellationToken cancellationToken)
    {
        var product = await new ProductService(_client).CreateAsync(new ProductCreateOptions
        {
            Name = title,
            Metadata = new Dictionary<string, string> { ["programId"] = programId.ToString() }
        }, Idempotent($"product:{programId}"), cancellationToken);

        return product.Id;
    }

    public async Task<string> CreatePriceAsync(string productId, Guid programId, int priceVersion, ProgramPrice price, CancellationToken cancellationToken)
    {
        var created = await new PriceService(_client).CreateAsync(new PriceCreateOptions
        {
            Product = productId,
            Currency = price.Currency!.ToLowerInvariant(),
            UnitAmount = price.AmountMinor,
            Recurring = price.Model == PricingModel.Recurring
                ? new PriceRecurringOptions { Interval = price.Interval == BillingInterval.Yearly ? "year" : "month" }
                : null,
            Metadata = new Dictionary<string, string>
            {
                ["programId"] = programId.ToString(),
                ["priceVersion"] = priceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        }, Idempotent($"price:{programId}:{priceVersion}"), cancellationToken);

        return created.Id;
    }

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string> { ["purchaseId"] = request.PurchaseId.ToString() };

        var sessionOptions = new Stripe.Checkout.SessionCreateOptions
        {
            Mode = request.Recurring ? "subscription" : "payment",
            Customer = request.CustomerId,
            ClientReferenceId = request.PurchaseId.ToString(),
            LineItems = [new Stripe.Checkout.SessionLineItemOptions { Price = request.PriceId, Quantity = 1 }],
            SuccessUrl = request.SuccessUrl.ToString(),
            CancelUrl = request.CancelUrl.ToString(),
            ExpiresAt = request.ExpiresAt,
            Metadata = metadata
        };

        if (request.Recurring)
        {
            sessionOptions.SubscriptionData = new Stripe.Checkout.SessionSubscriptionDataOptions
            {
                ApplicationFeePercent = request.ApplicationFeePercent,
                TransferData = new Stripe.Checkout.SessionSubscriptionDataTransferDataOptions { Destination = request.DestinationAccountId },
                TrialPeriodDays = request.TrialDays > 0 ? request.TrialDays : null,
                Metadata = metadata
            };
        }
        else
        {
            sessionOptions.PaymentIntentData = new Stripe.Checkout.SessionPaymentIntentDataOptions
            {
                ApplicationFeeAmount = request.ApplicationFeeMinor,
                TransferData = new Stripe.Checkout.SessionPaymentIntentDataTransferDataOptions { Destination = request.DestinationAccountId },
                Metadata = metadata
            };
        }

        var session = await new Stripe.Checkout.SessionService(_client)
            .CreateAsync(sessionOptions, Idempotent($"checkout:{request.PurchaseId}"), cancellationToken);

        return new CheckoutSession(session.Id, session.Url, session.ExpiresAt);
    }

    public async Task<bool> ExpireCheckoutAsync(string sessionId, CancellationToken cancellationToken)
    {
        var sessions = new Stripe.Checkout.SessionService(_client);
        var session = await sessions.GetAsync(sessionId, cancellationToken: cancellationToken);

        if (session.Status == "open")
        {
            session = await sessions.ExpireAsync(sessionId, cancellationToken: cancellationToken);
        }

        return session.Status != "complete";
    }

    public async Task<Uri> CreatePortalSessionAsync(string customerId, Uri returnUrl, CancellationToken cancellationToken)
    {
        var session = await new Stripe.BillingPortal.SessionService(_client).CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = customerId,
            ReturnUrl = returnUrl.ToString()
        }, cancellationToken: cancellationToken);

        return new Uri(session.Url);
    }

    public async Task SchedulePriceChangeAsync(string subscriptionId, string priceId, BillingInterval interval, CancellationToken cancellationToken)
    {
        var subscriptions = new SubscriptionService(_client);
        var schedules = new SubscriptionScheduleService(_client);

        var subscription = await subscriptions.GetAsync(subscriptionId, cancellationToken: cancellationToken);
        var scheduleId = subscription.ScheduleId
            ?? (await schedules.CreateAsync(new SubscriptionScheduleCreateOptions { FromSubscription = subscriptionId },
                Idempotent($"schedule:{subscriptionId}"), cancellationToken)).Id;

        var schedule = await schedules.GetAsync(scheduleId, cancellationToken: cancellationToken);
        var current = schedule.Phases.Single(p => p.StartDate == schedule.CurrentPhase.StartDate);
        var destination = current.TransferData?.DestinationId;

        // Keep the current phase as it is, then switch price at its end and hand the subscription back.
        await schedules.UpdateAsync(scheduleId, new SubscriptionScheduleUpdateOptions
        {
            EndBehavior = "release",
            Phases =
            [
                new SubscriptionSchedulePhaseOptions
                {
                    StartDate = current.StartDate,
                    EndDate = current.EndDate,
                    TrialEnd = current.TrialEnd,
                    Items = [.. current.Items.Select(i => new SubscriptionSchedulePhaseItemOptions { Price = i.PriceId, Quantity = i.Quantity })],
                    ApplicationFeePercent = current.ApplicationFeePercent,
                    TransferData = destination is null ? null : new SubscriptionSchedulePhaseTransferDataOptions { Destination = destination },
                    ProrationBehavior = "none"
                },
                new SubscriptionSchedulePhaseOptions
                {
                    Items = [new SubscriptionSchedulePhaseItemOptions { Price = priceId, Quantity = 1 }],
                    Duration = new SubscriptionSchedulePhaseDurationOptions { Interval = interval == BillingInterval.Yearly ? "year" : "month", IntervalCount = 1 },
                    ApplicationFeePercent = current.ApplicationFeePercent,
                    TransferData = destination is null ? null : new SubscriptionSchedulePhaseTransferDataOptions { Destination = destination },
                    ProrationBehavior = "none"
                }
            ]
        }, cancellationToken: cancellationToken);
    }

    public Task CancelAtPeriodEndAsync(string subscriptionId, CancellationToken cancellationToken) =>
        ReleaseScheduleThen(subscriptionId, cancellationToken, subscriptions =>
            subscriptions.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions { CancelAtPeriodEnd = true }, cancellationToken: cancellationToken));

    public Task CancelNowAsync(string subscriptionId, CancellationToken cancellationToken) =>
        ReleaseScheduleThen(subscriptionId, cancellationToken, subscriptions =>
            subscriptions.CancelAsync(subscriptionId, cancellationToken: cancellationToken));

    public async Task RefundAsync(string paymentIntentId, Guid purchaseId, CancellationToken cancellationToken) =>
        await new RefundService(_client).CreateAsync(new RefundCreateOptions
        {
            PaymentIntent = paymentIntentId,
            ReverseTransfer = true,
            RefundApplicationFee = true,
            Metadata = new Dictionary<string, string> { ["purchaseId"] = purchaseId.ToString() }
        }, Idempotent($"refund:{purchaseId}:{paymentIntentId}"), cancellationToken);

    public async Task<string?> GetInvoicePaymentIntentAsync(string invoiceId, CancellationToken cancellationToken)
    {
        var payments = await new InvoicePaymentService(_client).ListAsync(
            new InvoicePaymentListOptions { Invoice = invoiceId, Status = "paid" }, cancellationToken: cancellationToken);

        return payments.Data.Select(p => p.Payment?.PaymentIntentId).FirstOrDefault(id => id is not null);
    }

    /// <summary>A subscription managed by a schedule must be released from it before it is cancelled.</summary>
    private async Task ReleaseScheduleThen(string subscriptionId, CancellationToken cancellationToken, Func<SubscriptionService, Task> action)
    {
        var subscriptions = new SubscriptionService(_client);
        var subscription = await subscriptions.GetAsync(subscriptionId, cancellationToken: cancellationToken);

        if (subscription.Status == "canceled")
        {
            return;
        }

        if (subscription.ScheduleId is { } scheduleId)
        {
            await new SubscriptionScheduleService(_client).ReleaseAsync(scheduleId, cancellationToken: cancellationToken);
        }

        await action(subscriptions);
    }
}
