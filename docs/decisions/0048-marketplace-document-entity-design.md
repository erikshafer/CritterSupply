# ADR 0048: Marketplace Document Entity Design

**Status:** ✅ Accepted

**Date:** 2026-03-31

**Context:**

The Marketplaces bounded context was introduced to manage external marketplace channel integrations (Amazon US, Walmart US, eBay US). A core design question was how to persist the `Marketplace` entity — as an event-sourced aggregate (consistent with Product Catalog and Orders) or as a Marten document (consistent with simpler configuration-level data).

This ADR records the persistence and modelling decisions made for the `Marketplace` document entity so future sessions understand why the Marketplaces BC uses a fundamentally different persistence pattern than other bounded contexts in CritterSupply.

**Decisions:**

### 1. Marten Document Store (Not Event Sourcing)

The `Marketplace` entity is persisted as a Marten document using `IDocumentSession.Store()` and lightweight sessions, not as an event-sourced aggregate. The Marten configuration explicitly registers it as a document:

```csharp
opts.Schema.For<Marketplace>().Identity(x => x.Id);
```

**Rationale:** Marketplace registrations are configuration-level data, not a domain aggregate requiring audit history. State changes are infrequent and operator-driven (e.g., an admin activating a new channel or rotating API credentials). The business logic does not require replaying an event stream to derive current state — a single document read with optimistic concurrency is sufficient. Event sourcing would add persistence complexity (stream management, projections, Apply methods) with no corresponding business benefit.

### 2. `Id` Property as Natural Key (Channel Code)

The `Marketplace.Id` property stores the channel code string (e.g., `"AMAZON_US"`, `"WALMART_US"`, `"EBAY_US"`) and serves as both the Marten document identity and the business identifier. There is no surrogate `Guid` key.

```csharp
public sealed class Marketplace
{
    public string Id { get; init; } = default!;
    // ...
}
```

**Rationale:** Channel codes are stable, human-readable, and globally unique by convention. Using the channel code directly as the document ID enables simple lookups (`session.LoadAsync<Marketplace>("AMAZON_US")`) without an intermediate projection or mapping layer. This is the same pattern used by `CategoryMapping`, which uses a composite `"{ChannelCode}:{InternalCategory}"` string as its document ID.

### 3. Sealed Class with Mutable Operational Fields

The `Marketplace` entity is a `sealed class` (not a record) with a mix of `init`-only identity fields and mutable operational fields:

```csharp
public sealed class Marketplace
{
    public string Id { get; init; } = default!;
    public string DisplayName { get; set; } = default!;
    public bool IsActive { get; set; }
    public bool IsOwnWebsite { get; init; } = false;
    public string? ApiCredentialVaultPath { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

**Rationale:** Identity fields (`Id`, `IsOwnWebsite`, `CreatedAt`) are set once at creation and are `init`-only. Operational fields (`DisplayName`, `IsActive`, `ApiCredentialVaultPath`, `UpdatedAt`) are mutable because marketplace configuration changes in place. A fully immutable record would require Marten to delete-and-reinsert on every update, which is unnecessary overhead for a document that is updated infrequently. The `sealed` modifier prevents inheritance, consistent with CritterSupply's coding standards.

### 4. OWN_WEBSITE Excluded from Marketplaces BC

The `OWN_WEBSITE` channel is **not** seeded as a `Marketplace` document and is not managed by the Marketplaces BC. Instead, it is the Listings BC's internal fast-path channel. The `ListingApprovedHandler` explicitly short-circuits for it:

```csharp
if (string.Equals(message.ChannelCode, "OWN_WEBSITE", StringComparison.OrdinalIgnoreCase))
    return outgoing;
```

**Rationale:** OWN_WEBSITE represents CritterSupply's own storefront — it has no external API, no adapter, no credentials, and no category mapping requirements. Modelling it as a `Marketplace` document would create a degenerate entity that violates every invariant the document is designed to enforce. The Listings BC handles OWN_WEBSITE as a fast-path: listings approved for the own website are immediately active without marketplace adapter involvement. This was a deliberate Product Owner decision to keep bounded context responsibilities clean.

### 5. Idempotent Seed Data (Three Canonical Marketplaces)

Development and test environments seed three canonical marketplace documents (`AMAZON_US`, `WALMART_US`, `EBAY_US`) via `MarketplacesSeedData.SeedAsync()`. The seed method is idempotent — it checks `session.Query<Marketplace>().AnyAsync()` before inserting.

**Rationale:** Seed data enables immediate development and testing without manual setup. The idempotency guard prevents duplicate document errors on application restart. The same seed method is callable from both `Program.cs` startup and integration test fixtures, ensuring consistent state across environments.

**Consequences:**

- The Marketplaces BC has a simpler persistence model than event-sourced BCs (Product Catalog, Orders), with no projections, Apply methods, or stream management
- Marketplace state changes are not auditable via event history — if audit requirements emerge, a migration to event sourcing would be needed
- The natural key pattern (`Id` = channel code) eliminates the projection-based lookup overhead seen in Product Catalog's SKU-to-stream mapping
- The `ListingApprovedHandler` OWN_WEBSITE guard must be maintained if new handlers consume `ListingApproved` — it is not enforced at the document level
- Category mappings follow the same document-store pattern with a composite string key, keeping the entire BC consistent

**References:**
- `src/Marketplaces/Marketplaces/Marketplaces/Marketplace.cs` — document entity
- `src/Marketplaces/Marketplaces.Api/Program.cs` — Marten configuration and seed data invocation
- `src/Marketplaces/Marketplaces.Api/MarketplacesSeedData.cs` — idempotent seed data
- `src/Marketplaces/Marketplaces.Api/Listings/ListingApprovedHandler.cs` — OWN_WEBSITE short-circuit
- `src/Marketplaces/Marketplaces/CategoryMappings/CategoryMapping.cs` — companion document entity
