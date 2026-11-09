# Deploying Hyundai Stock on Cloudflare Containers

The dashboard and mobile API run in a Cloudflare Container, fronted by a small
Worker, so the public site no longer depends on the office PC being on. Every
push to `main` runs the tests and redeploys automatically through GitHub Actions.

```
browser / mobile app ──HTTPS──▶ Worker "hyundai-stock" ──▶ Container (ASP.NET Core 8)
                                                             │
                                                             └──TLS──▶ SQL Server (Azure SQL)
```

| Piece | Where |
|---|---|
| `Dockerfile`, `.dockerignore` | Image for `src/HyundaiStock.Web` (Kestrel on loopback, `socat` on :8080 once the app is ready) |
| `deploy/container/entrypoint.sh` | Starts the app, waits for it, then exposes port 8080 |
| `deploy/cloudflare/` | Worker (`src/index.ts`) + `wrangler.jsonc` |
| `.github/workflows/deploy-cloudflare.yml` | Test → build → deploy on push to `main` |

## Requirements and cost

| Item | Why | Cost |
|---|---|---|
| Cloudflare **Workers Paid** plan | Containers are not available on the Free plan | $5 / month (includes 25 GiB-h memory, 375 vCPU-min, 200 GB-h disk) |
| Container `basic` (¼ vCPU, 1 GiB, 4 GB disk) | Billed only while awake; kept warm 06:00–22:00 Baghdad, sleeps after `SLEEP_AFTER` (2 h) idle | ~$3–4 / month above the included usage; ~$7 / month if it never sleeps |
| SQL Server database | Container disk is wiped on every restart, so data must live outside it | **Azure SQL Basic (5 DTU, 2 GB): ~$4.90 / month**, or Azure SQL free offer: $0 |

The app already supports SQL Server (EF Core migrations run automatically on start),
so no application code change is needed for the database.

### Database choice

* **Recommended: Azure SQL Database, Basic DTU tier (~$4.90/month).** Always on,
  fixed price, 2 GB is far more than the current data (~1.3 MB).
* **$0 option: Azure SQL Database free offer** (100,000 vCore-seconds + 32 GB per
  month). It auto-pauses when idle; the first request after a pause can take up to a
  minute, and with "auto-pause when the free amount is used up" the database is
  offline until the next month. Fine for a pilot, risky for daily stocktaking.
* SQLite inside the container is **not** an option: the disk is ephemeral.
  PostgreSQL (Neon/Supabase) would need a new EF provider and migrations.

Azure SQL firewall: Cloudflare Containers have no fixed outbound IP addresses, so the
server needs a firewall rule that allows all IPv4 addresses (`0.0.0.0` – `255.255.255.255`).
Protection then relies on TLS (`Encrypt=True`) and a strong password: create a
dedicated SQL login for the app instead of using the server admin.

## One-time setup

1. **Upgrade** the Cloudflare account to Workers Paid
   (Dashboard → Workers & Pages → Plans).
2. **Create the database** (Azure portal → SQL databases → Create; Basic tier or
   "Apply free offer"). Copy the ADO.NET connection string:
   `Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<db>;User ID=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;`
3. **Create a Cloudflare API token** (My Profile → API Tokens → Create Token →
   template "Edit Cloudflare Workers"), limited to this account. It needs Workers
   Scripts edit (create/deploy the `hyundai-stock` Worker, including its container
   and Durable Object). Add **Zone → Workers Routes → Edit** for `cloudapps.trade`
   only when you attach the custom domain.
4. **Add GitHub repository secrets** (Settings → Secrets and variables → Actions):

   | Secret | Value |
   |---|---|
   | `CLOUDFLARE_API_TOKEN` | token from step 3 |
   | `CLOUDFLARE_ACCOUNT_ID` | Cloudflare account ID |
   | `HYUNDAI_DB_CONNECTION_STRING` | connection string from step 2 |
   | `HYUNDAI_JWT_KEY` | ≥ 32 random bytes (e.g. `openssl rand -hex 32`). Keep it stable: changing it logs every phone out |
   | `HYUNDAI_SEED_ADMIN_NAME` / `_PHONE` / `_PASSWORD` | Main admin created on an empty database (phone `07[5-8]XXXXXXXX`, password ≥ 6) |
   | `HYUNDAI_FCM_SERVER_KEY` | optional, push notifications |

5. Merge the PR (or run the workflow manually). The first deploy takes a few
   minutes while Cloudflare provisions the image. The site is then live at
   `https://hyundai-stock.cloudapps-trade.workers.dev`.

If the Cloudflare/DB secrets are missing, the deploy job is skipped with a warning
instead of failing.

## Moving existing data

The PC database is SQLite (`App_Data/hyundai-stock.db`, ~1.3 MB on 2026-11-09:
1 admin, 1 app user, 1 branch, 1 warehouse, 1 session with 6,701 parts and 206
expected VINs, 3 recount requests, 12 change logs). Two options:

* **Start clean (simplest):** let the seed create the main admin, recreate the
  branch / warehouse / employee in the dashboard and re-import the session Excel.
* **Copy the data:** a one-off SQLite → SQL Server copy (table by table, keeping
  IDs with `IDENTITY_INSERT`) after the cloud database has been migrated once.
  Stop counting on the PC during the copy.

Uploaded import files (`App_Data/uploads`) are not needed to run the session.

## Cut-over: same hostname, no app rebuild

The released mobile app is built with `API_BASE_URL=https://hyundai.cloudapps.trade`
and calls `/api/v1/...` over HTTPS. The container serves exactly the same app, so
paths, request/response JSON and HTTPS stay identical. Only the Worker behind the
hostname changes; DNS, the TLS certificate and the app build are untouched.

`hyundai.cloudapps.trade` is currently a Worker custom domain on
`hyundai-admin-relay` (relay to the PC). `api-hyundai.cloudapps.trade`
(`hyundai-api`) is not touched.

**Before the cut-over**

1. Deploy (merge to `main`) and test on
   `https://hyundai-stock.cloudapps-trade.workers.dev`: dashboard login, and with a
   debug build or `curl`, `POST /api/v1/auth/login` → `GET /api/v1/me` →
   `GET /api/v1/sessions/current`.
2. Decide the data path (see "Moving existing data"), and stop counting on the PC
   while the data is copied.
3. **JWT key:** phones keep their stored token across the cut-over.
   * Data copied with the same IDs → set `HYUNDAI_JWT_KEY` to the PC's current
     `Jwt:Key`; phones stay signed in.
   * Fresh database → use a **new** key. Old tokens then get 401 and the app shows
     its login screen (users sign in once; no rebuild). Never reuse the old key
     with a fresh database: a token's user ID could point at a different account.

**Switch (one API call, seconds, no DNS change)**

```bash
curl -X PUT "https://api.cloudflare.com/client/v4/accounts/$CLOUDFLARE_ACCOUNT_ID/workers/domains" \
  -H "Authorization: Bearer $CLOUDFLARE_API_TOKEN" -H "Content-Type: application/json" \
  --data '{"hostname":"hyundai.cloudapps.trade","service":"hyundai-stock","zone_name":"cloudapps.trade"}'
```

This re-points the existing custom domain from `hyundai-admin-relay` to
`hyundai-stock` (the dashboard's Worker → Settings → Domains & Routes does the same).
Then uncomment the `routes` line in `wrangler.jsonc` and push, so later deploys keep
the domain (the deploy token then needs **Zone → Workers Routes → Edit** on
`cloudapps.trade`).

**Check right after:** `https://hyundai.cloudapps.trade/Account/Login`, a login and
a count from a phone with the installed app.

**Rollback (seconds):** the same call with `"service":"hyundai-admin-relay"`; the PC
relay works as before (the PC backend must be running).

**Cold starts and the app's 30 s timeout:** the app gives up after 30 s. A cron
trigger pings the container every 20 minutes from 06:00 to 22:00 Baghdad time, so it
stays warm during the working day. If an API call still arrives during a cold
start, the Worker answers within ~15 s with HTTP 503 and the app's usual JSON shape
(`{"success":false,"message":"الخادم قيد التشغيل، حاول بعد لحظات"}`) instead of
hanging; a retry succeeds once the container is up.

## Known limitations (follow-up work, not blocking)

* **Dashboard sign-out on restart:** ASP.NET data-protection keys live on the
  container disk, so each cold start (after `SLEEP_AFTER` idle, or a deploy) signs
  dashboard users out. Mobile JWT logins are unaffected. Fix: persist keys in the
  database (`PersistKeysToDbContext`), or raise `SLEEP_AFTER`.
* **Uploaded import files** are kept on the container disk only; the
  "download original file" link for a session stops working after a restart.
  Fix: store uploads in R2 or the database.
* **Login rate limit** (10/min) is keyed on the client IP, which is always the
  loopback forwarder (same as with the current relay). Fix: key on
  `CF-Connecting-IP`.
* Cold start (container + .NET + migrations) takes roughly 5–30 s outside the
  keep-warm window; the Worker shows a short "starting" page (503 JSON for `/api`).

## Local verification

```bash
docker build -t hyundai-stock .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__Default='Server=...;Encrypt=True;TrustServerCertificate=True' \
  -e Jwt__Key=$(openssl rand -hex 32) \
  -e Seed__MainAdminPhone=07700000999 -e Seed__MainAdminPassword='change-me' \
  hyundai-stock
cd deploy/cloudflare && npm ci && npx wrangler deploy --dry-run
```
