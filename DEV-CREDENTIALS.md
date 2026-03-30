# Dev Credentials

> These accounts are seeded automatically when running in **Development** mode.
> All passwords: **`Dev@123!`**
>
> ⚠️ Never use these accounts or passwords in production.

---

## Backoffice (http://localhost:5244)

| Email | Name | Role | Notes |
|-------|------|------|-------|
| `admin@crittersupply.dev` | Alex Admin | SystemAdmin | Full access — can manage users, roles, and all operations |
| `exec@crittersupply.dev` | Eve Executive | Executive | KPI dashboards and strategic reports |
| `ops@crittersupply.dev` | Oscar Ops | OperationsManager | Operations oversight, alerts, fulfillment |
| `cs@crittersupply.dev` | Clara Service | CustomerService | Customer and order lookup |
| `warehouse@crittersupply.dev` | Walt Warehouse | WarehouseClerk | Inventory management and stock alerts |
| `pricing@crittersupply.dev` | Priya Pricing | PricingManager | Pricing administration |
| `copy@crittersupply.dev` | Connor Copy | CopyWriter | Content editing only |

## Vendor Portal (http://localhost:5241)

### HearthHound Nutrition Co. (Active — default happy-path vendor)

| Email | Name | Role | Notes |
|-------|------|------|-------|
| `mkerr@hearthhound.com` | Melissa Kerr | Admin | Full vendor access — team management, catalog, dashboard |
| `jpike@hearthhound.com` | Jordan Pike | CatalogManager | Product and listing management |
| `esuarez@hearthhound.com` | Elena Suarez | ReadOnly | View-only access to dashboard and catalog |

### TumblePaw Play Labs (Onboarding — invitation edge case)

| Email | Name | Role | Notes |
|-------|------|------|-------|
| `asha@tumblepaw.com` | Asha Bell | Admin | Only active user — tests onboarding workflow |
| `connor@tumblepaw.com` | Connor Reeves | CatalogManager | **Invited** (not yet activated) |
| `mina@tumblepaw.com` | Mina Albright | ReadOnly | **Invited** (not yet activated) |

---

## Quick Start

```bash
# 1. Start infrastructure (Postgres, RabbitMQ, Jaeger)
docker-compose --profile infrastructure up -d

# 2. Start identity APIs (seed data runs automatically on startup)
dotnet run --project "src/Backoffice Identity/BackofficeIdentity.Api/BackofficeIdentity.Api.csproj"
dotnet run --project "src/Vendor Identity/VendorIdentity.Api/VendorIdentity.Api.csproj"

# 3. Start the frontends
dotnet run --project "src/Backoffice/Backoffice.Web/Backoffice.Web.csproj"
dotnet run --project "src/Vendor Portal/VendorPortal.Web/VendorPortal.Web.csproj"

# 4. Log in
#    Backoffice: http://localhost:5244  →  admin@crittersupply.dev / Dev@123!
#    Vendor Portal: http://localhost:5241  →  mkerr@hearthhound.com / Dev@123!
```

## E2E Test Users (separate from dev seed)

E2E tests seed their own ephemeral users during test runs. These are **not** the same
as the dev seed accounts above — they use different GUIDs, different emails, and
different passwords. See:

- `tests/Backoffice/Backoffice.E2ETests/WellKnownTestData.cs`
- `tests/Vendor Portal/VendorPortal.E2ETests/WellKnownVendorTestData.cs`

The two-tier system ensures dev seed data and E2E test data never collide.

## How Seed Data Works

Seed data is controlled by the `ASPNETCORE_ENVIRONMENT` variable:

- **Development** — Seed data runs on startup (idempotent — skips if data already exists)
- **Production / Test** — Seed data is never executed

Each identity API checks for existing records before inserting:
```csharp
if (await dbContext.Users.AnyAsync()) return; // Already seeded
```

To re-seed after a database wipe, simply restart the API in Development mode.
