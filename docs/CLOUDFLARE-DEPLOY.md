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
| Container `basic` (¼ vCPU, 1 GiB, 4 GB disk) | Billed only while awake; sleeps after `SLEEP_AFTER` (default 2 h) idle | ~$0–3 / month for office-hours use; ~$7 / month if it never sleeps |
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

## Switching `hyundai.cloudapps.trade`

`hyundai.cloudapps.trade` is currently a Worker custom domain on
`hyundai-admin-relay` (relay to the PC). After the workers.dev URL is verified:

1. Dashboard → Workers & Pages → `hyundai-admin-relay` → Settings → Domains &
   Routes → remove `hyundai.cloudapps.trade`.
2. In `deploy/cloudflare/wrangler.jsonc`, uncomment the `routes` line, push to
   `main` (token needs Zone → Workers Routes → Edit).
3. Check `https://hyundai.cloudapps.trade/Account/Login` and the mobile app.
   The app's API base URL stays the same.

**Rollback:** remove the custom domain from `hyundai-stock` and add it back to
`hyundai-admin-relay`; the PC relay works as before.

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
* Cold start (container + .NET + migrations) takes roughly 5–30 s; the Worker shows
  a short "starting" page (503 + `Retry-After` for `/api`).

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
