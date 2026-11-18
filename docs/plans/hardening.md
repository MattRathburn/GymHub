# Milestone 3: Security and production hardening

Gets the Gateway and API ready to run outside a developer machine. Nothing here changes product behaviour for members.

## Decisions
- **Uploads:** images are JPEG, PNG, WebP or GIF up to 10 MB (covers, program media, avatars); program video is MP4 or WebM up to 500 MB. SVG is refused, since it can carry scripts. Upload requests declare the size, which the presigned URL signs, so storage refuses anything else. The server checks the stored object's real size and type before attaching it, and deletes a failing new upload (never an already-attached one). Uploads nothing references are swept after a day.
- **SPA build:** `dotnet publish` of the Gateway runs `npm ci` and the Angular build and ships `dist` as `wwwroot`. Aspire keeps publishing the Gateway as a normal .NET project; the build machine needs Node. (The roadmap's Dockerfile was the alternative.)
- **CSP in Development:** sent as `Content-Security-Policy-Report-Only`, so violations show in the console without breaking the dev server. Other environments enforce it.

## Design
- **Gateway serves the SPA:** when `wwwroot/index.html` exists (a published build), the Gateway serves static files and falls back to `index.html` for client routes. `index.html` is never cached; hashed assets are cached for a year. The YARP `spa` route to `ng serve` comes from the AppHost, which adds the npm app and that route only in run mode.
- **Removed from the Gateway:** the `authRoutes` (`/api/**`) and `keycloak` (`/auth/**`) proxy routes, which exposed Keycloak (admin console included) through the site origin and are unused, and the debug endpoints `/userinfo` and `/gatewaystatus`.
- **Configuration:**
  - The API reads the realm from `Keycloak:Realm` (also the default for `KeycloakAdmin:Realm`).
  - `RequireHttpsMetadata` is on outside Development, in the API and the Gateway.
  - The Gateway answers only `Gateway:AllowedHosts` (`localhost`; production sets its domain), except the health probes, which arrive with the container IP as Host.
  - Forwarded headers (`X-Forwarded-For`/`Proto`) are trusted only from loopback and the networks in `ForwardedHeaders:KnownNetworks` (CIDR list), in place of the AppHost's trust-everyone `ASPNETCORE_FORWARDEDHEADERS_ENABLED`. Outside Development the API and Gateway refuse to start without them (or `ForwardedHeaders:NoProxy`).
- **Security headers (Gateway, every response):** CSP, HSTS (outside Development), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `X-Frame-Options: DENY` with `frame-ancestors 'none'`, and a `Permissions-Policy` turning off camera, microphone and geolocation. The CSP allows `'self'` plus the media storage origins in `Gateway:Csp:MediaOrigins` for images, video and uploads. Scripts and styles use a per-request nonce, which the Gateway writes into `index.html` (Angular already has `ngCspNonce` placeholders there and puts the nonce on the styles it injects), so neither needs `'unsafe-inline'`.
- **Cookies:** the auth and session cookies are `Secure` and `HttpOnly`; the XSRF cookie (read by the SPA) is `Secure` and `SameSite=Strict`.
- **Rate limiting:**
  - API: a per-minute limit per signed-in user (`sub`), and a lower one per client IP for anonymous calls, configurable under `RateLimiting`. 429 with `Retry-After`. The Stripe webhook and health endpoints aren't limited.
  - Gateway: `/login`, `/signup` and `/logout` per client IP.
- **Health:** `/health` (every check) and `/alive` (liveness only) are mapped in all environments and return only the status. They answer direct calls only: a request that came through a proxy (the Gateway forwarding `/gh-api/health`, or the ingress) gets 404. Checks: Postgres (Aspire's per-DbContext checks), Keycloak (the realm's discovery document) and storage (the bucket) on the API; Keycloak on the Gateway.
- **Startup:** the Gateway retries loading Keycloak's discovery document (about two minutes) instead of failing on the first try.
- **API docs:** OpenAPI and Scalar are mapped only in Development.

## As built
- Everything above. 7 functional tests (`HardeningApiTests`): the upload allowlist, extensions from the type, oversized or retyped objects refused and deleted, video size and cover rules, and the anonymous rate limit with the Stripe webhook exempt.
- The dev-server route lives in the AppHost (run mode), not in `appsettings.Development.json` (which is git-ignored, so a fresh clone wouldn't have had it). A published Gateway never proxies to `ng serve`.
- Outside Development the Gateway refuses to start with an `http://` authority instead of failing every request.
- Checked by hand: the published Gateway serves `index.html` with the nonce filled in and no caching, assets with a year's cache, client routes via the fallback and 404 for missing files; no CSP violations in the browser on the production build; `/login` gives 429 after 20 calls a minute; a wrong `Host` gets 400; `/health` reports Healthy under the AppHost; an avatar upload against RustFS passes the stored-object check.
- `AttachMediaRequest.ContentType` is still accepted but ignored: the stored object's type is used.
- `GH.GatewayTests` (no Docker) covers the security headers and nonce, the built SPA (fallback, caching, `/index.html`), host rules and probes, health behind a proxy, the XSRF cookie and check, sign-in's redirect and rate limit, and local return URLs. Attach accepts only keys of the shape the upload endpoint issues.
- Not done here: the production `Gateway:AllowedHosts`, `ForwardedHeaders:KnownNetworks` and `Gateway:Csp:MediaOrigins` values belong to the Azure deployment (milestone 6).
