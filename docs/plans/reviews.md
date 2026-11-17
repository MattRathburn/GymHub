# Plan: Reviews module

## Context

Milestone 2a of the MVP roadmap (`docs/plans/mvp-roadmap.md`). Members rate and review the programs they have, trainers reply, reviews can be reported, and the average rating shows in the catalog and on the program page.

### Decisions (from Q&A)

| Topic | Decision |
|---|---|
| Who can review | **Anyone with current access** (`IEntitlements`) to a published program. The program's own trainer can't review it. |
| When access ends | **The review stays** and keeps counting, but can't be edited until access returns. The author can always delete it. |
| Moderation | Reported reviews show on the **existing Reports page, in a new "Reviews" tab**. Admins dismiss a report or hide the review. |

## Design

### Module `GymHub.Modules.Reviews` (+ `.Contracts`)
- Route prefix and schema `reviews`.
- **`Review`:** one per member per program, enforced by a unique index.
  - stars 1–5 and optional text (up to 2,000 characters)
  - the program's trainer id, kept so replies can be authorized
  - a username snapshot as a fallback name
  - created and updated times
  - hidden flag (admin)
  - **trainer reply:** one per review, editable and removable
- **`ReviewReport`:** the same shape and rules as Discussions' `PostReport`. One open report per reporter per review, and you can't report your own review.
- **`ProgramRating`:** per program, the count and sum of the visible reviews, plus a version. It is recomputed in the same transaction as every change that affects it (write, delete, hide, unhide), with an optimistic concurrency token and a retry. Each recompute raises **`ProgramRatingChanged(ProgramId, Count, Sum, Version)`**.
- **`ReviewPosted(ReviewId, ProgramId, TrainerId, AuthorId, Stars)`** is raised when a review is first written, not on edits.

### Programs keeps a copy of the rating
- `TrainingProgram` gets `RatingCount`, `RatingSum` and `RatingVersion`.
- A handler stores `ProgramRatingChanged` only when its version is newer. Outbox delivery is at-least-once and can arrive out of order.
- `ProgramCard` and `ProgramOverview` gain `Rating(Average, Count)`. Milestone 2b can sort by rating in SQL.

### Endpoints (`/api/v1/reviews`)
| Endpoint | Who | What |
|---|---|---|
| `GET /programs/{programId}?pageIndex=&pageSize=` | anyone | Visible reviews, newest first. Includes the summary: average, count, and how many reviews gave each star rating. |
| `GET /programs/{programId}/mine` | signed in | My review (even when hidden), `canWrite` and the reason when not. |
| `PUT /programs/{programId}/mine` | access, not the trainer | Write or edit my review. Needs current access and a published program. |
| `DELETE /programs/{programId}/mine` | author | Delete my review. Always allowed. |
| `PUT /{reviewId}/reply`, `DELETE /{reviewId}/reply` | the program's trainer | Reply to a review, or remove the reply. |
| `POST /{reviewId}/reports` | signed in, not the author | Report a review. |
| `GET /reports?status=`, `POST /reports/{id}/dismiss`, `POST /reports/{id}/action` | admin | The queue. "Action" hides the review and closes its open reports. |

Names come from `IProfileDirectory`, falling back to the username snapshot, the same way as Discussions' `AuthorLookup`.

### Notifications
- A new `NotificationType.ReviewPosted` in the Trainer group, in-app on and email off. Its handler notifies the trainer, deduplicated by review id.

### Angular
- `features/reviews/`:
  - model and service
  - `star-rating` display component
  - `program-reviews` component on the program page: the summary and bars, my review form (stars and text) when allowed, the list with trainer replies, a reply form for the trainer, reporting and "Load more"
- **Program cards and the program page** show the stars and count.
- **Admin Reports page:** "Posts" and "Reviews" tabs.

## Tests
- **Unit:**
  - `Review` rules: stars, text length, replies
  - `ReviewReport` rules
  - the rating summary and average rounding
  - Programs ignores stale `ProgramRatingChanged` events
- **Functional:**
  - write, edit and delete with access; 403 without access and for the trainer
  - one review per member
  - anonymous list with the summary
  - access ended means editing is locked but deleting still works
  - a trainer reply, and only that trainer can reply
  - report, then admin action: the review is hidden and drops out of the rating
  - after the outbox runs, the program card shows the rating
  - the trainer gets a notification
- **Architecture:** the new assemblies are registered.
- **Angular:** specs for the service and the star and summary helpers.

## As built

- **Built as planned** on branch `reviews`, which was branched from `ci-cleanup` so it includes Milestone 1.
- **Slices:** `ProgramReviews`, `MyReview`, `ReplyToReview`, `ReportReview` and `ReviewReports`. `ProgramGate`, `RatingWriter` and `ReviewMapper` are shared.
- **Rating recompute:** done under `pg_advisory_xact_lock` inside the execution strategy, the same pattern as Billing's `StartCheckout`, rather than with a row-version retry.
- **Programs:** `ProgramRatingHandler` applies the rating with one guarded `ExecuteUpdate` (`RatingVersion < version`).
- **Tests:**
  - 11 unit tests, plus the boundary tests that now cover the module
  - 7 functional tests
  - 6 Angular specs
- **End-to-end run in the AppHost:**
  - The trainer published a program and saw no review form.
  - The member subscribed, the form appeared, and they posted 4 stars. The card showed ★★★★☆ 4.0 · 1 review.
  - The trainer got the notification, which opened the page at the reviews. The trainer replied and reported the review.
  - The admin hid it from the Reviews tab, and the rating went back to "No reviews yet".
- **Left for later:** an admin "unhide" for a hidden review. Today an admin would need the database to undo a hide.
