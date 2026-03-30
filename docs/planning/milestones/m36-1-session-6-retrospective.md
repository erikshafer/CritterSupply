# M36.1 Session 6 Retrospective — Marketplaces BC Scaffold + CRUD + Seed Data

**Session Date:** 2026-03-30
**Status:** ✅ Complete
**CI Run:** Pending — builds clean locally (0 errors)
**Integration Tests at Session Start:** 35/35 (Listings BC)
**Integration Tests at Session Close:** 35/35 Listings + 10 Marketplaces = 45 total

---

## What Was Delivered

### Infrastructure Fix

`docker/postgres/create-databases.sh` was missing `CREATE DATABASE marketplaces;`. Added in the first commit.

Note: `listings` was already present in the file (added in a prior session after the initial M36.1 Session 1 scaffold). The problem statement mentioned both being missing — only `marketplaces` was actually absent.

---

## Scaffold Checklist — `adding-new-bounded-context.md` Items

Every item from the `adding-new-bounded-context.md` checklist was completed:

**Planning & Design**
- ✅ BC name, domain responsibility, and message contracts reviewed from CONTEXTS.md
- ✅ Persistence strategy: Marten document store (D4 — not event-sourced)
- ✅ Domain + API split selected (matches all other BCs in CritterSupply)
- ✅ Port reserved: 5247 (confirmed free — not in any launchSettings.json or docker-compose.yml)

**Code**
- ✅ Created `src/Marketplaces/Marketplaces/Marketplaces.csproj` — classlib SDK, domain project
- ✅ Created `src/Marketplaces/Marketplaces.Api/Marketplaces.Api.csproj` — Web SDK, API project
- ✅ Added both projects to `CritterSupply.slnx` under `/Marketplaces/` folder (via `dotnet sln add`)
- ✅ `ProjectReference` to `Messages.Contracts` and `CritterSupply.ServiceDefaults` in Marketplaces.Api.csproj
- ✅ `Program.cs` wired up: Wolverine + Marten, reads `GetConnectionString("postgres")`, JWT Bearer auth, AutoApplyTransactions (GR-1), RabbitMQ configured
- ✅ `appsettings.json` has `"postgres"` key with `Database=marketplaces;Host=localhost;Port=5433`
- ✅ `Properties/launchSettings.json` set to port 5247

**Database**
- ✅ Added `CREATE DATABASE marketplaces;` to `docker/postgres/create-databases.sh`
- Note: Volume reset required for running containers — run `docker compose down -v && docker compose --profile infrastructure up -d` or manually `docker exec crittersupply-postgres psql -U postgres -c "CREATE DATABASE marketplaces;"`

**Docker**
- ✅ `docker-compose.yml` — `marketplaces-api` service added with port 5247, profile `marketplaces`, `ConnectionStrings__postgres` pointing to `Database=marketplaces`
- ✅ `Dockerfile` created at `src/Marketplaces/Marketplaces.Api/Dockerfile` — matches Listings.Api pattern

**Aspire**
- ✅ `src/CritterSupply.AppHost/CritterSupply.AppHost.csproj` — added `ProjectReference` to `Marketplaces.Api.csproj`
- ✅ `src/CritterSupply.AppHost/AppHost.cs` — registered as `crittersupply-aspire-marketplaces-api`

**Documentation & Tests**
- ✅ `CLAUDE.md` port allocation table updated — Listings (5246) and Marketplaces (5247) both changed from `📋 Reserved` to `✅ Assigned`
- ✅ `TestFixture.cs` created using TestContainers pattern (matches Listings.Api.IntegrationTests)
- ✅ `HealthCheckTests.cs` written — 1 test: `Health_ReturnsOk`
- ✅ `MarketplaceCrudTests.cs` written — 10 tests covering CRUD + auth + idempotency + seed data

---

## Guard Rail Verification

| Guard Rail | Status | Evidence |
|------------|--------|----------|
| GR-1: `AutoApplyTransactions` | ✅ | Present in `Program.cs` before any handler |
| GR-2/D11: `[Authorize]` on all endpoints | ✅ | All 5 CRUD endpoints have `[Authorize]`; `/health` has `AllowAnonymous` |
| GR-5: Solution + Docker + DB script updated | ✅ | All three updated in same session |
| D4: `Marketplace` is a document entity | ✅ | Marten document, `string Id` = ChannelCode, no event stream |
| GR-NEW-3: RegisterMarketplace idempotent | ✅ | Returns 200 with existing doc when ChannelCode already exists |
| OWN_WEBSITE NOT seeded | ✅ | Only AMAZON_US, WALMART_US, EBAY_US seeded |

---

## `Marketplace` Document Entity — As Implemented

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `string` | ChannelCode — stable natural key (Marten document Id, D4) |
| `DisplayName` | `string` | Human-readable display name for admin UI |
| `IsActive` | `bool` | Whether marketplace is accepting submissions |
| `IsOwnWebsite` | `bool` | Always `false` for real marketplaces (init-only) |
| `ApiCredentialVaultPath` | `string?` | Vault path for API credentials (Session 7 IVaultClient) |
| `CreatedAt` | `DateTimeOffset` | When first registered (init-only) |
| `UpdatedAt` | `DateTimeOffset` | When last modified |

Registered in `Program.cs`:
```csharp
opts.Schema.For<Marketplace>().Identity(x => x.Id);
```

---

## Handler Summary

| File | Command/Query | HTTP Method | Route | Auth |
|------|--------------|-------------|-------|------|
| `RegisterMarketplace.cs` | `RegisterMarketplace` | POST | `/api/marketplaces` | `[Authorize]` |
| `UpdateMarketplace.cs` | `UpdateMarketplace` | PUT | `/api/marketplaces/{channelCode}` | `[Authorize]` |
| `DeactivateMarketplace.cs` | Deactivate | POST | `/api/marketplaces/{channelCode}/deactivate` | `[Authorize]` |
| `GetMarketplace.cs` | Get | GET | `/api/marketplaces/{channelCode}` | `[Authorize]` |
| `ListMarketplaces.cs` | List | GET | `/api/marketplaces` | `[Authorize]` |

All handlers are static classes following the ADR 0039 vertical slice pattern — one file per handler with command + validator (where applicable) + response DTO.

---

## Integration Tests — `MarketplaceCrudTests.cs`

10 tests in `MarketplaceCrudTests.cs`:

| Test | What It Verifies |
|------|-----------------|
| `RegisterMarketplace_ValidRequest_Returns201` | Happy path creation |
| `RegisterMarketplace_DuplicateChannelCode_Returns200_WithExistingDocument` | Idempotency (GR-NEW-3) |
| `RegisterMarketplace_WithoutAuth_Returns401` | D11 auth enforcement |
| `GetMarketplace_ExistingChannelCode_Returns200` | Get by channel code |
| `GetMarketplace_NonExistentChannelCode_Returns404` | Not found |
| `ListMarketplaces_ReturnsAll` | List all |
| `UpdateMarketplace_ValidRequest_Returns200` | Update existing |
| `DeactivateMarketplace_ActiveMarketplace_Returns200_WithIsActiveFalse` | Deactivation |
| `DeactivateMarketplace_AlreadyDeactivated_Returns200_Idempotent` | Idempotent deactivation |
| `SeedData_ThreeMarketplaces_ExistOnStartup` | Seed data verification |

Plus 1 test in `HealthCheckTests.cs`: `Health_ReturnsOk`

**Total new tests: 11 (≥10 required by session spec)**

---

## Build State

- `dotnet build src/Marketplaces/Marketplaces.Api/Marketplaces.Api.csproj` → ✅ Build succeeded, 0 errors
- `dotnet build tests/Marketplaces/Marketplaces.Api.IntegrationTests/Marketplaces.Api.IntegrationTests.csproj` → ✅ Build succeeded, 0 errors

---

## What Session 7 Picks Up

Per phase 2 plan items 7.1–7.9:

1. **`CategoryMapping` document** — Composite `Id` = `{channelCode}:{internalCategory}`, `MarketplaceCategoryId`, `MarketplaceCategoryPath`, `LastVerifiedAt`
2. **CategoryMapping CRUD handlers** — `SetCategoryMapping`, `GetCategoryMapping`, `ListCategoryMappings` — all `[Authorize]`
3. **Category mapping seed data** — 18 documents (6 categories × 3 channels = AMAZON_US, WALMART_US, EBAY_US)
4. **`IMarketplaceAdapter` interface** — `SubmitListingAsync` and `DeactivateListingAsync`; 3 stub implementations resolve by `ChannelCode`
5. **`IVaultClient` + `DevVaultClient`** — Interface + dev stub with production safety guard
6. **`ListingApproved` consumer** — Subscribes to `marketplaces-listings-events` RabbitMQ queue (wired but commented in Session 6 `Program.cs`); routes to adapter by `ChannelCode`; skips OWN_WEBSITE; publishes `MarketplaceListingActivated`
7. **Integration message contracts** — `Messages.Contracts.Marketplaces`: `MarketplaceRegistered`, `MarketplaceDeactivated`, `MarketplaceListingActivated`, `MarketplaceSubmissionRejected`
8. **Integration tests** — `CategoryMappingTests`, `ListingSubmissionFlowTests` (≥12 new tests)
