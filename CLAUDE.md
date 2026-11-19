# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Hyundai Iraq stock-count system: an Arabic (RTL) ASP.NET Core 8 MVC dashboard plus a REST API (`/api/v1`) for the field mobile app. `README.md` (Arabic + English) holds the business rules, roles, Excel session-file format and the mobile API table. Read it before changing workflow logic.

## Commands

```powershell
# Local dev (SQLite). First time: copy the example config and set Jwt:Key (>= 32 bytes) and Seed:* values
Copy-Item src/HyundaiStock.Web/appsettings.Development.example.json src/HyundaiStock.Web/appsettings.Development.json
dotnet run --project src/HyundaiStock.Web          # http://localhost:5169, Swagger at /swagger

dotnet build HyundaiStock.sln -c Release
dotnet test HyundaiStock.sln -c Release
dotnet test tests/HyundaiStock.Tests --filter "FullyQualifiedName~WorkflowTests"           # one class
dotnet test tests/HyundaiStock.Tests --filter "FullyQualifiedName~WorkflowTests.<Method>"  # one test

# EF migration (SQL Server), then regenerate the manual SQL script
dotnet ef migrations add <Name> --project src/HyundaiStock.Infrastructure --startup-project src/HyundaiStock.Web --output-dir Data/Migrations
dotnet ef migrations script --project src/HyundaiStock.Infrastructure --startup-project src/HyundaiStock.Web -o db/initial-sqlserver.sql
```

`tests/smoke.py` (HTTP smoke, about 150 checks) creates and deletes data. Run it only against a separate local server with an empty test DB, never real data or Demo seed (see README for env vars). `Seed__Demo=true` loads demo data, Development only.

## Architecture

Three projects: `Domain` (entities and enums in one `Entities.cs`), `Infrastructure` (EF Core `AppDbContext`, all business logic in `Services/`, Excel via ClosedXML), and `Web` (MVC controllers, Razor views, mobile API). Controllers stay thin and call services. Services return `OpResult(Ok, Error, Id)`, and dashboard controllers turn it into a flash message with `Done(...)` (`Web/Infrastructure/BaseController.cs`). Related controllers share a file (`InventoryControllers.cs`, `OrgControllers.cs`, `WorkflowControllers.cs`); related services do too (`InventoryServices.cs`, `OrgServices.cs`, `PeopleServices.cs`).

**Two databases, two schema paths.** Production is SQL Server 2014 (compat level 120: no JSON/OPENJSON) using EF migrations applied on startup. Local dev and **all tests** use SQLite through `EnsureCreated`, which never alters an existing DB. So `Data/SqliteSchema.cs` adds new tables and columns by hand. **Every schema change needs both an EF migration and a matching `SqliteSchema.EnsureCurrentAsync` update**, or existing local DBs break. Some recent migrations are hand-written, without a `.Designer.cs`. Keep `AppDbContextModelSnapshot.cs` consistent.

**Two auth schemes.** Dashboard uses cookie auth (`DashboardController` base: cookie scheme + antiforgery). Mobile API uses JWT bearer (`Api/MobileApiController.cs`, `Api/ApiInfra.cs`). JWT `OnTokenValidated` rejects disabled users on every request. API responses always use the `ApiResponse` envelope `{ success, message, data, errors }`, including errors, 404 and 500 (see `Program.cs`).

**Roles and data scope.** `AdminRole` (Domain): MainAdmin 0, SuperAdmin 1, Admin 2, WarehouseManager, SparePartsManager, Accountant, AccountsManager, VehicleWarehouseManager. `AdminScopeFilter` reloads the admin from the DB on each dashboard request into an `AdminScope` (role, branch, assigned branches/warehouses, `AllBranches`). Controllers read it via `Scope`. Services take `AdminScope` and filter through `Scoped(s)` queries, so branch and warehouse visibility is enforced in services, not views. Use `[Roles(...)]` for action access and `Menu.*` (in `AdminContext.cs`) for navigation visibility. Field users (mobile) are `AppUser`, separate from `Admin`.

**Stocktaking sessions.** Data is per session. A session is annual (`SessionKind.Annual`, one per year) or monthly (`SessionKind.Monthly`, one per year and month), has a display `Name` («جرد سنوي 2026», «جرد شباط 2026»), and at most one session is in progress at a time. Monthly works exactly like annual (parts, vehicles, variances to finance). `SessionLookup` resolves current, latest or a chosen session (`ForSessionAsync`); `ForYearAsync` only serves older `year=` export links. Every list page has a session filter (`sessionId`, partial `_SessionFilter`), and "export by filter" must export exactly the visible session, search and view. The mobile app needs no change: it counts into the open session, and `sessions/current` returns its name.

**Count and approval workflows** (core business logic, covered by `WorkflowTests`, `RecountListTests`, `QuantityRemovalTests`):
- Every count saves directly, above or below the system quantity. The variance shows on the finance page (`FinanceController`) as surplus/shortage. Quantity-correction requests were removed from the dashboard and the API; the `QuantityCorrections` table stays for history only.
- A counted row is counted once. Another user gets 409 "counted by employee <name>" and never sees the counted quantity (`MobileService.Project` masks it). The user who counted it may count it again directly. Admin-assigned recount tasks (`RecountRequest`) still work; `RecountAsk` lets a counter ask for one.
- `AppUser.CountType` (Parts / Vehicles / Both) limits what a mobile user may count. `me` exposes it and `MobileService` / `NewPartRequestService` return 403 otherwise.
- Missing-part requests (`NewPartRequestService`): two steps. The SparePartsManager approves (`ApproveAsync`, PendingSparePartsManager -> PendingAccountant), then the Accountant or AccountsManager approves (`FinalApproveAsync`), which adds the part and lists it on «القطع الجديدة» (`/ApprovedParts`). Each step's reviewer may return it to the employee (`CanAct`).
- Duplicate part numbers in one warehouse: only users with `CanPickDuplicateRow` choose the row. Otherwise the first uncounted row is used.
- `IStockNotifier` sends FCM push notifications. It is optional in service constructors (`= null`) so tests can omit it.

**UI.** Arabic RTL Razor views with Hyundai styling (`wwwroot/css/design.css`, `app.css`). Helpers in `Web/Helpers` (`Ui`, `FormUi`, `IconSet`, `PagerVm`). Shared partials: `_Search`, `_Pager`, `_YearFilter`, `_ConfirmModal`, `_Flash`. User-facing strings are Iraqi-dialect Arabic.

## Tests

xUnit in `tests/HyundaiStock.Tests`. Service tests (`WorkflowTests` etc.) build services directly on a temp SQLite file. `ApiTests` uses `ApiFactory : WebApplicationFactory<Program>`, which forces SQLite and test JWT/seed settings in memory. Reuse these fixtures rather than adding new hosting setups.

## Config and deploy

`appsettings.json` holds no secrets. The app refuses to start without `ConnectionStrings:Default` and `Jwt:Key` (>= 32 bytes). Production comes from env vars (`Database__Provider=SqlServer`, `Jwt__Key`, `Seed__*`, `Storage__UploadsPath`). `appsettings.Development.json` is git-ignored.

A push to `main` runs `.github/workflows/deploy-cloudflare.yml`: tests, Worker type-check (`deploy/cloudflare`), Docker build, then deploy to Cloudflare Containers backed by Azure SQL (`docs/CLOUDFLARE-DEPLOY.md`). Kestrel binds loopback only, and `socat` exposes :8080. Only `X-Forwarded-Proto` is trusted (`docs/LIVE-READINESS.md`). Don't widen forwarded-header trust.
