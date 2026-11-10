# Plan: Discussions module (+ member profiles, live updates)

## Context

Every program gets a discussion board. Only the program's subscribers, its trainer and admins can see it. It's where members ask questions and help each other, and where the trainer gives guidance.

### Decisions (from Q&A)

| Topic | Decision |
|---|---|
| Structure | Threads with **nested replies** (a reply can answer another reply). |
| Author names | Add **basic member profiles**: a display name and avatar for every user, not only trainers. |
| Moderation | Authors can edit or delete their own posts. The program's trainer and admins can hide posts and lock or pin threads. Anyone can **report** a post, and admins work through a report queue. |
| Live updates | **SignalR now.** New threads, replies and moderation changes appear on open boards without a refresh. |

### Assumptions (tell me if any are wrong)

- **Access is checked on every read and write.** Allowed: the program's trainer (`IProgramCatalog` → `TrainerId`), admins, and users with `IEntitlements.HasAccessAsync`. Unsubscribing removes access.
- **Archived programs** keep a readable, locked board, so no new posts. Draft programs have no board.
- **Posts are plain text** with line breaks, rendered escaped (no Markdown or HTML), and no images in v1. Limits: title 150 characters, body 5,000.
- **Nesting:** replies can go up to **8 levels** deep. Below that, the UI keeps the tree but stops indenting. A whole thread loads at once, capped at 500 posts in v1.
- **Deleting a post that has replies** keeps a "[deleted]" placeholder, so the tree stays intact. A post with no replies is removed.
- **Single API instance.** SignalR runs in memory, and the existing Redis could be its backplane later, as with the outbox.
- **Question threads:** the program's trainer or the thread's author can mark one reply as the answer. Cheap to add, and it fits the "ask the trainer" use case.

## Part 1: Member profiles (Trainers & Profiles module)

The profiles belong in the existing `Trainers` module ("Trainers & Profiles"), so there's no new module.

- **Domain:** `MemberProfile` (UserId = Keycloak `sub`, DisplayName, AvatarObjectKey?, CreatedAt, UpdatedAt).
  - DisplayName is 2–40 characters.
  - The avatar uses the same presigned-upload pattern and key prefix as `TrainerProfile`.
- **Slices:**
  - `EditMyMemberProfile`: `GET`/`PUT /trainers/me/member-profile`.
  - A member-avatar upload: presign, then attach.
- **Contract:** `IProfileDirectory.GetAuthorsAsync(ids)` in `Trainers.Contracts`, returning `AuthorInfo(UserId, DisplayName, AvatarUrl?, IsTrainer)`.
  - The name comes from the trainer profile if there is one, then the member profile, then nothing.
  - Discussions falls back to a name snapshot it stores on each post (see below).
- **Migration:** add a `member_profiles` table to the `trainers` schema.
- **Angular:** a `me/profile` page (display name and avatar). The discussion board prompts users without a profile to set a display name before they post.

## Part 2: Discussions module

**Projects:** `src/Modules/Discussions/GymHub.Modules.Discussions` plus `.Contracts`.
- Schema `discussions`, a `DiscussionsDbContext` with its own outbox, and the `InitialDiscussions` migration.
- Registered in `GymHub.Api` and `GymHub.MigrationService`, with a `ModuleBoundaryTests` entry.

### Domain

**`DiscussionThread`** (aggregate):
- Id, ProgramId, AuthorId, AuthorNameSnapshot
- Kind: `Question` | `General`
- Title, CreatedAt, LastActivityAt, ReplyCount
- PinnedAt?, LockedAt?, HiddenAt?, AnswerPostId?

**`Post`**:
- Id, ThreadId, ParentPostId? (null = the thread's opening post), Depth
- AuthorId, AuthorNameSnapshot, Body
- CreatedAt, EditedAt?, DeletedAt?, HiddenAt?, HiddenBy?

**`PostReport`**:
- Id, PostId, ThreadId, ProgramId, ReporterId, Reason (`Spam` | `Abuse` | `OffTopic` | `Other`), Note?
- Status (`Open` | `Dismissed` | `Actioned`), CreatedAt, ResolvedAt?, ResolvedBy?
- One open report per reporter per post.

**Invariants:**
- You can't reply to a locked or hidden thread, or past the maximum depth.
- Only the author edits or deletes a post. Only the program's trainer or an admin pins, locks or hides.
- The answer must be a reply in the same thread, and only on `Question` threads.

**Author snapshot:** the name is taken from the token's `preferred_username` when the post is written. Reads prefer the live `IProfileDirectory` name.

### Slices (under `/api/v1/discussions`)

**Reading:**
- `ListThreads`: `GET /programs/{programId}/threads?page&kind&q`. Pinned threads come first, then by last activity.
- `GetThread`: `GET /threads/{threadId}`. Returns the thread plus all of its posts as a flat list with `parentPostId`, which the client builds into a tree. Authors come from `IProfileDirectory`, with a trainer badge for the program's trainer.

**Writing:**
- `StartThread`: `POST /programs/{programId}/threads`.
- `Reply`: `POST /threads/{threadId}/posts` with `{ parentPostId, body }`.
- `EditPost` / `DeletePost`: `PUT`/`DELETE /posts/{postId}`, author only.

**Moderation (program's trainer or admin):**
- `ModerateThread`: pin/unpin, lock/unlock, hide/unhide.
- `ModeratePost`: hide/unhide.
- `MarkAnswer`: the trainer or the thread's author.

**Reports:**
- `ReportPost`: `POST /posts/{postId}/reports`, for anyone with board access.
- `ReviewReports`, admin only:
  - `GET /reports?status=Open`
  - `POST /reports/{id}/dismiss`
  - `POST /reports/{id}/action`, which hides the post and closes every open report on it

**Shared:** `BoardAccess` (`Features/Shared`) answers "can view / can post / can moderate" for a user and program, using `IProgramCatalog` and `IEntitlements`. Every slice uses it.

### Contracts and events

These go through the outbox, for the Notifications module later:
- `DiscussionThreadStarted(ThreadId, ProgramId, AuthorId, Title)`
- `DiscussionReplyPosted(PostId, ThreadId, ProgramId, AuthorId, ParentAuthorId?)`
- `DiscussionAnswerMarked(ThreadId, PostId, PostAuthorId)`

## Part 3: Live updates (SignalR)

**API:**
- `AddSignalR()` in `GymHub.Api`. The Discussions module maps `DiscussionsHub` at `/api/v1/discussions/hub` with `RequireAuthorization()`.
- `JoinBoard(programId)` checks `BoardAccess` before adding the connection to group `program:{id}`. `JoinThread(threadId)` does the same for group `thread:{id}`. Leaving works the same way in reverse.
- After a successful `SaveChanges`, handlers push through `IHubContext` (best effort, not through the outbox):
  - `ThreadStarted` / `ThreadUpdated` to the board group
  - `PostAdded` / `PostUpdated` / `PostRemoved` to the thread group
- **As built:** payloads carry ids only (`threadChanged(programId, threadId)`), and clients refetch over REST. Sending DTOs would leak hidden posts to non-moderators in the same group.

**Gateway:**
- The existing `/gh-api/{**}` route already covers the hub, and YARP proxies WebSockets.
- **To verify:**
  - Does `GatewayService.AddToken` attach the bearer token on the WebSocket upgrade request?
  - The negotiate `POST` must carry `X-XSRF-TOKEN`, which the SignalR client can send through its `headers` option.
  - If the upgrade doesn't get the token, add it in the transform for upgrade requests. Don't fall back to `access_token` in the query string, because tokens must stay server-side.
- **As built:** the token is attached to the upgrade without changes. However, the XSRF check rejected HTTP/2 WebSocket handshakes, which arrive as CONNECT requests. The Gateway now accepts WebSocket handshakes only from its own `Origin` instead.
- **Known limit:** the token is checked when the connection opens. A connection outlives the token's expiry until it reconnects. That's acceptable here, and every write still goes through REST with a fresh token.

**Angular:**
- `DiscussionsRealtimeService` wraps `@microsoft/signalr`, with automatic reconnect and re-joining groups after a reconnect. It exposes the events as signals or observables that the board and thread pages merge into their state.

## Part 4: Angular screens

- **Program detail:** a "Discussion" tab or link, visible when the user has access.
- `programs/:id/discussion`: the thread list. It has filters (All, Questions) and search, a "New thread" form, and pinned and locked badges.
- `programs/:id/discussion/:threadId`: the thread page.
  - Nested replies with indentation (capped visually at 8 levels), inline reply boxes, and edit and delete on your own posts.
  - The answer is highlighted, and the trainer's posts carry a badge.
  - The trainer and admins get a moderation menu (pin, lock, hide). A report dialog asks for a reason and an optional note.
- `admin/reports`: the report queue, linking to each post, with Dismiss and Hide actions.
- `me/profile`: the member profile, from Part 1.
- **Specs:** the service, building the reply tree from the flat list, and merging realtime events into state.

## New dependencies (repo rule: ask first)

- **npm:** `@microsoft/signalr`, the SignalR browser client.
- **NuGet, test only:** `Microsoft.AspNetCore.SignalR.Client`, for one hub functional test.
- Server-side SignalR is part of ASP.NET Core, so the API needs no package.

## Order of work (branch `discussions` from `main`)

1. **Member profiles:** domain, slices, migration, `IProfileDirectory`, tests.
2. **Discussions core:** domain, threads and posts, access, events, migration, tests.
3. **Moderation and reports:** the slices and their tests.
4. **SignalR:** the hub and its pushes in the API, then the Gateway check, then a functional test.
5. **Angular:** profile, board, thread, moderation and report screens, plus the realtime service.
6. **Docs:** `CLAUDE.md` and README, including clearing the stale Trainers line and the removed `.github/skills` references.

## Verification

- `dotnet build GymHub.slnx` with zero warnings, and `has-pending-model-changes` clean for each module.
- **Unit tests:**
  - thread and post invariants: depth limit, a locked thread rejects replies, delete with or without replies, answer rules
  - report de-duplication
  - module boundaries
- **Functional tests (Testcontainers):**
  - a non-subscriber gets 403 on the board, while a subscriber, the owner and an admin get 200
  - an unsubscribed user loses access
  - an archived program's board is read-only
  - a non-owning trainer can't moderate
  - a report followed by an admin action hides the post and closes the reports
  - nested replies come back with the right parent and depth
  - posting writes the outbox events
  - a hub client that joined a board receives `PostAdded`, and a non-subscriber's `JoinBoard` is refused
- **Angular:** `npm test` passes and `npm run build` succeeds.
- **End to end with the AppHost:**
  1. `member` sets a display name, subscribes to a free program, starts a question thread, and replies to a reply.
  2. `trainer`, in a second browser tab, sees the thread appear live, replies with a trainer badge, marks the answer, and pins the thread.
  3. `member` reports a post. `admin` hides it from the queue, and it disappears live for both.
  4. Confirm WebSocket traffic goes through the Gateway, with no token in the URL.
