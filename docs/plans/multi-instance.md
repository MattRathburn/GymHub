# Milestone 4: Multi-instance readiness

Lets the API and the Gateway run as several instances behind a load balancer. Nothing changes for members.

## Decisions
- **SignalR:** a Redis backplane. Redis comes from the AppHost now (a container), and from Azure Cache for Redis in milestone 6.
- **Gateway Data Protection keys:** in Redis, with the sessions. Milestone 6 can add Key Vault encryption of the key ring.
- **Rate limits:** stay per instance. With N instances a client can get up to N times the limit. They are abuse guards, so that margin is accepted.
- **Redis is optional.** Without a `redis` connection string, everything runs in-process as before: SignalR has no backplane, and the Gateway uses an in-memory session cache and per-instance keys. Tests and single-instance runs don't need Redis.

## Design
- **Outbox:** `OutboxDispatcher` claims its batch with `SELECT ... FOR UPDATE SKIP LOCKED` in a transaction, which it holds until the batch is marked processed. A second instance skips the claimed rows instead of dispatching them again. Each message's handlers run in their own DI scope, so a failing handler can't break the claim's transaction or the next message. Delivery stays at-least-once: a crash before commit leaves the rows for the next pass.
- **Singleton jobs:** `ISingletonJobLock` (BuildingBlocks `Jobs`) runs a pass under a Postgres session advisory lock named after the job (`pg_try_advisory_lock`). If another instance holds it, the pass is skipped until the next tick. It covers `WeekReleaseJob`, `NotificationCleanupJob`, `EmailDispatcher`, `AccountDeletionJob` and the upload sweepers. A dropped connection releases the lock, and every job is already safe to repeat.
- **SignalR:** both hubs share the Redis backplane (channel prefix `gymhub-api`). Clients connect over WebSockets and skip the negotiate request, so a connection never depends on landing on the instance that negotiated it, and no sticky sessions are needed.
- **Revoking board access:** `BoardConnections` only knows its own instance's connections. `RevokeBoardAccessHandler` therefore publishes the revocation to every instance through `IInstanceMessages` (Redis pub/sub, or in-process without Redis), and each instance removes its own connections.
- **Gateway:** sessions use Aspire's `AddRedisDistributedCache`, and Data Protection keys are persisted to Redis (`RedisXmlRepository`) under one application name (`SharedState`). That shares the sign-in, session and antiforgery cookies across instances.
- **Per-instance caches, accepted:** suspension and "not deleted" answers are cached for a minute per instance (`SuspendedAccounts`, `DeletedAccounts`), so another instance catches up within a minute.

## As built
- Everything above.
- **Tests:**
  - `MultiInstanceApiTests` runs two hosts on one database. One claims an outbox batch while its handler is held, and the other finds nothing. Each message is delivered once, and a job lock is held by one instance at a time.
  - `RedisBackplaneTests` adds a Testcontainers Redis. A post through one instance reaches a hub client on the other, and leaving a program through one instance takes the member off the board on the other. Without Redis, both tests fail.
- **Checked by hand** in the AppHost with two API and two Gateway replicas (the `WithReplicas(2)` was temporary):
  - Both APIs report Healthy, which now includes the Redis check.
  - Both API instances are on the backplane (two server ids per hub), and both are subscribed to the revocation channel.
  - The Gateway key ring is in Redis, and one browser sign-in is recognised by both Gateway replicas, including token forwarding to the API.
  - Six raw hub WebSockets all heard a new thread.
- **Gateway to API traffic:** YARP sends the WebSockets to the API over HTTP/2, so each Gateway multiplexes on a single connection to one API replica. Spreading load across API replicas therefore depends on the platform's load balancing (milestone 6).
- **Redis TLS:** Aspire 13 runs the Redis container with TLS. The connection string it injects handles that.
- **Not done here:** Azure Cache for Redis, Key Vault protection of the key ring, and replica counts all belong to milestone 6.

## After review
- **Gateway Redis:** its own Redis (`gateway-redis`), never shared with the API. Session values are encrypted with Data Protection, and outside Development the key ring must be protected with a certificate (`Gateway:DataProtection:Certificate`), or the Gateway refuses to start.
- **Revocations:** subscribing retries in the background (the API starts without Redis), and each instance re-checks the board memberships it holds every five minutes, which catches a missed message.
- **Job lock:** taken on a module DbContext's connection, so it uses the same data source as EF (credentials, TLS).
- **Outbox:** the claim is a short transaction that leases the batch (`NextAttemptAt` five minutes ahead); handlers run with no transaction open, and each message's result is saved on its own.
- **Hub transports:** a direct WebSocket first; if that's blocked, a negotiated connection (SSE or long polling, which needs session affinity with several API instances).
