# M36.1 Phase 2 Plan — Marketplaces BC Foundation

**Status:** 📋 Planning Complete
**Scope:** Phase 2a (scaffold + CRUD + seed data + stub adapter) + partial Phase 2b (stub consumer)
**Estimated Sessions:** 3 (Sessions 6–8)
**Prerequisite:** Phase 1 gate ✅ MET (Session 4, 35/35 tests, 0 errors)
**Author:** @PSA + @PO (Session 5 planning discussion)

---

## 1. Goal Statement

**Phase 2a value:** A Backoffice operator can register marketplace channels, configure
category mappings, and view marketplace readiness status — establishing the
infrastructure that listing submission will flow through.

**Phase 2b value:** When a listing is approved for a non-OWN_WEBSITE channel, the
Marketplaces BC consumes the approval event, routes it through the appropriate adapter
(stub), and publishes a confirmation or rejection back to the Listings BC.

**Architectural value:** The Marketplaces BC introduces CritterSupply's first
**bidirectional async messaging integration** — a Listings BC publisher becomes a
consumer, and vice versa. This validates the inbox/outbox pattern for round-trip
message flows and establishes the adapter strategy pattern for future real marketplace
API integrations.

---

## 2. Scope Boundary

### In Scope (M36.1 Phase 2)

- **Marketplaces.Api project scaffold** — port 5247, database `marketplaces`, Docker
  Compose `marketplaces` profile
- **Marketplace document entity** — `ChannelCode` as stable `string Id`,
  Marten document store (D4: not event-sourced)
- **Marketplace CRUD** — Register, Update, Deactivate, Get, List handlers
- **CategoryMapping document** — composite `Id` = `{channelCode}:{internalCategory}`,
  Set, Get, List handlers
- **Adapter stubs** — `IMarketplaceAdapter` interface with `SubmitListingAsync` and
  `DeactivateListingAsync`; 3 stub implementations (Amazon, Walmart, eBay)
- **Vault stub** — `IVaultClient` with `GetSecretAsync`; `DevVaultClient` reads
  from `appsettings.Development.json`
- **Seed data** — 3 marketplace documents (AMAZON_US, WALMART_US, EBAY_US) + 18
  category mappings (6 categories × 3 marketplaces)
- **`ListingApproved` consumer** — Marketplaces.Api consumes the already-published
  integration message, routes to adapter by `ChannelCode`, publishes
  `MarketplaceListingActivated` back
- **Integration message contracts** — `Messages.Contracts.Marketplaces`:
  `MarketplaceRegistered`, `MarketplaceDeactivated`, `MarketplaceListingActivated`,
  `MarketplaceSubmissionRejected`
- **Marketplace admin list page** — `/admin/marketplaces` in Backoffice.Web
  (simple table, read-only)
- **Integration tests** — MarketplaceCrudTests, CategoryMappingTests,
  ListingSubmissionFlowTests, SeedDataTests
- **ADRs** — 0048 (Marketplace document entity), 0049 (Category mapping ownership)
- **Solution, Docker Compose, DB script updates**

### Explicitly Out of Scope (defer to M37.0+)

- **Listings BC consuming `MarketplaceListingActivated` / `MarketplaceSubmissionRejected`**
  — Listings aggregate state transitions (Submitted → Live / Draft) are Phase 2b
  completion, deferred to M37.0
- **`AttributeSchemaPublished` event + `RequiresSchemaReview` flagging** (D10) —
  Schema versioning deferred to late Phase 2b or Phase 3
- **Submission timeout safety net** — Wolverine scheduled message for orphaned
  Submitted listings deferred to M37.0
- **Full marketplace admin UI** — 4-tab detail page, create wizard, category mapping
  editor deferred to M37.0
- **Real marketplace API integrations** — Phase 3 (M38.x)
- **`OWN_WEBSITE` in Marketplaces BC** — OWN_WEBSITE is not an external marketplace;
  it is handled by the Listings BC fast-path (Draft → Live)

---

## 3. Phase 2 Reconciliation Summary

### Execution Plan vs Current State

| Execution Plan Says | Current Reality | Impact |
|---------------------|-----------------|--------|
| Build `Backoffice.Web` + `AdminPortal.Api` | Both exist (`Backoffice.Web` port 5244, `Backoffice.Api` port 5243) | No new UI scaffold needed; marketplace pages are additions to existing Backoffice.Web |
| Port 5247 for Marketplaces.Api | Not allocated in `docker-compose.yml` or any `launchSettings.json` | Clean slot available |
| Database `marketplaces` | Not in `docker/postgres/create-databases.sh` | Must add `CREATE DATABASE marketplaces;` |
| ADRs 0035–0036 for Phase 2 | Numbers occupied by Backoffice ADRs | Phase 2 ADRs: **0048** (marketplace document entity), **0049** (category mapping ownership) |
| Seed OWN_WEBSITE in Marketplaces BC | PO decision: **No** — OWN_WEBSITE is not an external marketplace | Seed only AMAZON_US, WALMART_US, EBAY_US |

### `ListingApproved` Consumer — Already Published, Waiting

```csharp
// src/Shared/Messages.Contracts/Listings/ListingIntegrationMessages.cs
public sealed record ListingApproved(
    Guid ListingId, string Sku, string ChannelCode, DateTimeOffset OccurredAt);
```

Published from `ApproveListing.cs` handler in Session 3. **No consumer exists yet.**
The Marketplaces BC consuming this message is Phase 1's natural completion.

---

## 4. Session Sequence

### Session 6: Marketplaces.Api Scaffold + Marketplace Document CRUD + Seed Data

**Owner:** @PSA (scaffold) + @QAE (integration tests)

**Deliverables:**

| # | Item | Acceptance Criteria |
|---|------|---------------------|
| 6.1 | Project scaffold — `Marketplaces/Marketplaces.csproj` + `Marketplaces.Api/Marketplaces.Api.csproj` | Both projects compile; added to `CritterSupply.slnx` |
| 6.2 | Solution + infrastructure updates | `docker/postgres/create-databases.sh` includes `marketplaces`; `docker-compose.yml` has `marketplaces-api` service with profile `marketplaces`; `CLAUDE.md` port table updated |
| 6.3 | `Program.cs` wiring | Marten config (`marketplaces` schema), `AutoApplyTransactions`, `DurableLocalQueues`, `DurableOutbox`, JWT Bearer auth, health check, RabbitMQ |
| 6.4 | `Marketplace` document entity | `ChannelCode` as `string Id`, `DisplayName`, `IsActive`, `IsOwnWebsite: false`, `ApiCredentialVaultPath`, `CreatedAt`, `UpdatedAt` |
| 6.5 | Marketplace CRUD handlers | `RegisterMarketplace`, `UpdateMarketplace`, `DeactivateMarketplace`, `GetMarketplace`, `ListMarketplaces` — all with `[Authorize]` |
| 6.6 | Seed data — 3 marketplace documents | AMAZON_US, WALMART_US, EBAY_US seeded on startup (Development environment only) |
| 6.7 | Integration tests | `MarketplaceCrudTests`: register, update, deactivate, get, list, seed verification; minimum 10 tests |
| 6.8 | `launchSettings.json` | Port 5247 |
| 6.9 | `appsettings.json` | Connection string for `localhost:5433/marketplaces` |

**Guard rails enforced:** GR-1 (`AutoApplyTransactions`), GR-2/D11 (`[Authorize]`), GR-5 (solution + Docker + DB updates)

### Session 7: Category Mappings + Adapter Stubs + ListingApproved Consumer

**Owner:** @PSA (handlers + adapters) + @QAE (integration tests)

**Deliverables:**

| # | Item | Acceptance Criteria |
|---|------|---------------------|
| 7.1 | `CategoryMapping` document | Composite `Id` = `{channelCode}:{internalCategory}`, `MarketplaceCategoryId`, `MarketplaceCategoryPath`, `LastVerifiedAt` |
| 7.2 | CategoryMapping CRUD handlers | `SetCategoryMapping`, `GetCategoryMapping`, `ListCategoryMappings` — all with `[Authorize]` |
| 7.3 | Seed data — 18 category mappings | 6 categories (Dogs, Cats, Birds, Reptiles, Fish & Aquatics, Small Animals) × 3 marketplaces |
| 7.4 | `IMarketplaceAdapter` interface | `SubmitListingAsync(ListingSubmission)` and `DeactivateListingAsync(string listingId)` |
| 7.5 | 3 stub adapters | `StubAmazonAdapter`, `StubWalmartAdapter`, `StubEbayAdapter` — return success after simulated delay; resolve by `ChannelCode` via DI |
| 7.6 | `IVaultClient` + `DevVaultClient` | Interface with `GetSecretAsync(string path)`; dev stub reads from config; production startup guard |
| 7.7 | `ListingApproved` consumer | Marketplaces.Api subscribes to `ListingApproved` via RabbitMQ; routes to adapter by `ChannelCode` (skips OWN_WEBSITE); adapter calls stub; publishes `MarketplaceListingActivated` |
| 7.8 | Integration message contracts | `Messages.Contracts.Marketplaces`: `MarketplaceRegistered`, `MarketplaceDeactivated`, `MarketplaceListingActivated`, `MarketplaceSubmissionRejected` |
| 7.9 | Integration tests | `CategoryMappingTests`, `ListingSubmissionFlowTests` (end-to-end: ListingApproved → adapter → MarketplaceListingActivated); minimum 12 tests |

**Guard rails enforced:** D3 (Marketplaces owns API calls), D5 (category mapping ownership), D6 (Vault for credentials)

### Session 8: Marketplace Admin UI + E2E + ADRs + Phase 2 Gate

**Owner:** @FPE (admin UI) + @QAE (E2E) + @PSA (ADRs)

**Deliverables:**

| # | Item | Acceptance Criteria |
|---|------|---------------------|
| 8.1 | `MarketplacesApi` named HttpClient in Backoffice.Web | Base address `http://localhost:5247`, configured in `Program.cs` |
| 8.2 | CORS configuration in Marketplaces.Api | `BackofficePolicy` allowing Backoffice.Web origin (same pattern as Listings.Api) |
| 8.3 | Marketplace admin list page | `/admin/marketplaces` with table showing ChannelCode, DisplayName, IsActive, CreatedAt |
| 8.4 | E2E page objects + feature files | `MarketplacesAdminPage.cs`, `MarketplacesAdmin.feature` (2 executable + 2 @wip scenarios) |
| 8.5 | E2E step definitions | Step definitions for executable scenarios |
| 8.6 | ADR 0048 | Marketplace Identity as Document Entity (D4 rationale) |
| 8.7 | ADR 0049 | Category Mapping Ownership in Marketplaces BC (D5 rationale) |
| 8.8 | Phase 2 gate verification | All gate criteria below checked and documented |
| 8.9 | Session 8 retrospective + CURRENT-CYCLE.md update | Standard session bookend |

---

## 5. Guard Rails

All M36.1 guard rails carry forward. Phase 2 additions:

| Guard Rail | Requirement | Source |
|------------|-------------|--------|
| **GR-1** | `opts.Policies.AutoApplyTransactions()` from first commit | M36.0 lesson |
| **GR-2 / D11** | `[Authorize]` on every endpoint from first commit | M36.0 lesson |
| **GR-5** | Solution file + Docker Compose + DB script updated in scaffold commit | Adding-new-BC checklist |
| **D3** | Marketplaces BC owns marketplace API calls | Clean BC boundary |
| **D4** | `Marketplace` is a document entity, not event-sourced | Infrequent config changes |
| **D5** | Category mappings owned by Marketplaces BC | Ownership follows change rate |
| **D6** | `IVaultClient` for credentials; `DevVaultClient` stub in development only | No secrets in Postgres |
| **GR-NEW-1** | Deactivated marketplace rejects submissions | Business invariant |
| **GR-NEW-2** | Missing category mapping rejects submission with clear error | Fail-fast, don't silently skip |
| **GR-NEW-3** | Marketplace registration is idempotent by `ChannelCode` | Natural key, upsert semantics |

---

## 6. Infrastructure Assignments

| Resource | Value | Notes |
|----------|-------|-------|
| **Port** | `5247` | Next available per CLAUDE.md table |
| **Database** | `marketplaces` | Add to `docker/postgres/create-databases.sh` |
| **Docker Compose profile** | `marketplaces` | Add `marketplaces-api` service block |
| **Marten schema** | `marketplaces` | `opts.DatabaseSchemaName = "marketplaces"` |
| **RabbitMQ queues** | `marketplaces-listings-events` | For consuming `ListingApproved` |
| **Connection string key** | `postgres` | Standard `ConnectionStrings:postgres` |

---

## 7. Seed Data Specification

### Marketplace Documents (3)

| ChannelCode | DisplayName | IsActive | ApiCredentialVaultPath |
|-------------|-------------|----------|------------------------|
| `AMAZON_US` | Amazon US | `true` | `marketplace/amazon-us` |
| `WALMART_US` | Walmart US | `true` | `marketplace/walmart-us` |
| `EBAY_US` | eBay US | `true` | `marketplace/ebay-us` |

**Note:** `OWN_WEBSITE` is NOT seeded in Marketplaces BC — it is the Listings BC's
internal fast-path channel, not an external marketplace.

### Category Mappings (6 categories × 3 marketplaces = 18 documents)

| Internal Category | AMAZON_US ID | WALMART_US ID | EBAY_US ID |
|-------------------|-------------|---------------|------------|
| Dogs | `AMZN-PET-DOGS-001` | `WMT-PET-DOGS-001` | `EBAY-PET-DOGS-001` |
| Cats | `AMZN-PET-CATS-001` | `WMT-PET-CATS-001` | `EBAY-PET-CATS-001` |
| Birds | `AMZN-PET-BIRDS-001` | `WMT-PET-BIRDS-001` | `EBAY-PET-BIRDS-001` |
| Reptiles | `AMZN-PET-REPT-001` | `WMT-PET-REPT-001` | `EBAY-PET-REPT-001` |
| Fish & Aquatics | `AMZN-PET-FISH-001` | `WMT-PET-FISH-001` | `EBAY-PET-FISH-001` |
| Small Animals | `AMZN-PET-SMALL-001` | `WMT-PET-SMALL-001` | `EBAY-PET-SMALL-001` |

**CategoryMapping composite Id format:** `{channelCode}:{internalCategory}`
(e.g., `AMAZON_US:Dogs`)

---

## 8. ADR Schedule

| ADR # | Topic | Deliver By | Notes |
|-------|-------|-----------|-------|
| **0048** | Marketplace Identity as Document Entity | Session 6 | D4 rationale: `ChannelCode` as stable key, not event-sourced |
| **0049** | Category Mapping Ownership in Marketplaces BC | Session 7 | D5 rationale: ownership follows change rate |

**Note:** The execution plan's ADR numbers 0035–0036 for Phase 2 are stale — those
numbers were consumed by Backoffice ADRs. The M36.1 plan's allocation (0041–0046)
accounts for Phase 1. Phase 2 continues with 0048–0049 (0047 reserved for Listings
State Machine Transitions from Phase 1 ADR backlog).

---

## 9. Vault Stub Pattern (D6)

### Interface

```csharp
// src/Marketplaces/Marketplaces/Credentials/IVaultClient.cs
public interface IVaultClient
{
    Task<string> GetSecretAsync(string path, CancellationToken ct = default);
}
```

### Development Stub

```csharp
// src/Marketplaces/Marketplaces/Credentials/DevVaultClient.cs
public sealed class DevVaultClient(IConfiguration configuration) : IVaultClient
{
    public Task<string> GetSecretAsync(string path, CancellationToken ct = default)
    {
        var configKey = $"Vault:{path.Replace('/', ':')}";
        var value = configuration[configKey]
            ?? throw new KeyNotFoundException($"Dev vault secret not found: {path}");
        return Task.FromResult(value);
    }
}
```

### Production Safety Guard

```csharp
if (builder.Environment.IsProduction() &&
    builder.Services.Any(s => s.ImplementationType == typeof(DevVaultClient)))
    throw new InvalidOperationException(
        "DevVaultClient must not be used in Production. Configure a real IVaultClient.");
```

---

## 10. Phase 2 Gate (Definition of Done)

Adapted from execution plan Phase 2 Gate, scoped to M36.1 delivery:

- [ ] `Marketplace` documents exist for AMAZON_US, WALMART_US, EBAY_US (seed data)
- [ ] Marketplace CRUD works: register, update, deactivate, get, list
- [ ] Deactivated marketplace rejects new submissions (business invariant)
- [ ] `CategoryMapping` CRUD works for 18 seeded mappings (6 categories × 3 channels)
- [ ] Missing category mapping rejects submission with clear error
- [ ] `IMarketplaceAdapter` interface with 3 stub implementations resolve by `ChannelCode`
- [ ] `ListingApproved` consumed by Marketplaces.Api → routes to adapter → publishes
  `MarketplaceListingActivated`
- [ ] `IVaultClient` / `DevVaultClient` pattern implemented with production safety guard
- [ ] `[Authorize]` on all Marketplaces.Api endpoints
- [ ] `opts.Policies.AutoApplyTransactions()` configured
- [ ] Solution, Docker Compose (`marketplaces` profile), DB script updated
- [ ] Marketplace admin list page in Backoffice.Web
- [ ] Integration tests: CRUD + seed data + end-to-end stub submission flow (≥22 tests)
- [ ] E2E page objects + feature files for marketplace admin
- [ ] ADRs 0048 and 0049 written
- [ ] CI green (build + integration tests + E2E)

---

## 11. Risk Register

| Risk | Mitigation | Severity |
|------|-----------|----------|
| Volume reset required — `marketplaces` DB won't exist in running Postgres containers | Document `docker compose down -v && docker compose --profile infrastructure up -d` in Session 6 notes | LOW |
| `ListingApproved` consumer race — Marketplaces.Api must be running before approval flow triggers | Wolverine durable inbox/outbox handles this — messages queue in RabbitMQ until consumer is ready | LOW |
| Vault stub in dev config could leak to production | GR check: `DevVaultClient` startup guard throws in Production | MEDIUM |
| Bidirectional integration complexity — first round-trip message flow in CritterSupply | Start with stub adapters; defer real API integration to Phase 3 | MEDIUM |

---

## 12. What M37.0 Picks Up (Phase 2b Completion)

Items deferred from M36.1 Phase 2:

1. **Listings BC consuming `MarketplaceListingActivated`** → listing transitions to `Live`
2. **Listings BC consuming `MarketplaceSubmissionRejected`** → listing transitions to `Draft`
3. **`AttributeSchemaPublished` event** → flags draft listings with `RequiresSchemaReview` (D10)
4. **Submission timeout safety net** → Wolverine scheduled message for orphaned `Submitted` listings
5. **Full marketplace admin UI** — 4-tab detail page, create wizard, category mapping editor
6. **MarketplaceAttributeSchema** — versioned document for marketplace-specific attribute requirements
