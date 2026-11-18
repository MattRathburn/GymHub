# Milestone 2d: Admin moderation

Admins can take programs down, suspend users, revoke the trainer role and see a few numbers about the site.

## Decisions
- **Programs:** archive only. Admins can already archive any program (`ArchiveProgram`); 2d adds an admin program list and an Archive button on the program page. Archiving can't be undone: the program leaves the catalog, renewals stop and buyers keep access.
- **Suspension:** sign-in only. The Keycloak user is disabled and its sessions ended, and the API answers 401 to tokens that are still valid. Programs, posts and reviews stay; an admin archives or hides them separately if needed. Reactivating undoes the suspension. Admins can't suspend themselves or another admin.
- **Revoking the trainer role:** the Keycloak `trainer` role is removed and the trainer's published programs are archived in the same step (buyers keep access). Their approved application becomes `Revoked`, so they may apply again. The profile stays, because archived programs still show the trainer's name.

## Design
- **Identity:** `IIdentityAdmin` (BuildingBlocks) gains `SearchUsersAsync`, `CountUsersAsync`, `GetRoleMemberIdsAsync`, `SetEnabledAsync` (disabling also ends the user's sessions) and `RemoveRealmRoleAsync`. The `gymhub-api-admin` service account already has `manage-users` and `view-realm`, which is enough, so the realm doesn't change.
- **Accounts module** owns account state, so suspensions live there:
  - `AccountSuspension` (`UserId`, `SuspendedAt`, `SuspendedBy`, `Reason`), table `accounts.account_suspensions`.
  - The API guard becomes `UseAccountGuard` (BuildingBlocks `Auth`): 401 for deleted (`IDeletedAccounts`) or suspended (`ISuspendedAccounts`) users. A suspension is cached for a minute per instance; the instance that suspends or reactivates updates its cache at once.
  - Admin endpoints under `/api/v1/account/admin`: `GET users` (Keycloak search, with trainer/admin/suspended/pending-deletion flags and profile names), `POST`/`DELETE users/{id}/suspension`, and `GET metrics`.
- **Trainers module:** `POST /api/v1/trainers/{userId}/revoke` (admin). Marks the approved application `Revoked` and raises `TrainerRevoked`. Its handlers remove the Keycloak role (Trainers) and archive the trainer's published programs (Programs). Both run from the outbox and are idempotent.
- **Programs module:** `GET /api/v1/programs/admin` (admin): every program with search, a status filter and paging, newest first, with the trainer's name.
- **Dashboard:** BuildingBlocks `IAdminMetricsSource`. Each module that has numbers registers one, and `GET /account/admin/metrics` collects them:
  - Accounts: users, suspended users, pending deletions; trainers (Keycloak role members).
  - Trainers: applications waiting.
  - Programs: published, drafts, archived.
  - Subscriptions: active enrollments, new enrollments in the last 30 days.
  - Billing: sales and platform fees in the last 30 days, per currency, from payments recorded in that window at the purchase's price, less refunds.
  - Discussions and Reviews: open reports.
- **Angular:** `/admin` (dashboard), `/admin/users`, `/admin/programs`, plus an admin Archive button on the program page. The header's admin links become one "Admin" link to the dashboard, which links to every admin page.

## Known gaps
- A revoked trainer keeps the role in their current access token until it refreshes (minutes). Publishing checks a `TrainerRevocation` row instead, so they can't publish in that window; other trainer actions (editing drafts) still work until the token refreshes.
- Sales figures use the purchase's current amount for every payment, so a payment made before a price change counts at the new price. Disputed payments are left out; refunds are subtracted.

## As built
- Everything above, with 7 functional tests (`AdminApiTests`), Keycloak admin unit tests for the new calls, and Angular specs for the admin model and service.
- The three older admin pages (trainer applications, reports, purchases) share the admin tabs; `/admin/reports?tab=reviews` opens the Reviews tab, which the dashboard links to.
- The program page shows **Archive (admin)** to admins on programs that aren't theirs, then goes to the admin program list (archived programs have no public page).
- `SetEnabledAsync` reads the whole Keycloak user and writes it back, because a partial `PUT` can clear profile attributes.
- Not covered by tests: the real Keycloak calls (checked by hand in the AppHost) and the 1-minute suspension cache on other instances.
