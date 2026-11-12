# Plan: Notifications module

## Context

The modules already publish integration events that nobody listens to. A new week unlocks, someone replies to your post, a trainer you follow publishes a program, your subscription price changes at renewal, and nobody hears about it unless they happen to look.

Notifications listens to those events. It turns each one into notifications for the right people, shows them in an in-app inbox that updates live, and emails the ones worth leaving the inbox for.

### Decisions (from Q&A)

| Topic | Decision |
|---|---|
| Channels | **In-app** (a bell with an unread count, plus an inbox, pushed live over SignalR) **and email**, one email per notification. |
| Events in v1 | **All four groups:** week released, discussion activity, billing, and trainer news. |
| Preferences | **Per type, per channel:** a settings page with an in-app switch and an email switch for each kind of notification. |

### Assumptions (tell me if any are wrong)

- **Where email addresses come from.** Notifications stores the `email` and `email_verified` claims from the user's token whenever they use the app (the bell's unread-count call). That needs no Keycloak admin access and no realm change. A user who has never signed in since this ships has no address yet, so they get in-app notifications only.
  - **To verify:** the API's access token carries `email`. Billing already reads it, but if it's missing, add the `email` scope to the `gymhub-bff` client (a realm change, so it needs a fresh Keycloak volume).
- **Unverified addresses get email too, in dev.** All local mail goes to Mailpit. In production, only verified addresses get email; that's a config switch.
- **Some notifications can't be turned off by email:** an upcoming price change, a failed payment, access ending, a trial ending, and trainer approval. They are about money or access, so the settings page shows those email switches as locked on. Everything else follows the user's switches.
- **Who gets what:**

  | Type | Recipients | In-app | Email (default) |
  |---|---|---|---|
  | Week released | Active members of the program | on | on |
  | Reply to your post | Author of the parent post (not when replying to yourself) | on | off |
  | Your answer was marked | The marked post's author (not the person marking it) | on | off |
  | New thread on your board | The program's trainer (not when the trainer started it) | on | on |
  | Followed trainer published | The trainer's followers | on | on |
  | New subscriber or sale | The trainer | on | off |
  | Trainer application approved | The applicant | on | on (locked) |
  | Price changes at renewal | That subscriber | on | on (locked) |
  | Trial ends in 3 days | That subscriber | on | on (locked) |
  | Payment failed | That subscriber | on | on (locked) |
  | Access ended (cancelled, refunded, disputed, unpaid) | That member | on | on (locked) |

- **Recipients are worked out when the event arrives.** Each notification stores its title, body and link as they were. Opening the link still goes through the usual access checks, so a notification never shows content the user can't see. Its text only names the program, thread or week.
- **Discussion replies notify the parent post's author only.** Most replies in a question thread answer the opening post, so the asker hears about them. "Follow this thread" for everyone involved can come later.
- **One notification per user per source.** A unique dedupe key per user (for example `week-released:{weekId}`) makes every handler idempotent under the outbox's at-least-once delivery.
- **Retention:** read notifications are deleted after 90 days, and unread ones after 180, by a daily cleanup job.
- **Single API instance.** SignalR stays in memory, as in Discussions.

## Part 1: Changes to other modules (contracts and events)

New lookups, each implemented in its own module and read through `.Contracts` only:
- **Subscriptions.Contracts:** `IProgramMembers.GetActiveMemberIdsAsync(programId)`, the recipients of week releases.
- **Trainers.Contracts:** `IFollowers.GetFollowerIdsAsync(trainerId)`, the recipients of "followed trainer published".

New Billing events, raised where Billing already learns these facts:
- `SubscriptionPriceChangeScheduled(UserId, ProgramId, PurchaseId, AmountMinor, Currency, Interval, EffectiveAt)`, raised when `Purchase.SchedulePriceChange` runs. `EffectiveAt` is the current period's end.
- `SubscriptionTrialEnding(UserId, ProgramId, PurchaseId, TrialEndsAt)`, raised from Stripe's `customer.subscription.trial_will_end` webhook, which arrives 3 days before the trial ends.
- `SubscriptionPaymentFailed(UserId, ProgramId, PurchaseId)`, raised when a purchase moves into `PastDue`.
- `ProgramSold(TrainerId, ProgramId, UserId, PurchaseId, Kind)`, raised alongside `PaidAccessGranted`, for the trainer's "new subscriber or sale".

Notifications also consumes these existing events:
- `ProgramWeekReleased`, `ProgramPublished`
- `DiscussionThreadStarted`, `DiscussionReplyPosted`, `DiscussionAnswerMarked`
- `TrainerApproved`, `PaidAccessEnded`
- free enrollments, for the trainer's "new subscriber": this needs a small new `EnrollmentStarted(UserId, ProgramId, Source)` event in Subscriptions, raised for free enrollments only, since paid ones are covered by `ProgramSold`

## Part 2: Notifications module (`GymHub.Modules.Notifications` + `.Contracts`, schema `notifications`)

### Domain

- `NotificationType` enum: the eleven types in the table. Each type's defaults and locked email switch live in one `NotificationCatalog` definition.
- `Notification`:
  - Fields: Id, UserId, Type, Title, Body, Link (an app path such as `/programs/{id}/discussion/{threadId}`), DedupeKey, CreatedAt, ReadAt?
  - Unique on (UserId, DedupeKey), with an index on (UserId, ReadAt, CreatedAt).
  - Methods: `MarkRead`.
- `NotificationPreference`: (UserId, Type) → InApp, Email. A row exists only when the user changed the default.
- `Recipient`: UserId (key), Email?, EmailVerified, UpdatedAt. Refreshed from token claims.
- `EmailMessage`:
  - Fields: Id, NotificationId, ToAddress, Subject, TextBody, HtmlBody, Status (`Pending` | `Sent` | `Failed`), Attempts, NextAttemptAt, SentAt?, LastError?
  - It is queued in the same `SaveChanges` as its notification.

### Fan-out

- One handler per consumed event, under `Handlers/`. Each one:
  1. works out the recipients, using the contract lookups and `IProgramCatalog` for titles and trainer ids
  2. drops the actor (nobody is notified of their own action)
  3. drops recipients who turned off in-app for this type
  4. inserts the notifications, skipping keys that already exist
  5. queues emails where the email switch is on and the recipient has an address
  6. pushes the new unread counts
- Week releases can reach many members, so the recipients are inserted in batches of 500, and `ON CONFLICT DO NOTHING` handles dedupe.
- An in-app switch that is off means no notification at all. That type's email is still sent if its email switch is on, and locked emails always are.

### Email

- `IEmailSender` with an SMTP implementation, configured by `Notifications:Smtp` (host, port, from address, user and password optional).
- `EmailDispatcher` is a `BackgroundService`. Every 10 seconds it sends due `Pending` messages, retrying with backoff (1, 5, 30 minutes, then 2 hours) and marking a message `Failed` after 5 attempts.
- Templates are plain text plus simple HTML: the title, the body, a button to open the link on `PublicBaseUrl`, and a footer linking to the settings page.
- **Local dev:** a Mailpit container in the AppHost (SMTP on 1025, web UI on 8025, linked from the Aspire dashboard). No secrets needed.

### Slices (under `/api/v1/notifications`)

- **ListNotifications:** `GET /?unreadOnly&pageIndex&pageSize`, newest first.
- **UnreadCount:** `GET /unread-count`. The bell calls it on load, and it also refreshes the user's `Recipient` row from their claims.
- **MarkRead:** `POST /{id}/read` and `POST /read-all`.
- **Preferences:**
  - `GET /preferences` returns every type with its label, group, current switches and whether email is locked.
  - `PUT /preferences` takes a list of `{type, inApp, email}`. Locked email switches can't be turned off.
- **Cleanup job:** a daily background job deletes old notifications and sent emails, and keeps failed emails for 30 days.

### Live updates

- `NotificationsHub` at `/api/v1/notifications/hub` is `[Authorize]` and needs no groups. It sends to `Clients.User(sub)`, with an `IUserIdProvider` that reads `sub`.
- Messages carry no content, only `notificationsChanged(unreadCount)`. The client refetches, as Discussions does.
- The Gateway already proxies hubs under `/gh-api/` and accepts WebSocket handshakes by `Origin`, so nothing changes there.

## Part 3: AppHost

- A Mailpit container (`axllent/mailpit`, pinned version) with `smtp` and `http` endpoints. The API gets `Notifications__Smtp__Host`/`Port` from it.
- `Notifications__PublicBaseUrl` from the Gateway's https endpoint, as Billing does.

## Part 4: Angular

- **Bell in the nav** for signed-in users:
  - The unread badge comes from `/unread-count` and stays live through the hub.
  - Clicking it opens a dropdown with the latest 10 notifications. Clicking one marks it read and navigates to its link. A "Mark all read" button sits at the top, and a "See all" link at the bottom.
- `me/notifications`: the paged inbox, with an "Unread only" filter.
- `me/notifications/settings`: a table of types grouped as Training, Discussions, Billing and Trainer, with two switches each. Locked switches show as disabled and checked, with "Required for billing and access" as the reason.
- `NotificationsRealtimeService`: a small SignalR wrapper on the same pattern as `DiscussionsRealtimeService`, but its own connection, with no groups to join.
- Specs for the service, the bell's unread logic and the settings form.

## New dependencies (repo rule: ask first)

- **MailKit** (NuGet), for SMTP. Microsoft no longer recommends `System.Net.Mail.SmtpClient` for new code.
- **Mailpit** container image, for local dev only.
- No new npm packages, since `@microsoft/signalr` is already installed.

## Order of work (branch `notifications` from `main`)

1. Module skeleton, domain, migration, `NotificationCatalog`, and the list, read and unread-count slices.
2. Contract lookups (`IProgramMembers`, `IFollowers`) and the handlers for existing events: week released, discussions, trainer approved, followed trainer published, access ended.
3. New Billing and Subscriptions events (price change, trial ending, payment failed, program sold, free enrollment) and their handlers.
4. Preferences and their enforcement in fan-out.
5. Email: the queue, the dispatcher, MailKit, and Mailpit in the AppHost.
6. Live updates: the hub and its push.
7. Angular: the bell, inbox, settings and realtime service. Then docs.

## Verification

- `dotnet build GymHub.slnx` with no warnings, no pending model changes, and the architecture tests including Notifications.
- **Unit tests:**
  - catalog defaults and locked emails
  - preference resolution (default, override, locked)
  - actor exclusion
  - dedupe keys
  - email backoff schedule
- **Functional tests**, with a fake `IEmailSender` and dispatched outboxes:
  - a released week notifies active members only, and dispatching the event twice creates no duplicates
  - a reply notifies the parent author but not the replier, and a self-reply notifies nobody
  - a followed trainer publishing notifies followers
  - turning off in-app for replies stops them, while a locked billing email still goes out
  - the unread count, mark read and read-all
  - another user's notification returns 404
  - email is queued only for recipients with an address, and a failed send is retried and then marked `Failed`
  - the hub pushes `notificationsChanged` to the right user only
- **End to end with the AppHost:**
  - the trainer publishes, and a follower sees the bell update live
  - a member gets a week-released notification and the email in Mailpit
  - a reply shows in-app but sends no email by default
  - turning email on for replies makes the next reply arrive in Mailpit

## As built

- Everything above is built on branch `notifications`, with these differences:
  - **No `.Contracts` project.** No other module needs anything from Notifications yet; one can be added when one does.
  - **Notification rows are kept even when in-app is off** (`ShowInApp = false`, hidden from the inbox and count), so their dedupe key also stops a redelivered event from emailing twice.
  - **A payment failure notifies once per episode** (when the purchase moves into `PastDue`), keyed by the event id, so a later failure in another period notifies again.
  - **"Your answer was marked"** doesn't exclude the person marking it, because the event doesn't carry who marked it. In practice the asker marks someone else's reply.
  - **Mailpit** is pinned at `axllent/mailpit:v1.27`, and `Notifications:EmailUnverifiedAddresses` is turned on by the AppHost only.
- **Tests:** catalog, preference resolution, email backoff and template escaping unit tests; Purchase tests for the new Billing events; 7 Notifications functional tests (week released with dedupe and email, retries, discussion activity without notifying the actor, followers and new members, preferences with locked billing emails, inbox ownership and read state, hub pushing to the right user only); Angular specs for the service, preferences logic and realtime service.
