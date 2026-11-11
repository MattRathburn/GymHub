# Plan: Billing module (Stripe Connect)

## Context

Only free programs can be joined today. Enrolling in a paid program returns 402 "checkout required". Billing lets members pay for programs, either once or by subscription, and pays trainers through Stripe Connect, with GymHub keeping a platform fee.

Parts of this already exist:

- **Programs** keeps `PriceVersion`s and raises `ProgramPublished`, `ProgramPricingChanged` and `ProgramArchived`. `IProgramCatalog` returns the current price and its version.
- **Subscriptions** has `Enrollment.Source = Free | Stripe` and `IEntitlements`. Program content and Discussions already check access through `IEntitlements`, so paid members need no changes there.

### Decisions (from Q&A)

| Topic | Decision |
|---|---|
| Connect type | **Express** accounts with **destination charges**. GymHub is the merchant of record, Stripe hosts onboarding and the trainer dashboard. |
| Platform fee | **One global percent**, set in config as `Billing:PlatformFeePercent` (default 10). |
| Price changes | Existing subscribers **move to the new price at their next renewal**. |
| Refunds and trials | **Trainers can refund their own sales**, and admins can refund any sale. Trainers can give recurring programs a **free trial**. |

### Assumptions (tell me if any are wrong)

- **Hosted pages only.** Stripe Checkout handles buying and the Stripe Customer Portal handles cancelling and updating the card, so no card fields live in our app and no publishable key is needed. Card payments only, with no async methods such as bank debits.
- **A paid program needs a trainer who is ready for payouts.** Publishing a paid program, or switching a published program to paid, fails until the trainer's Express account has `charges_enabled`.
- **Who has access:**
  - One-time purchase: access for as long as the program exists, ending only on refund or dispute.
  - Subscription: access while it is `trialing`, `active` or `past_due` (Stripe's retries run during `past_due`). Access ends when the subscription is deleted or `unpaid`.
- **Cancelling** goes through the portal and takes effect at the end of the period. The existing "Leave" button returns 409 for a paid enrollment and points to "Manage billing".
- **Trials:** a recurring price gets `TrialDays` (0–30). A user gets one trial per program, and Checkout still collects a card.
- **Refunds:**
  - Always full refunds in v1.
  - Trainers can refund their own sales up to 30 days after the charge. Admins can refund at any time.
  - A refund reverses the transfer and the application fee (`reverse_transfer`, `refund_application_fee`).
  - Refunding a one-time purchase ends access. Refunding a subscription refunds its latest paid invoice and cancels it immediately.
- **Price changes when the model changes.** "Move to the new price" only works for recurring → recurring, so the other changes need rules:
  - **Recurring → recurring** (amount or interval): each subscription moves at renewal through a Stripe Subscription Schedule, with no proration.
  - **Recurring → free:** subscriptions are cancelled at period end, and those members keep access as free enrollments.
  - **Recurring → one-time:** subscriptions are cancelled at period end, then access ends unless the member buys. *This one is a judgment call.*
  - **One-time → anything:** past buyers keep access, since they paid for it.
  - **Free → paid:** existing free members keep access, since there's no card on file to charge. *This is also a judgment call.*
  - **The currency of a published recurring program can't change**, because Stripe subscriptions can't switch currency. This needs a small domain rule in Programs.
- **Archiving** a program cancels its subscriptions at period end, with no automatic refunds. Members keep access until then.
- **Disputes** end access and mark the purchase `Disputed`. Admins see it in the purchases list and handle the dispute in the Stripe dashboard.
- **Notice before a price change:** there's no Notifications module yet. "My programs" shows "Renews at $X from {date}", and Stripe's own upcoming-renewal emails are turned on in the Stripe dashboard.
- **Single API instance and moderate volume.** The price-change handler updates subscriptions in a loop, which is fine at this scale but would become a background job if it grew.

## Part 1: Programs changes (small)

- `ProgramPrice` gains `int? TrialDays`. It is valid only for `Recurring`, from 0 to 30, and is shown on the overview.
- `TrainingProgram.SetPricing`:
  - rejects a currency change on a published recurring program
  - a trial-only change still adds a price version, so new buyers get the new trial
- `PublishProgram` and `SetProgramPricing` call `IPayoutReadiness.IsReadyAsync(trainerId)` from Billing.Contracts when the price isn't free. If the trainer isn't ready, they return 409 "Set up payouts before selling programs".
- Migration: `TrialDays` lives in the price version's JSON or column, whichever `PriceVersion` already uses.

## Part 2: Billing module (`GymHub.Modules.Billing` + `.Contracts`, schema `billing`)

### Stripe access

- Add the `Stripe.net` package. All calls go through an internal `IStripeGateway`: create Express accounts, account and login links, customers, products and prices, Checkout and portal sessions, subscriptions and schedules, and refunds. Functional tests swap in a fake. Webhook parsing uses `EventUtility.ConstructEvent` directly.
- Stripe's *idempotency keys* go on every call that creates something, built from our own ids, e.g. `checkout:{purchaseId}`.
- Config:
  - `Billing:PlatformFeePercent`
  - `Billing:TrainerRefundWindowDays` (30)
  - the app's public base URL for return links
- Secrets come from AppHost parameters `stripe-secret-key` (an `sk_test_…` key) and `stripe-webhook-secret`, kept in user-secrets. **They are never committed.** Tracked files are scanned for keys before every commit.

### Domain

- `PayoutAccount`: TrainerId (key), StripeAccountId, ChargesEnabled, PayoutsEnabled, DetailsSubmitted, UpdatedAt. It is updated from `account.updated` events and on return from onboarding.
- `BillingCustomer`: UserId (key), StripeCustomerId. Created on first checkout using the email claim.
- `CatalogPrice`: (ProgramId, PriceVersion) → StripeProductId, StripePriceId. One Product per program and one Price per price version, created lazily and kept as is (Stripe prices can't be edited).
- `Purchase`:
  - Fields: Id, UserId, ProgramId, TrainerId, Kind (`OneTime` | `Subscription`), AmountMinor, Currency, PriceVersion, FeeMinor, CheckoutSessionId, PaymentIntentId?, ChargeId?, SubscriptionId?, CurrentPeriodEnd?, CancelAtPeriodEnd, TrialEndsAt?, ScheduledPriceVersion?, CreatedAt, PaidAt?, EndedAt?
  - Status: `Pending` → `Trialing`/`Active` → `PastDue` → `Ended` | `Refunded` | `Disputed`.
  - The domain methods enforce the transitions, the refund window and trial eligibility, and raise events.
- `StripeEventRecord`: EventId (key), Type, ReceivedAt, used to drop duplicate webhooks.

### Contracts (`Billing.Contracts`)

- `IPayoutReadiness.IsReadyAsync(trainerId)`.
- Events:
  - `PaidAccessGranted(UserId, ProgramId, PurchaseId)`
  - `PaidAccessEnded(UserId, ProgramId, PurchaseId, Reason)`, where Reason is Cancelled, Refunded, Disputed or Unpaid
  - `PaidAccessConvertedToFree(UserId, ProgramId)`
- Billing reads `IProgramCatalog` (Programs.Contracts) and `IEntitlements` (Subscriptions.Contracts). Subscriptions handles the Billing events. Every reference is to a Contracts project, as the architecture tests require.

### Slices (under `/api/v1/billing`)

- **Payouts** (trainer):
  - `POST /payouts/onboarding` creates the Express account if missing and returns an Account Link URL.
  - `GET /payouts` returns status plus sales totals.
  - `POST /payouts/dashboard` returns an Express login link.
- **StartCheckout** (member): `POST /programs/{id}/checkout`.
  - Fails if the program is free, a draft or archived, if the trainer isn't ready, if the member already has access, or if the buyer is the trainer.
  - Creates the Pending purchase, then the Checkout Session:
    - one-time: `mode=payment` with `application_fee_amount`
    - subscription: `mode=subscription` with `application_fee_percent` and `trial_period_days` if eligible
    - both use `transfer_data.destination` = the trainer's account and `metadata.purchaseId`
  - Returns the session URL. A Pending purchase for the same user and program is reused if its session is still open.
- **Portal** (member): `POST /portal` returns a Customer Portal session URL. The portal is configured for cancel at period end and payment-method updates, with no plan switching.
- **MyPurchases** (member): `GET /me/purchases` returns status, period end, the next price and the cancel flag, for "My programs".
- **Sales** (trainer): `GET /sales?programId&page` lists their own purchases, net of the fee.
- **Refund**: `POST /purchases/{id}/refund`. Allowed for the owning trainer within the window, or for an admin. Ends access through the webhook path.
- **AdminPurchases** (admin): `GET /purchases?status&programId&page`.
- **Webhook** (anonymous): `POST /webhooks/stripe`.
  - Verifies the signature against the raw body and drops duplicates by event id.
  - Handles:
    - `checkout.session.completed` → Active or Trialing, `PaidAccessGranted`
    - `invoice.paid` → update the period, apply a scheduled price version
    - `customer.subscription.updated` → status, cancel flag, period
    - `customer.subscription.deleted` → Ended, `PaidAccessEnded`
    - `charge.refunded` → Refunded, `PaidAccessEnded`
    - `charge.dispute.created` → Disputed, `PaidAccessEnded`
    - `account.updated` → `PayoutAccount`
  - Each event is handled in one `SaveChanges` together with its outbox events. Unknown types return 200. Any failure returns 500 so Stripe retries.

### Event handlers in Billing

- `ProgramPricingChanged` applies the price-change rules above to active subscriptions of that program:
  - Recurring → recurring: ensure the new Stripe Price exists, then create or update a Subscription Schedule that switches at `current_period_end`.
  - Model changes: cancel at period end, as the rules say.
  - Idempotent through `Purchase.ScheduledPriceVersion`.
- `ProgramArchived` sets cancel at period end on that program's subscriptions.

## Part 3: Subscriptions changes

- `Enrollment.StartPaid(userId, programId, purchaseId, now)` and `ReactivatePaid`. Add a nullable `PurchaseId`. A paid enrollment replaces an existing cancelled free row, since the (user, program) row is unique.
- Handlers:
  - `PaidAccessGranted` → activate as Stripe.
  - `PaidAccessEnded` → cancel only if `PurchaseId` matches, so an old purchase's event can't end a newer one. Raises `EnrollmentCancelled` as before, so Discussions keeps working.
  - `PaidAccessConvertedToFree` → Source = Free.
  - All idempotent.
- `Leave` on a Stripe enrollment returns 409 "Cancel from Manage billing".

## Part 4: Gateway and AppHost

- **Gateway:** a new YARP route `/stripe/webhook` → `gymhub-api` `/api/v1/billing/webhooks/stripe`. It sits outside `/gh-api/`, so no XSRF check and no token are added, and the raw body is forwarded as is.
- **AppHost:** the `stripe-secret-key` and `stripe-webhook-secret` parameters are passed to the API.
- **Local webhooks:** run the Stripe CLI separately:

  ```bash
  stripe listen --forward-to https://localhost:7179/stripe/webhook
  ```

  Put its `whsec_…` value in user-secrets. Running the CLI as an optional AppHost container could come later.
- **Docs:** CLAUDE.md (the Billing module, secrets, the webhook route) and README setup steps.

## Part 5: Angular

- **Trainer, Payouts page (`/trainer/payouts`):**
  - Onboarding status, with "Set up payouts" / "Continue setup" buttons that redirect to Stripe and return to the page.
  - An "Open Stripe dashboard" button.
  - A sales table with Refund buttons, disabled outside the window.
- **Pricing form:** a trial-days field for recurring prices. If payouts aren't ready, a notice links to the Payouts page. The form also says existing subscribers move to the new price at renewal.
- **Program overview:** Buy/Subscribe (with "N-day free trial") redirects to Checkout. On the return URL `?checkout=success`, the page polls the enrollment for a few seconds, since the webhook may lag, then shows the content.
- **My programs:** a status badge (trial, renews {date}, ends {date}, past due), the upcoming price change, and a "Manage billing" button that opens the portal.
- **Admin, purchases page (`/admin/purchases`):** filters and a Refund button.
- Services and models for billing, plus specs for the service and the checkout-return polling.

## New dependencies (repo rule: ask first)

- `Stripe.net` (NuGet), added to `Directory.Packages.props`. No new npm packages.

## Order of work (branch `billing` from `main`)

1. Billing module skeleton, the Stripe gateway, `PayoutAccount` and the onboarding slices, the webhook endpoint with `account.updated`, and the Gateway route.
2. Programs: `TrialDays`, the currency rule, and the payout-readiness check.
3. One-time checkout, then the webhook, `PaidAccessGranted`, and the Subscriptions handler.
4. Subscriptions with trials, the portal, renewal and deletion webhooks, and `PaidAccessEnded`.
5. Price-change and archive handlers.
6. Refunds (trainer and admin) and disputes.
7. Angular screens and docs.

## Verification

- `dotnet build GymHub.slnx` with no warnings, and no pending model changes in Billing, Programs or Subscriptions.
- **Unit tests:**
  - `Purchase` transitions
  - the fee calculation
  - trial eligibility
  - the refund window, for trainer and admin
  - the price-change rule table
  - the Programs currency and trial rules
  - the architecture tests, including Billing
- **Functional tests**, with a fake Stripe gateway and webhooks signed with a test secret:
  - an unready trainer can't publish a paid program
  - checkout is rejected for free programs, existing access, or the trainer's own program
  - a completed checkout grants access, and a duplicate event changes nothing
  - a deleted subscription removes access and Discussions access
  - an event for an old purchase doesn't end a newer one
  - a trainer refunds inside the window and gets 403 outside it or for another trainer's sale
  - a price change schedules subscriptions once
  - archiving cancels at period end
  - a bad signature returns 400
- **End to end** with Stripe test mode, the Stripe CLI and test card `4242 4242 4242 4242`:
  - the trainer onboards an Express test account and publishes a monthly program with a 7-day trial
  - the member subscribes, sees the content and is trialing
  - Stripe's test clock reaches renewal, and the payment comes through
  - the trainer raises the price, and the member's next invoice uses it
  - the trainer refunds, and the member loses access to content and board
  - a one-time purchase, followed by a refund from the admin page
- Scan tracked files for keys before committing.

## Open items to verify while building

- Whether Stripe still requires one currency per customer for subscriptions. If it does, a member subscribed in USD can't subscribe to a EUR program, and StartCheckout must reject that clearly.
- Subscription Schedule behaviour when a subscription already has `cancel_at_period_end` set: skip those subscriptions.

## As built

- Everything above is built on branch `billing`, with these differences:
  - **Refunds end access at once.** The refund handler marks the purchase refunded and raises `PaidAccessEnded` itself instead of waiting for `charge.refunded`. That webhook now only covers refunds made in the Stripe dashboard.
  - **A trainer refunding another trainer's sale gets 404**, not 403, so sales of other trainers aren't revealed.
  - **The payouts page has no sales totals.** Subscription renewals aren't summed per purchase, so totals would mislead; the trainer's Stripe Express dashboard has the real earnings. The page lists sales with refund buttons.
  - **The payouts page lives at `/studio/payouts`**, next to the other trainer pages.
  - **Subscription refunds and disputes** need the payment intent of the latest invoice. Stripe's current API no longer puts it on invoices, so `invoice.paid` looks it up through the invoice payments API and stores it on the purchase.
  - **Recurring → free** converts the enrollment to free immediately and stops renewals; the later `customer.subscription.deleted` changes nothing because the enrollment no longer points at the purchase.
  - **Stripe keys are optional in the AppHost.** Without `stripe-secret-key` the API runs and paid features answer "Payments aren't configured".
- **Tests:** 11 Billing functional tests (fake gateway, webhooks signed with a test secret), Purchase domain and Programs pricing unit tests, architecture tests including Billing, and Angular specs for the billing service and status lines.
- **Not verified yet:** the end-to-end run against Stripe test mode, which needs your Stripe test keys and the Stripe CLI. The two open items above (one currency per customer, schedules on subscriptions already set to cancel) are still open; the code skips subscriptions already cancelling and reports Stripe errors at checkout as 409.
