# Polecat Candidate Bounded Contexts

**Date:** 2026-02-25  
**Status:** 🔬 Research / Exploratory  
**Author:** Principal Architect  
**Purpose:** Identify and evaluate which bounded context(s) in CritterSupply would be the best candidates to adopt Polecat—the upcoming SQL Server-backed event store from JasperFX—rather than (or in addition to) Marten.

---

## What Is Polecat?

Polecat is an upcoming event store from [JasperFX](https://github.com/JasperFx)—the same team behind Marten, Wolverine, and Alba. Key characteristics:

- **SQL Server** as the underlying database engine (vs. Marten's PostgreSQL)
- **~99% API parity** with Marten's event store, meaning handler code, aggregate patterns, and the `Apply()`/`Create()` decider model should port with minimal change
- **EF Core–powered projections**, which is the most significant architectural departure from Marten's own JSONB-based projection engine
- Inherits the lessons learned from years of Marten production usage
- Designed for teams in Microsoft/SQL Server shops who can't (or don't want to) run PostgreSQL

Because the event store API is nearly identical to Marten's, Wolverine will continue to be the handler and routing backbone. The main difference developers will feel is:

1. The persistence layer is SQL Server (not PostgreSQL)
2. Projections are defined using EF Core `DbContext` patterns (not Marten's inline/async projection API)

---

## Why This Matters for CritterSupply

CritterSupply is a **reference architecture**. Every technology and pattern choice is an opportunity to demonstrate something a developer learning the Critter Stack can take home and use. Introducing Polecat on a well-chosen bounded context would:

- Show that the Critter Stack is not locked to PostgreSQL
- Demonstrate how EF Core projections compare to Marten's native projections
- Prove that Polecat BCs can participate in the same RabbitMQ message bus alongside Marten-based BCs (because the integration contracts live in `Messages.Contracts`, not the DB)
- Highlight the SQL Server ecosystem (SSMS, Azure SQL, SQL Profiler, EF Core tooling)

---

## Key Selection Criteria

When evaluating a BC as a Polecat candidate, the following dimensions matter most:

| Criterion | Why It Matters |
|-----------|----------------|
| **Greenfield vs Migration** | Greenfield eliminates risk of breaking existing functionality |
| **Projection Complexity** | EF Core projections shine when queries are relational (joins, aggregations, filtering) — JSONB is less ergonomic for complex reads |
| **Event Lifecycle Richness** | Polecat needs to demonstrate the same event sourcing patterns as Marten to be a meaningful comparison |
| **Integration Isolation** | All BC integration is over RabbitMQ, so SQL Server vs PostgreSQL is transparent to other BCs |
| **Operational Overhead** | SQL Server adds a new container to docker-compose and requires SQL Server knowledge from contributors |
| **Pedagogical Contrast** | Ideally the candidate demonstrates something the Marten-based BCs don't already cover |

---

## Candidate Analysis

### Candidate 1: Returns BC 🔄 *(Planned, Greenfield)*

**Summary:** The Returns BC handles reverse logistics — return requests, eligibility validation, physical receipt, inspection, and disposition (refund or rejection). It is planned but not yet implemented.

#### Event Lifecycle (Rich & Well-Defined)

```
ReturnRequested
  └─> ReturnApproved / ReturnDenied
      └─> [Customer ships back]
          └─> ReturnReceived
              └─> ReturnInspecting
                  └─> ReturnCompleted (disposition: Restockable | NonRestockable)
                  └─> ReturnRejected
```

8+ states, clear domain events, and meaningful business rules (eligibility windows, restockable disposition). This is the kind of event-sourced aggregate where Polecat would shine identically to Marten.

#### Pros

- ✅ **Zero migration risk** — Greenfield means no impact on existing BCs
- ✅ **Rich event lifecycle** — 8+ states with genuine business logic (eligibility window, inspection, disposition)
- ✅ **Clear aggregate boundaries** — The `Return` aggregate is self-contained
- ✅ **EF Core projections add clear value** — "All open returns by customer," "Returns awaiting inspection this week," "Restockable items ready for Inventory" are all relational queries that benefit from LINQ + EF Core
- ✅ **Integrates over messages** — Publishes `ReturnApproved`, `ReturnCompleted`, `ReturnRejected` via RabbitMQ; Orders/Payments react independently. The DB choice is fully transparent to other BCs.
- ✅ **Demonstrates cross-database saga participation** — Orders already orchestrates the return status (`ReturnRequested` state). Showing that a Polecat BC can participate in a Marten-orchestrated saga is a powerful demonstration.

#### Cons

- ⚠️ **SQL Server adds a new container** — docker-compose must include a SQL Server image (heavier than PostgreSQL; `mcr.microsoft.com/mssql/server`)
- ⚠️ **Returns is downstream of Fulfillment (delivered) and upstream of Payments (refund)** — the cross-database testing setup requires more thought in integration tests
- ⚠️ **TestContainers.MsSql** exists but is less familiar to the team than `Testcontainers.PostgreSql`
- ⚠️ **SQL Server Developer Edition** is free for dev/testing, but SQL Server itself is a licensed product for production — this is a meaningful operational difference to document for readers

#### Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| SQL Server container breaks local dev | Low | Medium | Document docker-compose profile + `mcr.microsoft.com/mssql/server:2022-latest` |
| Polecat API gaps vs Marten | Medium (pre-release) | High | Spike on API surface before committing a cycle to this |
| Cross-database integration test complexity | Low | Low | Integration tests per BC don't span databases; all inter-BC flows via RabbitMQ |
| Contributor friction with SQL Server tooling | Medium | Low | README guidance, SSMS/Azure Data Studio links |

**Overall Risk: 🟢 LOW**

---

### Candidate 2: Vendor Portal BC 📊 *(Planned, Greenfield)*

**Summary:** The Vendor Portal provides tenant-isolated analytics and change request workflows for partnered vendors. It's projection-heavy by design, consuming events from Orders, Inventory, and Catalog to build aggregated read models.

#### Projection-Dominated Architecture

Already planned projections:
- `ProductPerformanceSummary` — sales metrics by SKU × time bucket (daily/weekly/monthly/quarterly/yearly)
- `InventorySnapshot` — current stock levels with historical movement
- `ChangeRequestStatus` — lifecycle tracking across all pending and resolved requests
- `SavedDashboardView` — vendor-configured filters

**This is the most compelling EF Core projection story in the system.** These projections aren't simple "current state" denormalizations — they require aggregations, time-bucketed metrics, and multi-tenant scoping. EF Core's LINQ provider and migration tooling are genuinely superior to Marten's JSONB projections for this class of query.

#### Pros

- ✅ **Zero migration risk** — Greenfield
- ✅ **Highest EF Core projection upside** — Multi-tenant analytics aggregation is exactly where EF Core projections outperform Marten JSONB document projections
- ✅ **Demonstrates multi-tenancy with Polecat** — `VendorTenantId` scoping maps naturally to whatever multi-tenancy support Polecat ships with (expected parity with Marten's multi-tenancy)
- ✅ **ChangeRequest aggregate is event-sourced** — `ChangeRequest` has a rich lifecycle (Submitted → Approved/Rejected), good use of Polecat's event store
- ✅ **High pedagogical contrast** — Shows when EF Core projections are superior to Marten's native projections, and explains why
- ✅ **Azure SQL alignment** — Vendor portals in enterprise settings frequently live in Azure SQL; this demonstrates that story

#### Cons

- ⚠️ **Dependency on Vendor Identity BC** — Vendor Portal needs `VendorTenantId` from Vendor Identity, which itself hasn't been built yet. Two sequential BCs are needed before a Polecat demo is complete.
- ⚠️ **Complex projection design upfront** — Time-bucketed sales metrics (`DailySales`, `WeeklySales`, etc.) require careful EF Core projection design. If Polecat's projection engine has limitations pre-release, this complexity could surface problems early.
- ⚠️ **SQL Server container + heavier operational surface**
- ⚠️ **Vendor Identity planned for EF Core (not event sourced)** — Two sequential BCs, only one of which uses Polecat's event store, may confuse readers about what each piece does

#### Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Polecat projection API incomplete for analytics use cases | Medium (pre-release) | High | Spike on projection API before designing full analytics model |
| Sequential dependency on Vendor Identity | Low | Medium | Can stub `VendorTenantId` in early phase |
| Multi-tenancy support gap in Polecat | Medium (pre-release) | High | API parity claim suggests this is covered; confirm in spike |
| Projection migration complexity (EF Core migrations on aggregation tables) | Low | Medium | Standard EF Core patterns apply |

**Overall Risk: 🟡 LOW-MEDIUM** (dependency chain + projection complexity)

---

### Candidate 3: Promotions & Pricing BC 🏷️ *(Not Yet Planned, Entirely Greenfield)*

**Summary:** A Promotions/Pricing BC would own discount rules, promotional campaigns, coupon codes, and time-limited offers. This BC doesn't exist in CONTEXTS.md yet — it would need to be designed from scratch.

#### Event Lifecycle (Hypothetical)

```
PromotionCreated
  └─> PromotionActivated / PromotionDeactivated
      └─> CouponBatchGenerated
          └─> CouponApplied (Shopping BC emits, Promotions listens)
              └─> CouponRedeemed / CouponExpired
```

Pricing rules are a natural event-sourced domain: every change to a discount rate or campaign window is meaningful audit data. "What was the price of DOG-BOWL-001 on Feb 14?" is a question that event sourcing answers definitively.

#### Pros

- ✅ **Maximum design freedom** — Not yet in CONTEXTS.md, so it can be co-designed with Polecat's capabilities in mind
- ✅ **Zero migration risk** — Entirely greenfield
- ✅ **Naturally event-sourced domain** — Price and discount changes benefit from full audit trail; event sourcing is the right tool
- ✅ **EF Core projections are ideal** — "All active promotions for category Dogs right now" is a relational query with filters on date range + category. EF Core LINQ is more ergonomic than Marten JSONB here.
- ✅ **High business value** — Every e-commerce platform needs promotions; readers can relate to this domain immediately
- ✅ **Interesting BC interactions** — Would integrate with Shopping (coupon application), Orders (price-at-checkout), and potentially Customer Identity (customer-specific offers)

#### Cons

- ⚠️ **Domain modeling is non-trivial** — Promotion stacking rules, exclusions, time windows, and priority resolution are genuinely complex
- ⚠️ **Design work required before implementation** — CONTEXTS.md needs to be written first; this adds a planning cycle
- ⚠️ **Competes for priority with Returns and Vendor Portal** — Both are already defined and queued; adding Promotions first may disrupt the planned roadmap
- ⚠️ **SQL Server infra overhead** same as all Polecat candidates

#### Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Domain modeling complexity stalls implementation | Medium | Medium | Spike on promotion rules domain model before committing |
| Roadmap disruption (jumping ahead of Returns/Vendor Portal) | Low | Medium | Frame as "bonus future BC" rather than next in sequence |
| Promotion-order integration contract needs careful design | Low | Low | Can be added to CONTEXTS.md as part of design spike |

**Overall Risk: 🟢 LOW** (but requires design investment upfront)

---

### Candidate 4: Notifications BC 🔔 *(Not Yet Planned, Greenfield)*

**Summary:** A Notifications BC would own customer-facing communications — order confirmation emails, shipping notifications, return status updates, and promotional messages. It reacts to events from other BCs.

#### Pros

- ✅ **Greenfield** — no migration risk
- ✅ **Clean event model** — `EmailSent`, `SMSSent`, `PushNotificationSent` + preference events
- ✅ **EF Core projections natural** — Notification history, throttle rules, opt-out preferences are relational data
- ✅ **Low domain complexity** — Good for a "first taste" of Polecat without overwhelming learners

#### Cons

- ⚠️ **Weak event sourcing story** — The audit trail of notification delivery isn't as compelling a domain as Returns, Vendor Portal, or Promotions
- ⚠️ **More infrastructure than domain** — Notification dispatch (via SendGrid, Mailchimp, etc.) is primarily integration, not rich business logic
- ⚠️ **Less pedagogical contrast** — Doesn't show something meaningfully different from what existing BCs already demonstrate

#### Risk Assessment: 🟢 LOW (but low reward)

---

### Candidate 5: Vendor Identity BC 🏢 *(Planned, EF Core — Polecat Addition)*

**Summary:** Vendor Identity is already planned with EF Core for traditional relational persistence (similar to Customer Identity). The question is whether to layer Polecat's event store on top to capture the vendor lifecycle.

#### Pros

- ✅ **EF Core already in the plan** — Projections wouldn't need a technology shift
- ✅ **Meaningful identity lifecycle events** — `VendorTenantCreated`, `VendorUserInvited`, `VendorUserActivated`, `VendorUserDeactivated` are worth capturing
- ✅ **Audit/compliance story** — Identity systems benefit from immutable event logs

#### Cons

- ⚠️ **Customer Identity already demonstrates "EF Core + Wolverine"** — Adding Polecat here doesn't add much contrast; it just moves from Postgres to SQL Server for the same kind of BC
- ⚠️ **Not purely event-sourced** — Vendor Identity state (current password hash, MFA status) is mutable relational data; event sourcing is additive rather than primary
- ⚠️ **Not as compelling for the reference architecture** — The learning value is lower compared to a genuinely projection-heavy or event-rich BC

#### Risk Assessment: 🟢 LOW (but low reward)

---

## Side-by-Side Comparison

| Candidate | Status | Risk | EF Core Projection Value | Event Richness | Pedagogical Contrast | Recommended? |
|-----------|--------|------|--------------------------|----------------|----------------------|--------------|
| **Returns BC** | Planned | 🟢 Low | Medium (return status queries) | High (8+ states) | High (core business flow) | ✅ Yes — Tier 1 |
| **Vendor Portal BC** | Planned | 🟡 Low-Medium | **Very High** (analytics aggregation) | Medium (ChangeRequest) | **Very High** (projections story) | ✅ Yes — Tier 1 |
| **Promotions/Pricing BC** | Not Planned | 🟢 Low | High (active promotions queries) | High (campaign lifecycle) | High (new domain) | ✅ Yes — Tier 2 |
| **Notifications BC** | Not Planned | 🟢 Low | Medium | Low | Low | ⚠️ Maybe — Low Priority |
| **Vendor Identity BC** | Planned | 🟢 Low | Low (EF Core already planned) | Low-Medium | Low | ❌ Skip |

---

## Cross-Cutting Infrastructure Considerations

Any Polecat BC will require the following additions to the project:

### 1. SQL Server Container

```yaml
# docker-compose.yml addition
sqlserver:
  image: mcr.microsoft.com/mssql/server:2022-latest
  environment:
    - ACCEPT_EULA=Y
    - SA_PASSWORD=YourStrong!Passw0rd
  ports:
    - "1433:1433"
  profiles: [infrastructure, polecat]
```

### 2. New TestContainers Setup

```csharp
// Testcontainers.MsSql (already available on NuGet)
var sqlServerContainer = new MsSqlBuilder()
    .WithPassword("YourStrong!Passw0rd")
    .Build();
```

### 3. EF Core SQL Server Provider

```xml
<!-- Directory.Packages.props additions -->
<PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="X.X.X" />
<PackageVersion Include="Testcontainers.MsSql" Version="X.X.X" />
```

### 4. Connection String Pattern

```json
{
  "ConnectionStrings": {
    "polecat": "Server=localhost,1433;Database=critter_supply_returns;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true"
  }
}
```

### 5. Port Allocation

SQL Server's default port (1433) is standard; no conflicts with existing BC ports (5231–5240).

---

## Recommended Path Forward

### Tier 1: Returns BC (Start Here)

**Why:** Clean greenfield domain, rich event lifecycle, participation in the existing saga, and a moderate enough complexity to be a successful Polecat "first implementation." It will surface any Polecat API gaps in a lower-stakes context before tackling the analytics complexity of Vendor Portal.

**Sequencing:**
1. Spike: Validate Polecat NuGet availability and basic API surface
2. Design `Return` aggregate in CONTEXTS.md using Polecat
3. Implement Returns BC with Polecat event store + EF Core projections
4. Add SQL Server to docker-compose under a `polecat` or `returns` profile
5. Write integration tests using `Testcontainers.MsSql`
6. Document the comparison in an ADR (Polecat vs Marten for Returns)

### Tier 1 (Parallel or Follow-on): Vendor Portal BC

**Why:** Once Polecat basics are proven with Returns, the Vendor Portal is where EF Core projections truly distinguish themselves from Marten's native projection engine. The analytics aggregation story is uniquely compelling for the Critter Stack reference architecture.

**Sequencing:**
1. Complete Vendor Identity BC (EF Core, planned)
2. Design Vendor Portal with Polecat event store for `ChangeRequest` aggregate
3. Implement EF Core projections for `ProductPerformanceSummary`, `InventorySnapshot`, etc.
4. Write ADR comparing Marten projections vs EF Core projections for analytics

### Tier 2 (Future Consideration): Promotions/Pricing BC

**Why:** Design it from scratch with Polecat's capabilities in mind — no migration, maximum pedagogical value, genuinely complex domain. Save this for when Polecat is stable and the Returns/Vendor Portal experience has informed the approach.

---

## What Polecat Would Need to Prove in CritterSupply

For the reference architecture to genuinely validate Polecat, these questions need answers:

1. **API Parity**: Does the `IEventStore` API feel identical enough that a Marten-trained developer is immediately productive?
2. **EF Core Projection Experience**: Is defining projections in EF Core `OnModelCreating` as ergonomic as Marten's `IProjection` interface?
3. **Multi-Tenancy**: Does Polecat support tenant-scoped event streams (needed for Vendor Portal)?
4. **Wolverine Integration**: Do Wolverine's `[ReadAggregate]` and `[WriteAggregate]` attributes work transparently with Polecat?
5. **Testcontainers Story**: Is the SQL Server test fixture setup as smooth as the PostgreSQL/Marten fixture?
6. **Cross-BC Saga Participation**: Can a Polecat-persisted aggregate publish integration events (via RabbitMQ) that a Marten-persisted saga reacts to without special configuration?

---

## Open Questions

1. **When will Polecat be available?** NuGet package timeline affects planning. This analysis assumes an alpha/preview is available before the next cycle targeting Polecat adoption.
2. **Will Polecat support inline projections or only async EF Core projections?** Inline projections (synchronous, within the same transaction) have different consistency guarantees than async projections.
3. **How does Polecat handle snapshotting?** Marten's snapshot support is used in long-lived aggregates. If Polecat supports snapshotting, the Returns/Vendor Portal sagas may benefit.
4. **Will there be a `WolverineFx.Polecat` NuGet package?** Similar to `WolverineFx.Http.Marten`, tight Wolverine integration will be needed for the `[ReadAggregate]`/`[WriteAggregate]` patterns.
5. **Should the SQL Server container be in the default `infrastructure` profile or a separate `polecat` profile?** Keeping it separate avoids adding SQL Server overhead for developers not working on Polecat BCs.

---

## References

- [Marten documentation — Event Store](https://martendb.io/events/)
- [JasperFx GitHub organization](https://github.com/JasperFx)
- [Wolverine documentation](https://wolverine.netlify.app/)
- [`Testcontainers.MsSql`](https://dotnet.testcontainers.org/modules/mssql/) — SQL Server TestContainers module
- [CONTEXTS.md — Returns BC](../../CONTEXTS.md#returns) — existing Returns BC design
- [CONTEXTS.md — Vendor Portal BC](../../CONTEXTS.md#vendor-portal) — existing Vendor Portal BC design
- [ADR 0002 — EF Core for Customer Identity](../decisions/0002-efcore-for-customer-identity.md) — precedent for EF Core in this system
