# Production dependencies

What a published GymHub needs besides its own containers, and how to set each up. The AppHost wires these only when publishing (`docs/plans/production-dependencies.md`); a local `dotnet run` still uses Mailpit, RustFS and the test realm.

## Parameters

Every value comes from an Aspire parameter. Milestone 6 keeps the secret ones in Key Vault.

| Parameter | Secret | Value |
|---|---|---|
| `public-host` | | The site's host name, e.g. `gymhub.example`. Used for redirect URIs, links in emails, Stripe return URLs and the Gateway's allowed host. |
| `auth-host` | | The sign-in host name, e.g. `auth.gymhub.example`. It points at the Gateway (like `public-host`), which passes on only the realm's pages to Keycloak. |
| `bff-client-secret` | yes | A long random string, shared by Keycloak's `gymhub-bff` client and the Gateway. |
| `api-admin-client-secret` | yes | A long random string, shared by Keycloak's `gymhub-api-admin` service account and the API. |
| `keycloak-password` | yes | Keycloak's bootstrap admin password, for the `master` realm (Aspire generates one if unset). |
| `gymhub-password` | yes | The Postgres server admin's password. Only the API and the migration service use it. |
| `keycloak-db-password` | yes | A long random string: the password of Keycloak's own `keycloak` role, which owns only the `keycloak` database. |
| `dataprotection-certificate` | yes | A base64-encoded PFX whose key encrypts the Gateway's Data Protection key ring in Redis. The Gateway refuses to start without it. |
| `dataprotection-certificate-password` | yes | That PFX's password. |
| `smtp-host` / `smtp-port` / `smtp-username` | | Default to `smtp.sendgrid.net`, `587` and `apikey`. |
| `smtp-password` | yes | The SendGrid API key. |
| `email-from` | | The sender, e.g. `no-reply@gymhub.example`, on SendGrid's authenticated domain. |
| `media` | yes | The R2 connection string (below). |
| `media-origin` | | `https://<account-id>.r2.cloudflarestorage.com`, for the Gateway's CSP. |
| `stripe-secret-key` / `stripe-webhook-secret` | yes | Milestone 7. |

The API and Gateway also need `ForwardedHeaders:KnownNetworks` (the ingress's network) in milestone 6. Aspire switches on ASP.NET's own forwarded-headers handling when publishing, which trusts every address. The AppHost turns it off, and both apps refuse to start if it's on.

## Email: SendGrid
1. Create a SendGrid account and authenticate the sending domain (the DNS records SendGrid gives you). Without that, mail lands in spam or is refused.
2. Create an API key with the Mail Send permission only, and set it as `smtp-password`.
3. Set `email-from` to an address on that domain.

Both GymHub's notifications and Keycloak's verification and password-reset emails go through it, over STARTTLS on port 587. GymHub refuses to start with an SMTP username but without TLS.

## Media: Cloudflare R2
1. Create a bucket, e.g. `gymhub-media`.
2. Create an R2 API token with Object Read & Write on that bucket only. Note its access key ID, its secret, and your account ID.
3. Set `media` to:
   ```
   Endpoint=https://<account-id>.r2.cloudflarestorage.com;AccessKey=<access-key-id>;SecretKey=<secret>;Bucket=gymhub-media;Region=auto
   ```
4. Set CORS on the bucket (Settings → CORS policy), because browsers upload and download directly with presigned URLs:
   ```json
   [
     {
       "AllowedOrigins": ["https://gymhub.example"],
       "AllowedMethods": ["GET", "PUT", "HEAD"],
       "AllowedHeaders": ["content-type"],
       "ExposeHeaders": ["ETag"],
       "MaxAgeSeconds": 3600
     }
   ]
   ```

At startup the API checks the bucket with HeadBucket and creates it only if it's missing. A token scoped to the bucket is therefore enough, since it can't list buckets.

## Keycloak
- **Image:** built from `src/GymHub.AppHost/Keycloak/Dockerfile`. It is Keycloak 26.5 with the `GymHub` realm (`Keycloak/realm/GymHub-realm.json`) in its import folder and Postgres as its database.
- **Startup:** it runs `start --import-realm` in production mode, http inside, with `KC_HOSTNAME=https://<auth-host>` and `KC_PROXY_HEADERS=xforwarded`.
- **No public endpoint:** browsers reach it through the Gateway, which answers on `auth-host` and passes on only `/realms/GymHub/**` and `/resources/**`. The API and the Gateway talk to it directly at `http://keycloak` on the environment's private network (the Gateway's `OpenIdConnect:MetadataAuthority`, the API's `Keycloak:RequireHttpsMetadata=false`); tokens are still checked against the https issuer.
- **Database:** `keycloak` on the same Postgres server, owned by a `keycloak` role that has no access to `gymhubdb`. The migration service creates both (and applies a changed `keycloak-db-password`) before Keycloak starts; the server admin's credentials never reach Keycloak.
- **Realm import:** only into an empty database. Later changes to the realm file don't touch an existing realm; make them in the admin console as well, or with `kcadm`.
- **The realm:** no users except the API's service account. It has `sslRequired: external`, email verification, brute-force protection (10 failures, then increasing waits), and a password policy (10+ characters, not the username or email). Sign-in may only return to `https://<public-host>/*`. `ProductionRealmTests` keeps it in step with the test realm's roles, clients and mappers.
- **Admin console:** not reachable from outside: the Gateway doesn't pass on `/admin`, the admin REST API or the `master` realm. Use it from inside the environment (for example a port-forward or `kcadm` in the container, below).

## The first admin
There are no seeded users. Once the site is up:

1. Sign up on the site with your own account and verify the email.
2. Open a shell in the Keycloak container (in Azure: `az containerapp exec --name keycloak --command bash`) and run:
   ```bash
   /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user admin --password '<keycloak-password>'
   /opt/keycloak/bin/kcadm.sh add-roles -r GymHub --uusername '<your username>' --rolename admin
   ```
3. Sign out of GymHub and back in, so your token carries the role.

Keycloak calls the bootstrap admin temporary. Afterwards, create a permanent admin in the `master` realm with its own password and two-factor sign-in, then delete the bootstrap `admin` user.
