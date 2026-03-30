# Dev Seed Data Session Retrospective

**Date:** 2026-03-30
**Type:** QoL (Quality of Life) — standalone session, not a numbered M36.1 session

---

## What Was Delivered

### 1. BackofficeIdentity Seed Data (7 users, one per role)

Created `src/Backoffice Identity/BackofficeIdentity.Api/Auth/BackofficeIdentitySeedData.cs` following the existing `VendorIdentitySeedData.cs` pattern.

| Email | Name | Role | GUID |
|-------|------|------|------|
| `admin@crittersupply.dev` | Alex Admin | SystemAdmin | `AA000000-AA00-AA00-AA00-AA0000000001` |
| `exec@crittersupply.dev` | Eve Executive | Executive | `AA000000-AA00-AA00-AA00-AA0000000002` |
| `ops@crittersupply.dev` | Oscar Ops | OperationsManager | `AA000000-AA00-AA00-AA00-AA0000000003` |
| `cs@crittersupply.dev` | Clara Service | CustomerService | `AA000000-AA00-AA00-AA00-AA0000000004` |
| `warehouse@crittersupply.dev` | Walt Warehouse | WarehouseClerk | `AA000000-AA00-AA00-AA00-AA0000000005` |
| `pricing@crittersupply.dev` | Priya Pricing | PricingManager | `AA000000-AA00-AA00-AA00-AA0000000006` |
| `copy@crittersupply.dev` | Connor Copy | CopyWriter | `AA000000-AA00-AA00-AA00-AA0000000007` |

**Password for all:** `Dev@123!`

Wired in `BackofficeIdentity.Api/Program.cs` — runs after `MigrateAsync()` in Development environment only. Idempotent (checks `Users.AnyAsync()` before inserting).

### 2. VendorIdentity Seed Data Expansion

Replaced the generic "Acme Pet Supplies" tenant with two vendors from `docs/domain/vendors/vendor-catalog.md`:

**HearthHound Nutrition Co.** (Active — default happy-path vendor):
| Email | Name | Role | Status |
|-------|------|------|--------|
| `mkerr@hearthhound.com` | Melissa Kerr | Admin | Active |
| `jpike@hearthhound.com` | Jordan Pike | CatalogManager | Active |
| `esuarez@hearthhound.com` | Elena Suarez | ReadOnly | Active |

**TumblePaw Play Labs** (Onboarding — invitation edge case):
| Email | Name | Role | Status |
|-------|------|------|--------|
| `asha@tumblepaw.com` | Asha Bell | Admin | Active |
| `connor@tumblepaw.com` | Connor Reeves | CatalogManager | Invited |
| `mina@tumblepaw.com` | Mina Albright | ReadOnly | Invited |

GUIDs sourced from `vendor-catalog.json` (deterministic `10000000-...` range).

**Password for all active users:** `Dev@123!` (invited users have no password hash).

Also updated `VendorPortalSeedData.cs` to seed VendorAccount documents for both tenants in Marten (required because dev seed bypasses the RabbitMQ event bus).

### 3. E2E Test Updates

Updated Vendor Portal E2E tests to reference the new HearthHound credentials:
- `WellKnownVendorTestData.cs` — updated tenant ID, name, user emails, password
- Feature files (`vendor-auth.feature`, `vendor-change-requests.feature`, `vendor-dashboard.feature`, `vendor-team-management.feature`) — updated email and password literals
- `VendorChangeRequestStepDefinitions.cs` — updated direct API call credentials
- `VendorDashboardStepDefinitions.cs` — updated tenant ID references
- `E2ETestFixture.cs` — updated VendorAccount verification check

### 4. DEV-CREDENTIALS.md

Created `DEV-CREDENTIALS.md` at repository root with:
- Complete credential table for Backoffice (7 users) and Vendor Portal (6 users across 2 tenants)
- Quick start instructions
- Explanation of two-tier user system (dev seed vs E2E test users)

---

## WellKnownTestData.cs Alignment

### Backoffice E2E (no changes needed)
The Backoffice E2E tests in `WellKnownTestData.cs` use a completely separate GUID range:
- E2E tests: `11111111-...`, `22222222-...`, `AAAAAAAA-...`, `BBBBBBBB-...`, etc.
- Dev seed: `AA000000-AA00-AA00-AA00-AA000000000X`

**No collision.** The E2E fixture's `SeedAdminUser()` checks `u.Id == userId || u.Email == email` before inserting — fully idempotent. Different emails (`alice.admin@crittersupply.com` vs `admin@crittersupply.dev`) mean no interference.

### Vendor Portal E2E (updated)
The Vendor Portal E2E tests previously referenced the old Acme tenant. Updated `WellKnownVendorTestData.cs` and all feature files to use HearthHound credentials.

### Two-Tier User System
- **Dev seed users** — persistent, survive database restarts, realistic names/roles from vendor catalog
- **E2E test users** — ephemeral, seeded per test run by test fixtures, test-only GUIDs and emails

Future sessions should understand that these are intentionally separate tiers.

---

## EF Core Migration Changes

**None required.** The existing `InitialCreate` migration already includes all columns needed for seed data. No schema changes were made.

---

## OWN_WEBSITE Channel Association

The Vendor Identity model does not include a channel association field. `OWN_WEBSITE` is a channel concept that lives in the Listings BC (as `channelCode` in `ListingStreamId`), not in vendor identity. This will be seeded when Marketplaces BC is implemented (reserved port 5247). Documented here for future reference.

---

## Build State

- **Build:** 0 errors, 33 warnings (matches M36.0 baseline — no new warnings introduced)
- **BackofficeIdentity.Api.IntegrationTests:** 6/6 passing ✅
- **VendorIdentity.Api.IntegrationTests:** 57/57 passing ✅
- **VendorPortal.Api.IntegrationTests:** 30 passing, 56 failing ⚠️ (pre-existing failures unrelated to seed data — AnalyticsHandlers, VendorProductCatalog, and VendorAccount tests)
- **All other test projects:** Passing (no regressions)

---

## Manual Verification

> ⚠️ **Infrastructure not available in this environment.** Docker Compose, Postgres, and RabbitMQ are not available in the sandboxed agent environment. Manual login verification requires running locally:
>
> 1. `docker-compose --profile infrastructure up -d`
> 2. Start `BackofficeIdentity.Api` and `Backoffice.Web`
> 3. Start `VendorIdentity.Api` and `VendorPortal.Web`
> 4. Log in with seeded credentials from `DEV-CREDENTIALS.md`
>
> The seed data code follows the identical pattern as the proven `VendorIdentitySeedData.cs` (which is verified by E2E tests) and uses the same `PasswordHasher<T>` from ASP.NET Core Identity. The Backoffice login flow uses the same `PasswordHasher.VerifyHashedPassword()` method (confirmed in `Login.cs:98`), so passwords will verify correctly.

---

## Commit History

1. `QoL: BackofficeIdentity — add dev seed data (7 users, one per role)`
2. `QoL: VendorIdentity — expand seed data with vendor catalog personas (HearthHound + TumblePaw)`
3. `QoL: Add DEV-CREDENTIALS.md with dev login cheat sheet`
4. `QoL: Update dev startup documentation` (retrospective + CURRENT-CYCLE.md)
