# Plan: Account and legal basics

## Context

Milestone 2c of the MVP roadmap (`docs/plans/mvp-roadmap.md`). Before this change:
- Members couldn't delete their account or download their data.
- There were no terms, privacy page or footer.
- Unknown URLs silently redirected to `/`.
- Sign-up was only reachable from a link on Keycloak's login page.
- Keycloak didn't verify email addresses.

Leaving a free program already worked (`DELETE /api/v1/subscriptions/programs/{id}`), so nothing was needed there.

### Decisions (from Q&A)

| Topic | Decision |
|---|---|
| Trainers | Deletion is **blocked while they have published programs**; they archive them first. Archived programs stay, so members who bought them keep access. |
| Posts and reviews | **Kept word for word, anonymized**: the stored author name becomes "Deleted user". |
| Timing | A **7-day grace period**. The member can sign in and cancel; after 7 days a job erases the data. |
| Email | Keycloak **email verification** is on now. In development it sends through Mailpit. |

## Design

### Personal data contract (BuildingBlocks)
`GymHub.BuildingBlocks.Privacy.IPersonalDataSource`, registered by each module that stores user data:
- `Section`: the name of the module's part of the export.
- `ExportAsync(userId)`: returns that part.
- `GetDeletionBlockerAsync(userId)`: a reason the account can't be deleted yet, or null.
- `EraseAsync(userId)`: deletes or anonymizes the data. It must be idempotent.

The Accounts module calls the sources directly, not through an outbox event. That way the Keycloak user is deleted only after every module has erased its data, and a failure is retried on the next run.

| Module | Blocker | Erase |
|---|---|---|
| Billing | a subscription that is still renewing (Trialing/Active/PastDue, not set to cancel at period end) | delete the `BillingCustomer` mapping. Purchases stay for accounting under the now-meaningless user id. |
| Programs | published programs | nothing (archived programs stay) |
| Subscriptions | | delete enrollments |
| Trainers | | delete the member and trainer profile, follows both ways, and applications |
| Discussions | | posts' author name becomes "Deleted user"; delete their reports |
| Reviews | | reviews' author name becomes "Deleted user"; delete their reports. Ratings don't change. |
| Training Log | | delete sessions |
| Notifications | | delete notifications, preferences, email address and queued emails |

### Accounts module (new, route prefix `account`, schema `accounts`)
- **`AccountDeletion`:** `UserId`, `RequestedAt`, `ScheduledFor` (+7 days) and `CompletedAt`. Cancelling deletes the row; completed rows stay as a record.
- **Endpoints** (all for the signed-in user):
  - `GET /api/v1/account` returns the pending deletion, if any, and the current blockers.
  - `GET /api/v1/account/export` returns a JSON file with every module's section.
  - `POST /api/v1/account/deletion { confirmation: "delete my account" }` schedules the deletion. It answers 409 if anything blocks it.
  - `DELETE /api/v1/account/deletion` cancels it.
- **`AccountDeletionJob`** runs hourly. For each due deletion it:
  1. re-checks the blockers and skips the user if any remain, keeping them on the account page;
  2. erases each module's data;
  3. deletes the Keycloak user;
  4. marks the deletion done.
- **Keycloak admin client:** `IIdentityAdmin` moves from Trainers to BuildingBlocks (`Identity`) and gains `DeleteUserAsync`, which treats 404 as done. The `gymhub-api-admin` service account already has `manage-users`.

### Gateway
- `/signup` starts the same OIDC challenge as `/login`, with `prompt=create`, so Keycloak opens its registration form.

### Keycloak realm
- `verifyEmail: true`, and `smtpServer` points at the Mailpit container. Password reset was already on.
- The realm only imports into an empty volume, so delete the `gymhub-keycloak` volume once.

### Angular
- **`/me/account`:** download your data, delete your account (type the phrase), the blockers, and cancelling a pending deletion. Linked from the profile page.
- **Banner:** a site-wide banner while a deletion is pending, with "Keep my account".
- **`/terms` and `/privacy`:** placeholder text, clearly marked; the final text is Milestone 8.
- **Footer:** links to terms, privacy, programs and trainers.
- **Not found:** a real page for unknown URLs.
- **Sign up:** a button next to Sign in.

## Tests
- **Functional:**
  - scheduling, the blockers, cancelling, and the job erasing data in each module
  - a deletion that isn't due yet is left alone
  - the Keycloak user is deleted
  - export sections
- **Angular:** the account page's confirmation rule.

## As built
- **One change from the design:** after scheduling, the member is not signed out. The account page and a site-wide banner show the date and a **Keep my account** button. The 7 days work as intended, and nobody is signed out unexpectedly.
- **Sign-up and PAR:** the Gateway sends authorization requests through PAR, and Keycloak 26.5 ignores a `prompt` sent that way. `/signup` therefore skips PAR (`OnPushAuthorization` → `SkipPush`), and login still uses it.
- **Avatars:** `IObjectStorage` gained `DeletePrefixAsync`, so deletion removes every avatar the user ever uploaded, not just the current one.
- **Tests:**
  - 4 functional tests cover scheduling and cancelling, blockers before and during the grace period, and the job erasing and anonymizing data.
  - Unit tests cover the Keycloak user delete.
  - The Billing blocker (a renewing subscription) has no functional test; setting one up needs the Stripe webhook flow.
- **Not covered:**
  - The Stripe customer itself isn't deleted (only GymHub's link to it).
  - A trainer's Stripe Express account stays, since payouts or refunds may still be pending.
