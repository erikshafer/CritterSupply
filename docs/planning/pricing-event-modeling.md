# Pricing Bounded Context: Event Modeling Session

**Date:** 2026-03-07
**Participants:** Product Owner, Principal Architect (+ UX Engineer review 2026-03-07)
**Status:** 🟡 Design Complete — Awaiting Implementation Cycle Assignment
**Related CONTEXTS.md Sections:** [Pricing](../../CONTEXTS.md#pricing), [Product Catalog](../../CONTEXTS.md#product-catalog), [Shopping](../../CONTEXTS.md#shopping)
**Related ADRs:** [ADR 0016: UUID v5 for Natural-Key Stream IDs](../decisions/0016-uuid-v5-for-natural-key-stream-ids.md) | See additional ADRs needed below
**UX Review:** [`docs/planning/pricing-ux-review.md`](pricing-ux-review.md) — DX analysis, event naming recommendations, `Pricing.Web` UI vision, risks

---

## Purpose

This document captures the collaborative event modeling session between the Product Owner and Principal Architect for the Pricing bounded context. It is the pre-implementation blueprint that supersedes the stub entry in CONTEXTS.md's "Future Considerations" section.

**This is NOT a one-shot implementation request.** It is planning, risk assessment, and event modeling output that feeds into a phased implementation cycle plan.

---

## Why Pricing Deserves Its Own Bounded Context

Product Catalog explicitly declares it does NOT own pricing. Price is one of the most dynamic, compliance-sensitive, and business-critical data points in the system. It changes for competitive reasons, promotional calendars, vendor-negotiated costs, seasonal demand, MAP (Minimum Advertised Price) enforcement, and channel strategy.

Mashing it into Product Catalog — which is read-heavy, rarely changes, and serves as stable master data — would create write-contention, compliance risk, and architectural coupling.

**One more urgent reason:** The current `AddItemToCart` command accepts `UnitPrice` as a client-supplied parameter. A customer could supply any price. **Phase 1 must make pricing server-authoritative at add-to-cart time.** Everything else is secondary.

---

## Lessons Learned from Existing BCs — Do Not Repeat

| Mistake | What to Do Instead |
|---|---|
| `Product.Price` as a `decimal` field in Catalog | Use `Money` value object with currency code from day one |
| `Events` collection vs. single event return bug (Cycle 18) | Always return `Events` collection from `[WriteAggregate]` handlers; write event-persistence test for every handler |
| MD5 for deterministic Guids (Inventory `CombinedGuid`) | Use UUID v5 (SHA-1, RFC 4122) for `ProductPrice.StreamId(sku)` |
| Aggregate in wrong BC requiring migration (Checkout, Cycle 8) | `ProductPrice` in Pricing, `VendorPriceSuggestion` in Pricing, never in Catalog or Vendor Portal |
| `Dictionary<string, T>` mutable state in aggregates (Cart, Inventory) | `ProductPrice` is a pure record with scalar/value-type properties only |
| Deferring integration tests to manual testing (Cycle 18 retrospective) | Alba integration tests written before any manual testing |
| SSE → SignalR migration debt (ADR 0013) | Real-time updates in BFF use SignalR from day one; no SSE for Pricing events |
| Null actor IDs in commands (`Order` saga gap) | Every price-mutating command carries `Guid ChangedBy` — FluentValidation rejects `Guid.Empty` |

---

## Open Decisions Resolved in This Session

| # | Decision | Resolution |
|---|---|---|
| 1 | Draft pricing state — yes or no? | **No for Phase 1.** Scheduling covers "don't publish yet." Phase 2: Draft as "campaign staging" for batch-prepared prices. |
| 2 | Price snapshot: add-to-cart or checkout? | **Add-to-cart freeze with cart price TTL.** Must define TTL (e.g., 1 hour). Contradicts current CONTEXTS.md wording — an ADR is required. |
| 3 | Was/Now strikethrough rule | **30 days from price drop date, or until next price change, whichever is first.** Disappears immediately on price increase. Reset clock on subsequent drops. Log first-shown date per SKU for FTC compliance. |
| 4 | Vendor suggestion notification: Phase 1 or 2? | **Phase 1 minimum.** Vendor cannot be left with no feedback — unanswered suggestions become support tickets within 24 hours. Even a status-poll endpoint is acceptable for Phase 1. |
| 5 | `Money` value object: yes? | **Yes.** Built into Pricing BC from day one. Currency code `USD` hardcoded but stored. |
| 6 | Multi-currency | Out of scope, but `Money` carries currency code so it's future-proof. |
| 7 | Price protection (auto-refund if price drops post-purchase) | **Out of Pricing BC scope.** Phase 3 concern in Returns BC. |
| 8 | BulkPricingJob audit trail | **Must be durable and auditable.** Either event-sourced or explicit `BulkApprovalRecord` document. Approval identity and timestamp are non-negotiable. |
| 9 | MAP vs. Floor price | **Distinct concepts for Phase 2.** Phase 1: single `FloorPrice` attribute. Phase 2+: separate `MapPrice` for vendor contractual obligations. |
| 10 | Anomaly threshold configurability | **Configurable per category, not hardcoded.** Default 30%. |
| 11 | `PriceCorrected` downstream behavior | **Updates `CurrentPriceView` immediately AND publishes `PriceUpdated` integration event.** Does NOT trigger marketing/notification events. The read model and storefront must reflect corrections. |
| 12 | Vendor suggestion TTL | **5–7 business days before auto-expiry or escalation.** `VendorPriceSuggestionExpired` event added. |

---

## Aggregate Design

### Aggregate 1: `ProductPrice` (Event-Sourced, One Per SKU)

**What it owns:** The authoritative price record for a single SKU — current base price, floor, ceiling, pending scheduled changes, and the full append-only history of everything that happened to that SKU's price.

**Stream key:** Deterministic `Guid` using UUID v5 from the SKU string (not MD5, not UUID v7). See **[ADR 0016](../decisions/0016-uuid-v5-for-natural-key-stream-ids.md)** for the full rationale.

**Why not UUID v7?** UUID v7 is a timestamp-random generator — it cannot produce the same value twice from the same input. Multiple handlers in the Pricing BC must derive the same stream ID from just the SKU string without a lookup table. UUID v7 would require persisting the generated ID and performing an extra database round-trip on every write — adding latency and a new failure mode. UUID v5 (SHA-1 + namespace, RFC 4122 §4.3) provides determinism, RFC compliance, namespace isolation, and explicit case normalization.

```csharp
public static Guid StreamId(string sku)
{
    // UUID v5 (RFC 4122 §4.3): deterministic, namespaced SHA-1 of SKU string.
    // WHY NOT UUID v7: v7 cannot produce the same value twice from the same input.
    // WHY NOT MD5: not RFC 4122-compliant; no namespace isolation. See ADR 0016.
    // NORMALIZATION: ToUpperInvariant() ensures "dog-food-5lb" == "DOG-FOOD-5LB".
    var namespaceBytes = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8").ToByteArray(); // URL namespace
    var nameBytes = Encoding.UTF8.GetBytes($"pricing:{sku.ToUpperInvariant()}");
    var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);
    hash[6] = (byte)((hash[6] & 0x0F) | 0x50);  // Version 5
    hash[8] = (byte)((hash[8] & 0x3F) | 0x80);  // Variant RFC 4122
    return new Guid(hash[..16]);
}
```

**Phase 1 State (`PriceStatus` enum: `Unpriced | Published | Discontinued`):**

```csharp
public sealed record ProductPrice(
    Guid Id,                               // = StreamId(Sku)
    string Sku,                            // Canonical SKU string
    PriceStatus Status,                    // Unpriced | Published | Discontinued
    Money? BasePrice,                      // null until first SetPrice
    Money? FloorPrice,                     // Minimum allowed base price
    Money? CeilingPrice,                   // Maximum allowed base price (MAP constraint)
    Money? PreviousBasePrice,              // For Was/Now display — set on every PriceChanged
    DateTimeOffset? PreviousPriceSetAt,    // When previous price was current
    ScheduledPriceChange? PendingSchedule, // null if no scheduled change
    DateTimeOffset RegisteredAt,           // When stream was created (from ProductAdded)
    DateTimeOffset? LastChangedAt)         // When price last changed
```

**Key invariants (enforced in handlers, not aggregates — A-Frame pattern):**
- `BasePrice > Money.Zero` — hard block, no bypass
- `BasePrice >= FloorPrice` when FloorPrice is set — hard block
- `BasePrice <= CeilingPrice` when CeilingPrice is set — hard block
- `FloorPrice < CeilingPrice` when both are set — hard block
- `ScheduledFor > UtcNow` — scheduled changes must be in the future
- Cannot change price on `Discontinued` or `Unpriced` (guard in `Before()`)
- Cannot have two concurrent scheduled changes
- Anomaly detection: >30% delta returns HTTP 202 with `requiresConfirmation: true`; command re-submitted with `ConfirmedAnomaly: true`

---

### Aggregate 2: `VendorPriceSuggestion` (Event-Sourced, One Per Suggestion)

**What it owns:** The full lifecycle of a single vendor price suggestion — from submission through approval or rejection.

**Stream key:** `VendorPriceSuggestionId` — a new `Guid.CreateVersion7()` generated when the suggestion arrives.

**States:** `Pending | Approved | Rejected | Expired`

**State:**

```csharp
public sealed record VendorPriceSuggestion(
    Guid Id,
    string Sku,
    Guid VendorId,
    Money SuggestedPrice,
    SuggestionStatus Status,
    string? VendorJustification,
    Guid? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    Money? ApprovedPrice,              // May differ from SuggestedPrice (manager can adjust)
    string? RejectionReason,
    DateTimeOffset ReceivedAt,
    DateTimeOffset ExpiresAt)          // ReceivedAt + 7 business days
```

**Key invariant:** Only one review action allowed (Approved/Rejected are terminal). `ApprovedPrice` must satisfy floor/ceiling (validated in handler by loading `ProductPrice`).

---

### Aggregate 3: `BulkPricingJob` (Wolverine Saga with Durable Audit Record)

**What it owns:** Stateful orchestration of a bulk price update — tracking per-SKU completion and managing the approval gate for jobs exceeding 100 SKUs.

**Why a Saga:** The saga coordinates across many `ProductPrice` streams. Each individual SKU's price change is recorded on that SKU's own event stream.

**Audit non-negotiable:** Approval events (`BulkJobApproved`, `BulkJobRejected`) must be persisted durably with approver identity and timestamp. Either implement as an event-sourced aggregate or store an explicit `BulkApprovalRecord` in Marten.

**States:** `Draft | AwaitingApproval | Processing | Completed | CompletedWithErrors | Cancelled | Failed`

**Saga message correlation:** Every `SetPriceFromBulkJob` internal command carries `BulkPricingJobId`. Wolverine finds the saga via this property.

---

## Event Definitions

### Domain Events — `ProductPrice` Stream

```csharp
// Created when ProductAdded arrives from Catalog. Stream starts here; no price yet.
// Idempotency: if stream already exists, discard duplicate (at-least-once delivery guard).
public sealed record ProductRegistered(
    Guid ProductPriceId,    // = StreamId(Sku)
    string Sku,
    DateTimeOffset RegisteredAt);

// First time a price is set — Unpriced → Published (Phase 1: no Draft)
// UX RECOMMENDATION: Consider renaming to `InitialPriceSet` for stream readability.
// ProductPriced vs PriceChanged looks like the same event category to new developers.
// InitialPriceSet makes the stream tell a clear story:
//   ProductRegistered → InitialPriceSet → PriceChanged → PriceChangeScheduled → ScheduledPriceActivated
// If renaming: MUST happen before first production write — event types cannot be renamed
// without a data migration or [EventType] alias. Team decision pending — see Event Naming Decision section.
public sealed record ProductPriced(
    Guid ProductPriceId,
    string Sku,
    Money Price,
    Money? FloorPrice,
    Money? CeilingPrice,
    Guid SetBy,              // Required — FluentValidation rejects Guid.Empty
    DateTimeOffset PricedAt);

// Subsequent price mutations — Published → Published (price value changes)
// PreviousPriceSetAt enables Was/Now display in the BFF
// ChangedBy is required on ALL mutating events for audit trail (FluentValidation enforced)
// SourceBulkJobId / SourceSuggestionId: traceability from bulk job or vendor suggestion → price change
public sealed record PriceChanged(
    Guid ProductPriceId,
    string Sku,
    Money OldPrice,
    Money NewPrice,
    DateTimeOffset PreviousPriceSetAt,
    string? Reason,
    Guid ChangedBy,                    // Required — who made this change?
    DateTimeOffset ChangedAt,
    Guid? BulkPricingJobId,           // null for manual changes; set for bulk job changes
    Guid? SourceSuggestionId);        // null for manual changes; set when triggered by vendor suggestion approval

// Scheduled future change registered — creates Wolverine durable scheduled message
public sealed record PriceChangeScheduled(
    Guid ProductPriceId,
    string Sku,
    Guid ScheduleId,           // Correlation ID for the Wolverine scheduled message
    Money ScheduledPrice,
    DateTimeOffset ScheduledFor,
    Guid ScheduledBy,          // Required — who scheduled this?
    DateTimeOffset ScheduledAt);

// Scheduled change cancelled. NOTE: does not cancel Wolverine message —
// stale-message guard in handler discards it when it fires.
// UX NOTE: Renamed from PriceChangeScheduleCancelled to align with ScheduledPriceActivated naming.
public sealed record ScheduledPriceChangeCancelled(
    Guid ProductPriceId,
    string Sku,
    Guid ScheduleId,
    string? CancellationReason,
    Guid CancelledBy,
    DateTimeOffset CancelledAt);

// Fires when the Wolverine scheduled message arrives and schedule is still active.
// Separate from PriceChanged — system-driven (not user-driven). Queryable separately.
public sealed record ScheduledPriceActivated(
    Guid ProductPriceId,
    string Sku,
    Guid ScheduleId,           // Must match PendingSchedule.ScheduleId or handler discards
    Money ActivatedPrice,
    DateTimeOffset ActivatedAt);

// Floor price set or changed by Merchandising Manager (Phase 1)
// Phase 2: separate MapPrice for vendor MAP obligations
// ExpiresAt: nullable — policy bounds may have time limits ("floor price until end of Q3")
public sealed record FloorPriceSet(
    Guid ProductPriceId,
    string Sku,
    Money? OldFloorPrice,
    Money FloorPrice,
    Guid SetBy,
    DateTimeOffset SetAt,
    DateTimeOffset? ExpiresAt);    // null = permanent until manually changed

// Ceiling / MAP set or changed
public sealed record CeilingPriceSet(
    Guid ProductPriceId,
    string Sku,
    Money? OldCeilingPrice,
    Money CeilingPrice,
    Guid SetBy,
    DateTimeOffset SetAt,
    DateTimeOffset? ExpiresAt);    // null = permanent until manually changed

// Retroactive correction — append-only audit record.
// DOES update CurrentPriceView and publishes PriceUpdated integration event.
// Does NOT trigger marketing or customer-notification events.
public sealed record PriceCorrected(
    Guid ProductPriceId,
    string Sku,
    Money CorrectedPrice,
    Money PreviousPrice,
    string CorrectionReason,
    Guid CorrectedBy,
    DateTimeOffset CorrectedAt);

// Fires when ProductDiscontinued arrives from Catalog.
// Clears any pending scheduled changes. Terminal state.
public sealed record PriceDiscontinued(
    Guid ProductPriceId,
    string Sku,
    DateTimeOffset DiscontinuedAt);
```

### Domain Events — `VendorPriceSuggestion` Stream

```csharp
public sealed record VendorPriceSuggestionReceived(
    Guid SuggestionId,
    string Sku,
    Guid VendorId,
    Money SuggestedPrice,
    string? VendorJustification,
    DateTimeOffset ReceivedAt,
    DateTimeOffset ExpiresAt);   // ReceivedAt + 7 business days

public sealed record VendorPriceSuggestionApproved(
    Guid SuggestionId,
    string Sku,
    Money ApprovedPrice,         // May differ from SuggestedPrice
    Guid ApprovedBy,
    string? Notes,
    DateTimeOffset ApprovedAt);

public sealed record VendorPriceSuggestionRejected(
    Guid SuggestionId,
    string Sku,
    string RejectionReason,
    Guid RejectedBy,
    DateTimeOffset RejectedAt);

// Fired by background job after 7 business days with no review
public sealed record VendorPriceSuggestionExpired(
    Guid SuggestionId,
    string Sku,
    DateTimeOffset ExpiredAt);
```

### Domain Events — `BulkPricingJob` Saga

The saga states are `Draft | AwaitingApproval | Processing | Completed | CompletedWithErrors | Cancelled | Failed`. The following events must be specified before the bulk job UI (`BulkJobDetail.razor`) can be built. `BulkJobItemProcessed` is the most critical — without it, progress tracking is impossible.

```csharp
public sealed record BulkJobSubmitted(
    Guid JobId,
    Guid SubmittedBy,
    int TotalSkuCount,
    DateTimeOffset SubmittedAt);

// Transition to AwaitingApproval (triggered when TotalSkuCount > 100)
public sealed record BulkJobSubmittedForApproval(
    Guid JobId,
    Guid SubmittedBy,
    int TotalSkuCount,
    DateTimeOffset SubmittedAt);

// Merchandising Manager approves the job (required when TotalSkuCount > 100)
public sealed record BulkJobApproved(
    Guid JobId,
    Guid ApprovedBy,
    DateTimeOffset ApprovedAt);

// Merchandising Manager rejects the job
public sealed record BulkJobRejected(
    Guid JobId,
    string RejectionReason,
    Guid RejectedBy,
    DateTimeOffset RejectedAt);

// Processing has begun — fired after approval (or immediately if ≤ 100 SKUs)
public sealed record BulkJobStarted(
    Guid JobId,
    DateTimeOffset StartedAt);

// ⭐ Critical for progress tracking and CompletedWithErrors detail
// Each successful SKU price change fires this event
public sealed record BulkJobItemProcessed(
    Guid JobId,
    string Sku,
    Money NewPrice,
    DateTimeOffset ProcessedAt);

// Each failed SKU fires this event — required for CompletedWithErrors UI
public sealed record BulkJobItemFailed(
    Guid JobId,
    string Sku,
    string FailureReason,
    DateTimeOffset FailedAt);

// Terminal success (all SKUs processed, zero failures)
public sealed record BulkJobCompleted(
    Guid JobId,
    int ProcessedCount,
    DateTimeOffset CompletedAt);

// Terminal partial success (some SKUs failed)
public sealed record BulkJobCompletedWithErrors(
    Guid JobId,
    int ProcessedCount,
    int FailedCount,
    DateTimeOffset CompletedAt);

// Terminal cancellation
public sealed record BulkJobCancelled(
    Guid JobId,
    string? CancellationReason,
    Guid CancelledBy,
    DateTimeOffset CancelledAt);

// Terminal failure (job-level error, not individual SKU failures)
public sealed record BulkJobFailed(
    Guid JobId,
    string FailureReason,
    DateTimeOffset FailedAt);
```

### Integration Messages — `Messages.Contracts.Pricing`

```csharp
// Outbound: first price published for a SKU
// Subscribers: Shopping BC (enables add-to-cart), BFF (display price), search indexer
public sealed record PricePublished(
    string Sku,
    decimal BasePrice,
    string Currency,
    DateTimeOffset PublishedAt);

// Outbound: existing published price changed
// Subscribers: Shopping BC (cart price refresh), BFF (cache invalidation)
public sealed record PriceUpdated(
    string Sku,
    decimal OldPrice,
    decimal NewPrice,
    string Currency,
    DateTimeOffset EffectiveAt);

// Inbound: from Vendor Portal when vendor submits a suggestion
public sealed record VendorPriceSuggestionSubmitted(
    Guid SuggestionId,
    string Sku,
    Guid VendorId,
    decimal SuggestedPrice,
    string Currency,
    string? VendorJustification,
    DateTimeOffset SubmittedAt);
```

---

## Event Naming Decision: Why Three Distinct Events

`ProductPriced`, `PriceChanged`, and `ScheduledPriceActivated` are kept as separate events:

- **`ProductPriced`** (or `InitialPriceSet` — see UX review): "For the first time, someone decided what this SKU costs." Transition from `Unpriced` → `Published`. Sets floor and ceiling atomically. A distinct business operation — not just changing a number.
- **`PriceChanged`**: "Someone changed an already-priced product's price." Carries `OldPrice` and `PreviousPriceSetAt` (meaningless on first-price event). User-driven.
- **`ScheduledPriceActivated`**: "The system fired a previously scheduled change." System-driven (Wolverine delivered a scheduled message). Queryable separately from manual changes.

**Do not merge these three into one event. The semantic clarity is worth the extra types.**

**⚠️ Open naming decision:** The UX Engineer recommends renaming `ProductPriced` → `InitialPriceSet` for stream readability. `InitialPriceSet` makes the stream tell a clearer story to developers who have never seen this domain:

```
ProductRegistered → InitialPriceSet → PriceChanged → PriceChangeScheduled → ScheduledPriceActivated
```

This rename **must happen before any production writes** — event types in Marten event streams cannot be renamed without a data migration (or Marten's `[EventType]` alias pattern). Team decision required before the implementation sprint begins. See [`docs/planning/pricing-ux-review.md`](pricing-ux-review.md) Section 1.1 for the full analysis.

Similarly, `PriceChangeScheduleCancelled` has been renamed to `ScheduledPriceChangeCancelled` in the event definitions above to align with `ScheduledPriceActivated` naming.

---

## Read Models (Projections)

### `CurrentPriceView` — Hot Path Read Model

**Pattern:** `SingleStreamProjection` registered as `ProjectionLifecycle.Inline`. Runs in the same transaction as the command handler. Zero async lag.

**Marten document key:** SKU string (not Guid) — enables `session.LoadAsync<CurrentPriceView>("DOG-FOOD-5LB")` directly. Marten's `LoadManyAsync` issues a single `WHERE id = ANY(@ids)` query for bulk lookups.

**⚠️ SKU normalization is critical.** Marten string document IDs are case-sensitive. `"fido-123"` and `"FIDO-123"` are different documents. All inbound SKU strings must be normalized to `ToUpperInvariant()` at the API boundary — in a single `PricingSkuNormalizationMiddleware` or model binder, not scattered throughout handlers. See [`docs/planning/pricing-ux-review.md`](pricing-ux-review.md) Section 1.4 for the three concrete developer pitfalls.

```csharp
public sealed record CurrentPriceView
{
    public string Id { get; init; } = null!;        // = Sku string
    public string Sku { get; init; } = null!;
    public decimal BasePrice { get; init; }
    public string Currency { get; init; } = "USD";
    public decimal? FloorPrice { get; init; }
    public decimal? CeilingPrice { get; init; }
    public decimal? PreviousBasePrice { get; init; }
    public DateTimeOffset? PreviousPriceSetAt { get; init; }
    public PriceStatus Status { get; init; }
    public bool HasPendingSchedule { get; init; }
    public DateTimeOffset? ScheduledChangeAt { get; init; }
    public decimal? ScheduledPrice { get; init; }
    public DateTimeOffset LastUpdatedAt { get; init; }
}
```

**Was/Now display logic (in BFF — not in domain):**
```csharp
bool ShowStrikethrough =
    currentPrice.PreviousBasePrice.HasValue &&
    currentPrice.PreviousPriceSetAt > DateTimeOffset.UtcNow.AddDays(-30) &&
    currentPrice.BasePrice < currentPrice.PreviousBasePrice;
```
Rule: 30 days from price drop date, or until next price change, whichever is first.

### `ScheduledChangesView` — Upcoming Price Changes Calendar

**Pattern:** `MultiStreamProjection` listening to `PriceChangeScheduled`, `ScheduledPriceChangeCancelled`, `ScheduledPriceActivated`. Async daemon (`ProjectionLifecycle.Async`).

```csharp
public sealed record ScheduledChangesView
{
    public Guid Id { get; init; }              // = ScheduleId
    public string Sku { get; init; } = null!;
    public decimal ScheduledPrice { get; init; }
    public string Currency { get; init; } = "USD";
    public DateTimeOffset ScheduledFor { get; init; }
    public Guid ScheduledBy { get; init; }
    public ScheduledChangeStatus Status { get; init; }  // Pending | Activated | Cancelled
    public DateTimeOffset CreatedAt { get; init; }
}
```

### `PendingPriceSuggestionsView` — Pricing Manager Review Queue

**Pattern:** `SingleStreamProjection` per suggestion, keyed by `SuggestionId`. Admin list endpoint queries by `Status == Pending`. Async daemon.

Includes flag: `bool SuggestedPriceBelowFloor` — surfaces floor-price violations prominently for the reviewer.

### Price History

**No separate projection needed for Phase 1.** The event stream itself is the audit trail. Admin price history endpoint reads raw events (`ProductPriced`, `PriceChanged`, `ScheduledPriceActivated`, `PriceCorrected`) from the stream via `session.Events.FetchStreamAsync(streamId)`.

**Phase 2 backlog:** `PriceHistoryView` projection when traffic demands it.

---

## Integration Contracts

### Inbound: `ProductAdded` from Product Catalog

Pricing BC creates a `ProductPrice` event stream in `Unpriced` status. This "registers" the SKU — subsequent `SetPrice` commands have a stream to append to, and handlers can verify SKU existence without calling Catalog over HTTP.

```csharp
public static class ProductAddedHandler
{
    public static IStartStream Handle(Messages.Contracts.ProductCatalog.ProductAdded message)
    {
        var streamId = ProductPrice.StreamId(message.Sku);
        return MartenOps.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, message.Sku, message.AddedAt));
    }
    // Idempotency: wrap in try/catch for stream-already-exists, or check FetchStreamStateAsync first
}
```

If `SetPrice` arrives before `ProductAdded` has been processed: HTTP 404 with message:
> *"SKU `{sku}` has not been registered in Pricing yet. It may still be syncing from the Product Catalog."*

### Inbound: `ProductDiscontinued` from Product Catalog

Pricing BC appends `PriceDiscontinued` to the ProductPrice stream. Clears any pending scheduled changes (ScheduleId guard in `ActivateScheduledPriceChange` handler will discard). Status transitions to `Discontinued` (terminal).

### Inbound: `VendorPriceSuggestionSubmitted` from Vendor Portal

```csharp
public static IStartStream Handle(Messages.Contracts.Pricing.VendorPriceSuggestionSubmitted message)
{
    return MartenOps.StartStream<VendorPriceSuggestion>(
        message.SuggestionId,
        new VendorPriceSuggestionReceived(
            message.SuggestionId, message.Sku, message.VendorId,
            Money.Of(message.SuggestedPrice, message.Currency),
            message.VendorJustification, message.SubmittedAt,
            message.SubmittedAt.AddBusinessDays(7)));
}
```

### The Price Snapshot for Orders — Critical Contract

**Recommendation: Price frozen at add-to-cart time. No synchronous call from Orders to Pricing at checkout time.**

Reasoning:
1. Synchronous Pricing call during checkout creates temporal coupling — Pricing outage blocks checkout
2. Industry standard (Amazon, Shopify, WooCommerce): price locks at add-to-cart
3. `PriceRefreshed` cart event (already in CONTEXTS.md) handles drift notification

**Phase 1 contract (no changes to existing `CheckoutLineItem`):**
```csharp
// In Messages.Contracts.Shopping — UnitPrice IS the price snapshot at add-to-cart time
public sealed record CheckoutLineItem(string Sku, int Quantity, decimal UnitPrice);
```

**Phase 2 contract (extended, non-breaking when Promotions BC exists):**
```csharp
public sealed record CheckoutLineItem(
    string Sku,
    int Quantity,
    decimal ListPrice,       // Base price from Pricing BC
    decimal EffectivePrice,  // ListPrice minus applied discount
    decimal? DiscountAmount, // null if no promotion
    string? PromotionCode);  // null if no promotion
```

**Phase 1 — Close the security gap (server-authoritative pricing at add-to-cart):**
When Pricing BC Phase 1 ships, `AddItemToCart` in the Shopping BC calls `IPricingClient.GetCurrentPriceAsync(sku)` internally. The `UnitPrice` parameter in the command becomes informational only — the server overrides it with the authoritative price from Pricing BC. This is **Phase 1 Priority #1** because the current client-supplied price is a security hole (a customer can submit any price).

**Cart price TTL:** Cart price snapshots are valid for a configurable window (default: 1 hour). After TTL expiry, the BFF refreshes via Pricing BC before displaying cart totals. Shopping BC fires `PriceRefreshed` when this occurs.

**ADR required:** Document the add-to-cart vs. checkout-time price freeze policy, since it contradicts the current CONTEXTS.md wording ("price-at-checkout immutability").

### Outbound: `PricePublished` and `PriceUpdated` to RabbitMQ

Published via Wolverine durable outbox in the same transaction as the domain event. Zero risk of lost integration events even on process failure.

### Promotions BC Query Contract

Promotions BC calls Pricing BC's HTTP API synchronously:
- `GET /api/pricing/products/{sku}` → `CurrentPriceView` (returns `BasePrice`, `FloorPrice`)
- Promotions validates floor price compliance at promotion activation time
- Hard reject (not soft clip) if promotion would price below floor

---

## Consistency Rules and Bounded Context Boundaries

### Optimistic Concurrency

Marten's event stream versioning handles concurrent writes. `[WriteAggregate]` handlers use Wolverine's retry policy on `ConcurrencyException`:

```csharp
opts.OnException<ConcurrencyException>()
    .RetryOnce()
    .Then.RetryWithCooldown(100.Milliseconds(), 250.Milliseconds())
    .Then.Discard();
```

Two managers changing the same SKU concurrently: one wins, one gets retried and succeeds with fresh state.

### Acceptable Eventual Consistency Lag

| Path | Acceptable Lag | Mechanism |
|---|---|---|
| `PricePublished` → Shopping cart display | ≤ 5 seconds | RabbitMQ async, BFF SignalR push |
| `PriceUpdated` → BFF cache invalidation | ≤ 5 seconds | `PriceUpdated` integration event |
| `PriceChanged` → `CurrentPriceView` | 0ms (inline projection) | `ProjectionLifecycle.Inline` |
| `PriceChangeScheduled` → `ScheduledChangesView` | ≤ 1 second | Async daemon |
| `VendorPriceSuggestionReceived` → Manager queue | ≤ 1 second | Async daemon |

`CurrentPriceView` is inline because it is the hot path — zero lag is non-negotiable.

### Scheduled Price Changes — Wolverine Durable Scheduling

Use `outgoing.Delay()`. Do NOT poll a projection.

```csharp
// In SchedulePriceChangeHandler.Handle():
outgoing.Delay(
    new ActivateScheduledPriceChange(command.Sku, scheduleId),
    command.ScheduledFor - DateTimeOffset.UtcNow);
```

**Cancellation strategy:** Append `ScheduledPriceChangeCancelled` to the stream. Do NOT attempt to cancel the Wolverine scheduled message. When it fires, the handler's `Before()` guard checks `PendingSchedule?.ScheduleId != command.ScheduleId` → silently discards. This is the stale-message discard pattern: robust, simple, idiomatic.

**Recovery after restart:** `AddAsyncDaemon(DaemonMode.Solo)` — Wolverine queries `wolverine_incoming_envelopes` on startup and delivers all pending scheduled messages. Late-firing schedules (system was down during scheduled time) are delivered immediately on restart; the `Before()` guard handles stale schedules correctly.

**Scaling note:** Switch to `DaemonMode.HotCold` if multiple `Pricing.Api` instances are ever deployed.

**Idempotency of `ActivateScheduledPriceChange`:** If Wolverine delivers the message twice (at-least-once), the second delivery's `Before()` guard finds `PendingSchedule == null` (already cleared by first activation) and discards. No duplicate `ScheduledPriceActivated` events. This must be tested explicitly.

---

## The `Money` Value Object — Structural Foundation

Every price value in the Pricing BC uses `Money`. No `decimal` for currency.

```csharp
[JsonConverter(typeof(MoneyJsonConverter))]
public sealed record Money
{
    public decimal Amount { get; init; }
    public string Currency { get; init; }   // ISO 4217

    private Money() { }   // Prevent deserialization bypass

    public static readonly Money Zero = Of(0m, "USD");

    public static Money Of(decimal amount, string currency = "USD")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (currency.Length != 3)
            throw new ArgumentException($"Currency must be ISO 4217, got: '{currency}'");
        if (amount < 0)
            throw new ArgumentException($"Money amount cannot be negative: {amount}");
        return new Money
        {
            Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            Currency = currency.ToUpperInvariant()
        };
    }

    public static bool operator >(Money a, Money b)  { AssertSameCurrency(a, b); return a.Amount > b.Amount; }
    public static bool operator <(Money a, Money b)  { AssertSameCurrency(a, b); return a.Amount < b.Amount; }
    public static bool operator >=(Money a, Money b) { AssertSameCurrency(a, b); return a.Amount >= b.Amount; }
    public static bool operator <=(Money a, Money b) { AssertSameCurrency(a, b); return a.Amount <= b.Amount; }

    // Explicit cast (not implicit) — prevents silent currency loss when mixing currencies.
    // Call sites must use (decimal)money or money.Amount to acknowledge currency context.
    public static explicit operator decimal(Money money) => money.Amount;
    public override string ToString() => $"{Amount:C2} {Currency}";
}
```

At API boundaries: commands accept `decimal NewPrice`; handler constructs `Money.Of(command.NewPrice)`. This avoids JSON complexity on the command side while keeping domain model clean.

---

## HTTP API Contracts

### Pricing Queries (Hot Path)

| Method | Endpoint | Description | SLA |
|---|---|---|---|
| `GET` | `/api/pricing/products/{sku}` | Single SKU price lookup | < 100ms p95 |
| `GET` | `/api/pricing/products?skus=...` | Bulk price lookup (20-50 SKUs, comma-separated) | < 100ms p95 |
| `GET` | `/api/pricing/products/{sku}/history` | Price audit trail (admin only) | < 500ms p95 |

### Pricing Commands (Admin)

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/pricing/products/{sku}/price` | Set initial price for a SKU |
| `PUT` | `/api/pricing/products/{sku}/price` | Change existing price |
| `POST` | `/api/pricing/products/{sku}/scheduled-changes` | Schedule a future price change |
| `DELETE` | `/api/pricing/products/{sku}/scheduled-changes/{scheduleId}` | Cancel a scheduled change |
| `PUT` | `/api/pricing/products/{sku}/floor-price` | Set floor price (Merchandising Manager only) |
| `PUT` | `/api/pricing/products/{sku}/ceiling-price` | Set ceiling price |
| `POST` | `/api/pricing/products/{sku}/correct` | Retroactive price correction |

### Pricing Calendar and Bulk Jobs

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/pricing/scheduled-changes?from=...&to=...` | Upcoming changes calendar |
| `POST` | `/api/pricing/bulk-jobs` | Submit bulk pricing job |
| `POST` | `/api/pricing/bulk-jobs/{jobId}/approve` | Approve bulk job (Merchandising Manager) |
| `GET` | `/api/pricing/bulk-jobs/{jobId}` | Bulk job status |

### Vendor Price Suggestions

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/pricing/suggestions?status=pending` | Pricing Manager review queue |
| `POST` | `/api/pricing/suggestions/{suggestionId}/review` | Approve or reject a suggestion |

---

## Project Structure

```
src/Pricing/
├── Pricing/                              # Domain project (Microsoft.NET.Sdk)
│   ├── Constants.cs                      # public const string Pricing = "Pricing"
│   ├── Money.cs                          # Value object + JsonConverter
│   ├── PriceStatus.cs                    # Unpriced | Published | Discontinued
│   ├── SuggestionStatus.cs               # Pending | Approved | Rejected | Expired
│   ├── BulkJobStatus.cs                  # Draft | AwaitingApproval | Processing | ...
│   │
│   ├── ProductPricing/
│   │   ├── ProductPrice.cs               # Aggregate (sealed record + Create + Apply methods)
│   │   ├── ScheduledPriceChange.cs       # Nested value object
│   │   ├── CurrentPriceView.cs           # Inline projection document
│   │   ├── CurrentPriceViewProjection.cs # SingleStreamProjection<CurrentPriceView>
│   │   ├── ScheduledChangesView.cs       # Async daemon projection document
│   │   ├── ScheduledChangesViewProjection.cs
│   │   │
│   │   ├── Events/
│   │   │   ├── ProductRegistered.cs
│   │   │   ├── ProductPriced.cs              # Consider renaming to InitialPriceSet.cs (see UX review)
│   │   │   ├── PriceChanged.cs
│   │   │   ├── PriceChangeScheduled.cs
│   │   │   ├── ScheduledPriceChangeCancelled.cs
│   │   │   ├── ScheduledPriceActivated.cs
│   │   │   ├── FloorPriceSet.cs
│   │   │   ├── CeilingPriceSet.cs
│   │   │   ├── PriceCorrected.cs
│   │   │   └── PriceDiscontinued.cs
│   │   │
│   │   ├── SetPrice.cs                   # Command + Validator + Handler
│   │   ├── ChangePrice.cs                # Command + Validator + Handler
│   │   ├── SchedulePriceChange.cs        # Command + Validator + Handler
│   │   ├── CancelScheduledPriceChange.cs # Command + Validator + Handler
│   │   ├── ActivateScheduledPriceChange.cs # Internal Wolverine scheduled message + Handler
│   │   ├── SetFloorPrice.cs              # Command + Validator + Handler
│   │   ├── SetCeilingPrice.cs            # Command + Validator + Handler
│   │   ├── CorrectPrice.cs               # Command + Validator + Handler
│   │   ├── ProductAddedHandler.cs        # Inbound integration handler (from Catalog)
│   │   └── ProductDiscontinuedHandler.cs # Inbound integration handler (from Catalog)
│   │
│   ├── PriceSuggestions/
│   │   ├── VendorPriceSuggestion.cs      # Aggregate (sealed record)
│   │   ├── PendingPriceSuggestionsView.cs
│   │   ├── PendingPriceSuggestionsViewProjection.cs
│   │   ├── Events/
│   │   │   ├── VendorPriceSuggestionReceived.cs
│   │   │   ├── VendorPriceSuggestionApproved.cs
│   │   │   ├── VendorPriceSuggestionRejected.cs
│   │   │   └── VendorPriceSuggestionExpired.cs
│   │   ├── ReviewPriceSuggestion.cs      # Command + Validator + Handler (approve or reject)
│   │   └── VendorPriceSuggestionSubmittedHandler.cs # Inbound integration handler
│   │
│   └── BulkPricing/
│       ├── BulkPricingJob.cs             # Saga (mutable class : Saga)
│       ├── BulkPriceLineItem.cs          # Value object
│       ├── SubmitBulkPricingJob.cs       # Command + Validator + Handler (starts saga)
│       ├── ApproveBulkPricingJob.cs      # Command + Validator (routed to saga)
│       ├── RejectBulkPricingJob.cs       # Command + Validator (routed to saga)
│       ├── SetPriceFromBulkJob.cs        # Internal command (dispatched by saga)
│       ├── SetPriceFromBulkJobHandler.cs # Handler — wraps ChangePrice logic
│       ├── BulkSkuPriceUpdated.cs        # Internal success event (routes back to saga)
│       └── BulkSkuPriceFailed.cs         # Internal failure event (routes back to saga)
│
├── Pricing.Api/                          # API project (Microsoft.NET.Sdk.Web)
│   ├── Properties/launchSettings.json    # Port: 5242
│   ├── Dockerfile
│   ├── appsettings.json                  # ConnectionStrings: { "marten": "...Database=postgres" }
│   ├── Program.cs                        # Full Marten + Wolverine wiring
│   ├── Pricing/
│   │   ├── GetPriceEndpoint.cs
│   │   ├── GetBulkPricesEndpoint.cs
│   │   ├── GetPriceHistoryEndpoint.cs
│   │   ├── SetPriceEndpoint.cs
│   │   ├── ChangePriceEndpoint.cs
│   │   ├── SchedulePriceChangeEndpoint.cs
│   │   ├── CancelScheduledPriceChangeEndpoint.cs
│   │   └── CorrectPriceEndpoint.cs
│   ├── PriceSuggestions/
│   │   ├── ListPendingSuggestionsEndpoint.cs
│   │   └── ReviewSuggestionEndpoint.cs
│   └── BulkPricing/
│       ├── SubmitBulkJobEndpoint.cs
│       ├── ApproveBulkJobEndpoint.cs
│       └── GetBulkJobStatusEndpoint.cs

src/Shared/Messages.Contracts/Pricing/
│   ├── PricePublished.cs                 # Outbound integration event
│   ├── PriceUpdated.cs                   # Outbound integration event
│   └── VendorPriceSuggestionSubmitted.cs # Inbound from Vendor Portal

tests/Pricing/
└── Pricing.IntegrationTests/
    ├── PricingTestFixture.cs
    ├── ProductPricing/
    │   ├── SetInitialPriceTests.cs
    │   ├── ChangePriceTests.cs
    │   ├── ScheduledPriceChangeTests.cs
    │   └── BulkPricingJobTests.cs
    ├── PriceSuggestions/
    │   └── VendorPriceSuggestionTests.cs
    └── Features/
        ├── pricing.feature
        └── PricingStepDefinitions.cs
```

### Marten Configuration

```csharp
opts.DatabaseSchemaName = Constants.Pricing.ToLowerInvariant();  // "pricing"
opts.Projections.Add<CurrentPriceViewProjection>(ProjectionLifecycle.Inline);
opts.Projections.Snapshot<ProductPrice>(SnapshotLifecycle.Inline);
opts.Projections.Snapshot<VendorPriceSuggestion>(SnapshotLifecycle.Inline);
opts.Projections.Add<PendingPriceSuggestionsViewProjection>(ProjectionLifecycle.Async);
opts.Projections.Add<ScheduledChangesViewProjection>(ProjectionLifecycle.Async);
```

### Port Allocation

**Port 5242** — `Pricing.Api` (reserved in CLAUDE.md)

---

## Phase Prioritization

### Phase 1: Minimum Viable Pricing BC

| Priority | Feature | Why It's Phase 1 |
|---|---|---|
| 🥇 1 | Server-authoritative price at add-to-cart (Shopping BC calls Pricing on `AddItemToCart`) | Current client-supplied price is a security gap |
| 🥇 2 | `ProductPrice` aggregate: Unpriced → Published lifecycle (`SetPrice` command) | Core domain model — no Draft |
| 🥇 3 | `CurrentPriceView` inline projection | Zero-lag storefront price display |
| 🥇 4 | Bulk price lookup API (`GET /api/pricing/products?skus=...`) | Product listing pages need this |
| 🥇 5 | `ProductRegistered` (from `ProductAdded`) and `PriceDiscontinued` (from `ProductDiscontinued`) | Pricing BC must know what SKUs exist |

**Phase 1 explicitly excludes:** Draft state, scheduled changes, Was/Now, vendor suggestions, floor/ceiling prices, anomaly detection, Promotions integration, bulk pricing jobs.

### Phase 2: Operational Completeness

| Priority | Feature | Why It's Phase 2 |
|---|---|---|
| 🥈 1 | Scheduled price changes (`PriceChangeScheduled` → `ScheduledPriceActivated`) | Campaign pricing, Black Friday — important but storefront works without it |
| 🥈 2 | Was/Now strikethrough (`PreviousBasePrice` + BFF display rules) | Revenue driver for conversion, but useless without Phase 1 first |
| 🥈 3 | Floor/ceiling price guards (`FloorPriceSet`, `CeilingPriceSet`) | Required before Promotions BC can go live safely |
| 🥈 4 | Vendor Price Suggestions workflow (`VendorPriceSuggestionSubmitted` handler + review) | Required before Vendor Portal pricing features |
| 🥈 5 | Anomaly detection (>30% change gate, configurable threshold) | Operational safety net; Phase 1 teams are careful manually |

### Phase 3+

- Campaign Draft state (batch-stage prices, publish all at once)
- MAP vs. Floor distinction (vendor contractual obligations)
- Bulk pricing jobs (>100 SKU approval gate)
- Price history projection (when raw stream query performance degrades at scale)
- Price protection policy (in Returns BC, using Pricing BC history queries)
- Channel-specific pricing (storefront vs. B2B vs. marketplace)
- Promotions BC integration

---

## Business Risks

Ranked by severity:

| Risk | Severity | Mitigation |
|---|---|---|
| **$0 or near-zero price bug** | 🔴 Critical | (a) Server-authoritative pricing returns 404 for unpriced SKU, never $0. (b) `Unpriced` status = unavailable for purchase in BFF. (c) Staging env test: deliberately unpriced SKU → verify checkout blocked. |
| **Client-supplied price (current state)** | 🔴 High | Phase 1 Priority #1 ships server-authoritative pricing. Interim: validate against Catalog price field. |
| **Scheduled price fires multiple times** | 🟠 High | `ActivateScheduledPriceChange` handler is idempotent: if schedule already activated (`PendingSchedule == null`), discard. Explicit integration test required. |
| **Silent floor price violation during Promotions** | 🟠 High | Promotions re-validates floor at redemption time (not only at creation). `CurrentPriceView` is inline — floor always current. |
| **Price history gaps from PriceCorrected** | 🟡 Medium | `PriceCorrected` updates `CurrentPriceView` immediately AND publishes `PriceUpdated`. Raw event stream captures full history including corrections. |
| **Event ordering lag: Catalog → Pricing** | 🟡 Medium | Return 404 with clear retry message. Automated bulk imports: handle "not yet registered" with retry queue. |
| **BulkPricingJob audit trail** | 🟡 Medium | Approval event must be persisted with approver identity and timestamp. Either event-sourced or explicit `BulkApprovalRecord` document. |

---

## ADRs Required Before Implementation

1. **ADR: Add-to-cart vs. checkout-time price freeze policy** — Documents the price snapshot decision and contradicts current CONTEXTS.md wording. Cart price TTL must be defined.
2. **ADR: `Money` value object as canonical monetary representation** — Establishes `Money` across all CritterSupply BCs. References Shopping BC's `decimal UnitPrice` and Catalog BC's `decimal Price` as technical debt.
3. **ADR: `BulkPricingJob` audit trail approach** — Documents whether the saga is event-sourced or uses an explicit `BulkApprovalRecord` document.
4. **ADR: MAP vs. Floor price distinction** — Deferred to Phase 2+, but documents why they are separate concepts (vendor contractual obligation vs. internal margin protection) and the planned `MapPrice` attribute.
4. **ADR: MAP vs. Floor price distinction** — Deferred to Phase 2, but documents the design decision to keep them separate.

---

## Event Modeling: Blue / Green / Red Stickies

### Scenario A: Set Initial Price for New Product

```
TIME ────────────────────────────────────────────────────────────────────────────────►

[Catalog BC publishes ProductAdded]
⚙️ ProductAddedHandler (inbound integration)
🟢 ProductRegistered {Sku: "DOG-FOOD-5LB", Status: Unpriced}
   → MartenOps.StartStream<ProductPrice>(StreamId("DOG-FOOD-5LB"), event)

🔴 CurrentPriceView (admin UI queries)
   → SKU "DOG-FOOD-5LB" shown in Unpriced status
   → Pricing Manager sees it in "Pending Pricing" queue

🔵 SetPrice {Sku: "DOG-FOOD-5LB", Price: 24.99, SetBy: managerId}
   → FluentValidation: Price > 0 ✓; ChangedBy != Guid.Empty ✓
   → Load ProductPrice → exists, Status == Unpriced ✓
   → Before(): price > 0 ✓
🟢 ProductPriced {Sku, Price: $24.99, Status → Published}
   → CurrentPriceView updated INLINE: Status: Published, BasePrice: $24.99
   → OutgoingMessages: PricePublished integration event → RabbitMQ
   → Shopping BC can now accept AddItemToCart for this SKU
   → BFF includes price in product listing

POST-CONDITIONS:
  - ProductPrice stream: ProductRegistered + ProductPriced (2 events)
  - CurrentPriceView: Status Published, BasePrice $24.99
  - PricePublished integration event delivered to Shopping BC and BFF
  - Storefront can display price immediately (zero lag from inline projection)

⚠️ SAD PATHS:
  - SetPrice before ProductAdded processed → 404: "SKU has not been registered in Pricing yet"
  - SetPrice with Price = 0 → 422: "Price must be greater than zero"
  - SetPrice for discontinued SKU → 400: "Cannot price a discontinued product"
```

---

### Scenario B: Immediate Price Change

```
TIME ────────────────────────────────────────────────────────────────────────────────►

🔴 GET /api/pricing/products/DOG-FOOD-5LB
   → CurrentPriceView { BasePrice: 24.99, FloorPrice: 18.00 }

🔵 ChangePrice {Sku: "DOG-FOOD-5LB", NewPrice: 21.99, Reason: "Competitive", ChangedBy: managerId}
   → FluentValidation: NewPrice > 0 ✓; ChangedBy != Guid.Empty ✓
   → Load ProductPrice → Status == Published ✓
   → Before():
       21.99 >= FloorPrice (18.00) ✓
       Change% = |21.99 - 24.99| / 24.99 = 12% — below 30% threshold, no anomaly
🟢 PriceChanged {OldPrice: $24.99, NewPrice: $21.99, PreviousPriceSetAt: T-30d}
   → CurrentPriceView updated INLINE: BasePrice: 21.99, PreviousBasePrice: 24.99
   → OutgoingMessages: PriceUpdated integration event → RabbitMQ

(async, <5s):
⚙️ Shopping BC receives PriceUpdated
   → For each active cart containing "DOG-FOOD-5LB":
      🟢 PriceRefreshed {CartId, Sku, OldPrice: 24.99, NewPrice: 21.99}
⚙️ BFF receives PriceUpdated → invalidates cached product listing

⚠️ ANOMALY PATH (>30% change):
  - NewPrice = 14.99 → change% = 40% → EXCEEDS threshold
  - → HTTP 202 Accepted: { "requiresConfirmation": true, "changePercent": 40.0 }
  - No event appended; user must re-submit with ConfirmedAnomaly: true

⚠️ FLOOR VIOLATION:
  - NewPrice = 15.00 < FloorPrice (18.00) → HTTP 422: "Price below floor"

⚠️ CONCURRENT EDIT:
  - Two managers hit Save at same millisecond for same SKU
  - One wins; other gets ConcurrencyException → Wolverine retries
  - Retried handler loads fresh state → succeeds (or no-ops if price unchanged)
```

---

### Scenario C: Scheduled Price Change (Black Friday)

```
TIME ────────────────────────────────────────────────────────────────────────────────►

Day -7:
🔵 SchedulePriceChange {Sku: "DOG-FOOD-5LB", ScheduledPrice: 19.99,
                        ScheduledFor: BlackFridayMidnight, ScheduledBy: managerId}
   → FluentValidation: ScheduledFor > UtcNow ✓; ScheduledPrice > 0 ✓
   → Before(): Status == Published ✓; No existing PendingSchedule ✓
   → 19.99 >= FloorPrice (18.00) ✓ (first attempt at 17.99 rejected: "below floor")
🟢 PriceChangeScheduled {Sku, ScheduleId: guid-A, ScheduledPrice: $19.99, ScheduledFor: T+7d}
   → CurrentPriceView: HasPendingSchedule: true, ScheduledChangeAt: T+7d
   → ScheduledChangesView: new entry for guid-A (Status: Pending)
   ⚙️ outgoing.Delay(ActivateScheduledPriceChange("DOG-FOOD-5LB", guid-A), 7 days)
      → Stored in wolverine_incoming_envelopes (survives process restart)

Day -3 (business decision: cancel):
🔵 CancelScheduledPriceChange {Sku: "DOG-FOOD-5LB", ScheduleId: guid-A}
🟢 ScheduledPriceChangeCancelled {ScheduleId: guid-A}
   → ProductPrice.PendingSchedule = null
   → CurrentPriceView: HasPendingSchedule: false
   → ScheduledChangesView: guid-A → Status: Cancelled
   NOTE: Wolverine message still in DB. When it fires at midnight:
         Before() sees PendingSchedule?.ScheduleId != guid-A → stale, silently discarded

-- Alternative: No cancellation --
Black Friday Midnight (T+7d):
⚙️ Wolverine delivers ActivateScheduledPriceChange {Sku, ScheduleId: guid-A}
   → Load ProductPrice → Before(): PendingSchedule.ScheduleId == guid-A ✓
🟢 ScheduledPriceActivated {Sku, ScheduleId: guid-A, ActivatedPrice: $19.99}
   → CurrentPriceView: BasePrice → 19.99, PreviousBasePrice → 24.99, HasPendingSchedule: false
   → OutgoingMessages: PriceUpdated integration event → Shopping, BFF
   → ScheduledChangesView: guid-A → Status: Activated

IDEMPOTENCY TEST (at-least-once delivery):
   If ActivateScheduledPriceChange delivered twice:
   → Second delivery: Before() sees PendingSchedule == null → discards silently
   → Only one ScheduledPriceActivated event in stream ✓

⚠️ SAD PATHS:
  - System outage at midnight → Wolverine delivers on restart; scheduled price applied late
  - Two schedules created for same SKU → blocked: "Existing pending schedule on this SKU"
  - Schedule fires after PriceDiscontinued → Before(): Status == Discontinued → discards
```

---

### Scenario D: Vendor Price Suggestion Approval Flow

```
TIME ────────────────────────────────────────────────────────────────────────────────►

Vendor Portal BC:
🔵 Vendor submits suggestion via Vendor Portal UI
   → Vendor Portal appends to its own ChangeRequest stream
   → Publishes VendorPriceSuggestionSubmitted integration event → RabbitMQ

Pricing BC:
⚙️ VendorPriceSuggestionSubmittedHandler receives integration event
🟢 VendorPriceSuggestionReceived {SuggestionId, Sku: "DOG-FOOD-5LB",
                                    VendorId, SuggestedPrice: $22.50, ExpiresAt: T+7d}
   → MartenOps.StartStream<VendorPriceSuggestion>(SuggestionId, event)
   → PendingPriceSuggestionsView updated (async, <1s): Status: Pending
   Note: If SuggestedPrice < FloorPrice → SuggestedPriceBelowFloor: true in view

🔴 Pricing Manager queries GET /api/pricing/suggestions?status=pending
   → Returns suggestion: { Sku, SuggestedPrice: $22.50, CurrentPrice: $21.99,
                            FloorPrice: $18.00, SuggestedPriceBelowFloor: false }

[APPROVAL PATH]:
🔵 ReviewPriceSuggestion {SuggestionId, Decision: Approved, ApprovedPrice: 22.00, ApprovedBy: managerId}
   → Before(): Status == Pending ✓; Load ProductPrice → 22.00 within floor/ceiling ✓
🟢 VendorPriceSuggestionApproved {SuggestionId, ApprovedPrice: $22.00}
   → VendorPriceSuggestion: Status → Approved
   → Handler cascades into ChangePrice command on ProductPrice stream:
🟢 PriceChanged {OldPrice: $21.99, NewPrice: $22.00, Reason: "Vendor suggestion #{SuggestionId}"}
   → CurrentPriceView updated inline
   → PriceUpdated integration event → RabbitMQ → Shopping, BFF
   → [Phase 1 minimum]: VendorPriceSuggestionApproved integration event published
     so Vendor Portal can show status to vendor

[REJECTION PATH]:
🔵 ReviewPriceSuggestion {SuggestionId, Decision: Rejected, RejectionReason: "...", RejectedBy: managerId}
🟢 VendorPriceSuggestionRejected {SuggestionId, RejectionReason}
   → Status → Rejected; NO PriceChanged event; price unchanged
   → [Phase 1 minimum]: notification to Vendor Portal

[AUTO-EXPIRY PATH]:
⚙️ Background job fires after 7 business days with no review
🟢 VendorPriceSuggestionExpired {SuggestionId, ExpiredAt}
   → Status → Expired; suggestion removed from review queue

⚠️ SAD PATHS:
  - Vendor Portal unavailable → integration event queued in Wolverine outbox; retried on recovery
  - SuggestedPrice < FloorPrice → surfaced prominently in review queue; reviewer must explicitly override
  - Suggestion for discontinued SKU → allowed in review queue; if approved and SKU is Discontinued, PriceChanged blocked in Before()
  - Manager on vacation → suggestion auto-expires at T+7d; vendor notified (Phase 1 minimum)
```

---

## Pre-Implementation Checklist

Before any implementation code is written:

- [x] **ADR 0016 written:** UUID v5 for deterministic natural-key stream IDs ([docs/decisions/0016-uuid-v5-for-natural-key-stream-ids.md](../decisions/0016-uuid-v5-for-natural-key-stream-ids.md))
- [ ] **ADR written:** Add-to-cart vs. checkout-time price freeze policy
- [ ] **ADR written:** `Money` value object as canonical monetary representation
- [ ] **ADR written:** `BulkPricingJob` audit trail approach
- [ ] **ADR written:** MAP vs. Floor price distinction
- [x] **CONTEXTS.md updated:** Add Pricing BC section, update Future Considerations, update Shopping/Orders integration notes
- [x] **Port 5242** registered in CLAUDE.md port allocation table
- [x] **UX Engineer review complete** ([docs/planning/pricing-ux-review.md](pricing-ux-review.md)) — action items must be resolved before implementation sprint
- [ ] **Event naming decision:** `ProductPriced` → `InitialPriceSet`? Team decision required before first production write
- [ ] **`pricing` schema** added to database init scripts (implementation cycle)
- [ ] **PO confirms:** Cart price TTL — what is the configured default (suggested: 1 hour)?
- [x] **Gherkin feature files** written and reviewed by PO (`docs/features/pricing/`)
- [ ] **Integration test structure** scaffolded (implementation cycle)

---

## What Needs to Happen First (Implementation Order)

1. `Messages.Contracts.Pricing` namespace — `PricePublished`, `PriceUpdated`, `VendorPriceSuggestionSubmitted`
2. `Money` value object in `Pricing` domain project
3. `ProductPrice` aggregate + `ProductRegistered`, `ProductPriced`, `PriceChanged` events
4. `CurrentPriceViewProjection` (inline, keyed by SKU string)
5. `ProductAddedHandler` (creates ProductPrice stream from Catalog integration event)
6. `SetPrice` command handler
7. `ChangePrice` command handler
8. GET endpoints: single price + bulk price lookup
9. Integration tests for all above before marking Phase 1 done
10. Shopping BC: replace client-supplied `UnitPrice` with server call to Pricing BC

---

*This design is based on collaborative event modeling between the Product Owner and Principal Architect. All assumptions flagged above should be resolved in a grooming session before the implementation cycle begins.*
