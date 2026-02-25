# Pricing & Promotions BC — Domain Modeling Spike

**Date:** 2026-02-25  
**Status:** 🔬 Research / Exploratory  
**Author:** Principal Architect  
**Purpose:** Initial domain modeling of the Pricing/Promotions bounded context(s), including DDD analysis of organizational options, event catalog, workflows, and suitability for Polecat (SQL Server-backed event store)

> **This is a spike, not a design spec.** Its goal is to generate discussion between the Product Owner and head developer before any implementation cycle is committed. All decisions herein are proposed, not final.

---

## Background & Motivation

The `polecat-candidates.md` spike identified the Pricing/Promotions domain as a **Tier 2 Polecat candidate**:

> *"Can be co-designed with Polecat's capabilities in mind — maximum flexibility. Naturally event-sourced domain (audit trail of price changes, campaign windows). Risk: Low, but requires domain design investment upfront."*

`CONTEXTS.md` currently lists Pricing and Promotions as future considerations:

> *"Pricing — price rules, promotional pricing, regional pricing"*  
> *"Promotions — buy-one-get-one, percentage discounts, coupon codes"*

Additionally, the Shopping BC already anticipates this domain's existence with planned future events:
- `CouponApplied` / `CouponRemoved` — *explicitly noted as "requires Promotions BC"*
- `PromotionApplied` / `PromotionRemoved` — *"auto-applied promotions, requires Promotions BC"*
- `PriceRefreshed` — *"handles price drift during long sessions, requires Catalog BC [and Pricing BC]"*

The domain is real, the integration points are already reserved. This spike models it.

---

## Ubiquitous Language

Before deciding on boundaries, we must agree on the language. In this domain, words like "price" and "promotion" are overloaded. The following definitions are proposed:

| Term | Definition |
|------|-----------|
| **List Price** | The standard selling price for a SKU before any promotions, discounts, or taxes. Set by the business or vendor. |
| **Effective Price** | The price a customer actually pays after all promotions and discounts are applied. Computed at checkout. |
| **Price Rule** | A configuration that governs how a List Price is set or changed for a given SKU (e.g., markup %, cost-plus). |
| **Price Schedule** | A time-bounded price override (e.g., Black Friday price for SKU-001 from Nov 28–Dec 2). |
| **Promotion** | A marketing campaign that grants a discount to customers who meet specific eligibility criteria. |
| **Campaign** | A grouping concept for Promotions (e.g., "Summer Sale 2026"). A Campaign contains one or more Promotions. |
| **Offer** | A specific discount rule within a Promotion (e.g., "15% off all dog food," "BOGO on cat treats"). |
| **Coupon** | A redeemable code that grants an Offer to a holder. May be one-time-use or multi-use. |
| **Coupon Batch** | A set of Coupons generated from a Promotion (e.g., 1,000 unique codes for email blast). |
| **Redemption** | The act of a customer applying a Coupon or qualifying for an auto-applied Offer at checkout. |
| **Stacking** | Whether multiple Promotions can combine for a single transaction. |
| **Exclusion** | Items or categories explicitly ineligible for a Promotion. |

---

## The Central Boundary Question

The most consequential design decision is: **how many bounded contexts should own this domain?**

Three organizational options are analyzed below.

---

## Option A: Separate `Pricing` BC + Separate `Promotions` BC

### Conceptual Model

```
Pricing BC                        Promotions BC
──────────────────────────────    ──────────────────────────────────────
Owns: List Price per SKU          Owns: Campaigns, Offers, Coupons
      Price Schedules                    Redemption logic
      Price history (audit)              Stacking rules, exclusions
      Price Rule config                  Coupon batch generation

Publishes: PricePublished         Publishes: PromotionActivated
           PriceRevised                    CouponRedeemed
           PriceScheduleStarted           PromotionExpired

Subscribes to: nothing            Subscribes to: PricePublished (to validate
               (source of truth)             offer eligibility against current prices)
```

### DDD Classification

| Context | Classification | Rationale |
|---------|---------------|-----------|
| Pricing BC | **Core Domain** | Price accuracy is a direct business differentiator. Wrong prices = lost revenue or eroded trust. |
| Promotions BC | **Supporting Subdomain** | Promotions support sales strategy but are not unique — many platforms handle promotions similarly. |

### Strengths

- ✅ **Clean separation of concerns** — Pricing is the authoritative source of truth for base prices; Promotions never directly mutates prices, only applies discount overlays
- ✅ **Independent deployability** — Pricing can be updated (e.g., cost changes, margin recalculation) without touching Promotion logic
- ✅ **Clearer team ownership** — A merchandising team might own Pricing, a marketing team might own Promotions
- ✅ **Simpler aggregates** — Each BC stays focused; fewer invariants to juggle per context
- ✅ **Aligned with industry practice** — Most mature e-commerce platforms separate pricing (master price book) from promotions (campaign engine)
- ✅ **Polecat fit** — Each BC can independently adopt Polecat or Marten; optimal choice per team's needs

### Weaknesses

- ⚠️ **More infrastructure** — Two separate APIs, two databases, two deployment units
- ⚠️ **Cross-context coordination** — Checkout price calculation must query both BCs (List Price + active Promotions)
- ⚠️ **More integration contracts** — More messages flowing through RabbitMQ between the two BCs
- ⚠️ **Higher initial development effort** — Two BCs to scaffold, wire, test, and document

### Business Risk

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Price/Promotion inconsistency at checkout | Low | High | Checkout orchestration queries both BCs atomically; Orders BC captures final effective price as immutable fact |
| Team confusion about which BC to modify for a "sale" | Medium | Low | Strong ubiquitous language, clear documentation |
| Over-engineering for a reference architecture | Medium | Medium | Frame both BCs as a single showcase package ("Pricing Suite") in README |

---

## Option B: Unified `Pricing` BC (Promotions as a Sub-Domain/Module)

### Conceptual Model

```
Pricing BC (Unified)
──────────────────────────────────────────────────────
Pricing Module:                   Promotions Module:
  List Price per SKU                Campaign aggregate
  Price Schedules                   Offer aggregate
  Price history                     Coupon aggregate
  Price Rule config                 Redemption logic

Single aggregate root debate:
  Option 1: PriceCatalog + Campaign as separate aggregates within one BC
  Option 2: ProductPrice aggregate (per SKU) with promotional overlay as a read model
```

### Strengths

- ✅ **Lower infrastructure overhead** — One database, one API, one deployment
- ✅ **Simpler checkout integration** — Single BC handles both list price and effective price calculation
- ✅ **Easier to start with** — One BC to scaffold in a reference architecture is less intimidating for readers
- ✅ **Natural for small teams** — When the same people own merchandising + promotions, one BC reduces context switching

### Weaknesses

- ⚠️ **Aggregate boundary confusion** — A `Promotion` and a `Price` are genuinely different concepts with different lifecycles and different business owners
- ⚠️ **Growing complexity** — Promotion stacking rules, exclusion logic, and coupon generation will balloon the BC's invariant surface
- ⚠️ **Harder to separate later** — If the business grows and wants a dedicated promotions team, splitting is painful
- ⚠️ **Violates the Single Responsibility Principle at the BC level** — Pricing is about *what things cost*; Promotions is about *why and when customers pay less*
- ⚠️ **Less educational value** — For a reference architecture, showing two well-bounded contexts teaches more than one monolithic one

### DDD Concern

**Eric Evans** would likely flag this as the "big ball of mud" trap for growing domains. When the ubiquitous language test is applied — "Can you describe all operations in this BC using the same vocabulary?" — a unified Pricing+Promotions BC fails quickly. A merchandising manager talking about "list prices" and a marketing manager talking about "campaign windows" and "redemption caps" are operating in different sub-languages. This is the classic signal that two contexts are being forced into one.

### Business Risk

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| BC grows unmanageable as promotions logic complexity increases | High (long-term) | High | Establish clear module boundaries early; plan for future split |
| Aggregate invariant conflicts between pricing and promotion logic | Medium | Medium | Strict internal module isolation even within one BC |

---

## Option C: Promotions as a Bridge Context (Between Pricing and Future Marketing BC)

### Conceptual Model

```
Pricing BC          Promotions BC (Bridge)       Marketing BC (Future)
──────────────    ──────────────────────────    ──────────────────────
List Prices   ──> Consumes list prices         Campaign management
Price Rules       Applies discount overlays  <── Campaign briefs
Price Schedules   Coupon generation               A/B test config
                  Redemption tracking             Customer segmentation
                  → PriceAtCheckoutResolved       → PromotionCreated
```

### Strengths

- ✅ **Natural growth path** — If CritterSupply eventually adds a Marketing BC (customer segmentation, email campaign management, A/B testing), Promotions is the natural integration seam
- ✅ **Separates pure pricing (financial) from discount strategy (marketing)** — This is how large retailers actually operate (price book team vs. promotions team vs. marketing team)
- ✅ **Promotions BC is a bounded translation layer** — It translates "marketing intent" (Campaign brief) into "pricing reality" (applied discount)

### Weaknesses

- ⚠️ **Over-engineering for current scope** — CritterSupply doesn't have a Marketing BC and won't for many cycles
- ⚠️ **Three-way coordination at checkout** — Pricing + Promotions + Marketing integration adds significant complexity
- ⚠️ **Premature abstraction** — Building for a Marketing BC that doesn't exist yet is a classic YAGNI violation
- ⚠️ **Confusing for reference architecture readers** — A bridge context without its upstream is pedagogically awkward

### Verdict on Option C

**Not recommended at this stage.** This option is the right *eventual* target for a mature platform but is premature for CritterSupply's current trajectory. If a Marketing BC is later added, the Promotions BC can be repositioned as the bridge at that point.

---

## Recommended Approach: Option A (Separate BCs) — Phased

**Recommendation: Build Pricing BC first, Promotions BC second, as two distinct but closely related bounded contexts.**

### Rationale

1. **DDD correctness** — These are genuinely different sub-domains with different ownership semantics, lifecycle frequencies, and team ownership patterns
2. **Reference architecture value** — Two well-modeled BCs that communicate via integration messages teaches the pattern better than one monolithic BC
3. **Polecat pedagogical opportunity** — Pricing BC (stable, audit-heavy) and Promotions BC (campaign-driven, time-bounded) demonstrate slightly different event sourcing characteristics on the same Polecat infrastructure
4. **Practical sequencing** — Start Pricing BC (simpler, foundational), then add Promotions BC (dependent on Pricing's published prices)

---

## Aggregate Design

### Pricing BC Aggregates

#### `ProductPrice` Aggregate (per SKU)

The `ProductPrice` aggregate is the authoritative record of a SKU's list price history. One aggregate stream per SKU.

```
Stream ID: productPrice-{sku}
```

**State:**
```csharp
public sealed record ProductPrice
{
    public string Sku { get; init; }
    public decimal ListPrice { get; init; }
    public string Currency { get; init; } = "USD";
    public PriceStatus Status { get; init; }
    public DateTimeOffset EstablishedAt { get; init; }
    public DateTimeOffset? LastRevisedAt { get; init; }
    public IReadOnlyList<PriceSchedule> ActiveSchedules { get; init; } = [];
}

public enum PriceStatus { Active, Inactive, Discontinued }

public sealed record PriceSchedule(
    Guid ScheduleId,
    decimal ScheduledPrice,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Reason);
```

**Core Invariants:**
- A SKU can only have one active base List Price at a time
- List Price must be > 0
- Price Schedules cannot overlap for the same SKU
- A discontinued SKU's price cannot be revised (terminal state)
- Currency is immutable once set (must establish a new stream if currency changes)

#### `PriceRule` Aggregate

Configures the rule by which prices are set (markup from cost, fixed margin, etc.). Optional — not all SKUs need explicit rules.

```
Stream ID: priceRule-{ruleId}
```

---

### Promotions BC Aggregates

#### `Promotion` Aggregate

A Promotion is a marketing offer with eligibility criteria, discount mechanics, and a lifecycle (draft → active → expired).

```
Stream ID: promotion-{promotionId}
```

**State:**
```csharp
public sealed record Promotion
{
    public Guid PromotionId { get; init; }
    public string Name { get; init; }
    public string Description { get; init; }
    public PromotionStatus Status { get; init; }
    public DiscountType DiscountType { get; init; }
    public decimal DiscountValue { get; init; }          // % or fixed amount
    public PromotionScope Scope { get; init; }           // AllItems, Category, SpecificSkus
    public IReadOnlyList<string> IncludedSkus { get; init; } = [];
    public IReadOnlyList<string> ExcludedSkus { get; init; } = [];
    public IReadOnlyList<string> IncludedCategories { get; init; } = [];
    public bool AllowsStacking { get; init; }
    public int? RedemptionCap { get; init; }             // null = unlimited
    public int CurrentRedemptionCount { get; init; }
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public enum PromotionStatus { Draft, Scheduled, Active, Paused, Expired, Cancelled }
public enum DiscountType { PercentageOff, FixedAmountOff, BuyXGetY, FreeShipping }
public enum PromotionScope { AllItems, ByCategory, BySpecificSkus }
```

**Core Invariants:**
- A Promotion cannot be activated if `StartsAt >= EndsAt`
- A Promotion's `DiscountValue` must be > 0 and ≤ 100 for `PercentageOff`
- A Promotion cannot be modified once `Active` (only Pause or Cancel is allowed)
- `RedemptionCap` cannot be exceeded — the Promotion auto-expires when reached
- An `Expired` or `Cancelled` Promotion is terminal — no further state transitions
- `BuyXGetY` requires explicit `X` and `Y` quantities in extended config
- A Promotion in `Draft` cannot issue Coupons

#### `Coupon` Aggregate

A Coupon is a redeemable code tied to a specific Promotion. Tracking per-Coupon state allows one-time-use enforcement.

```
Stream ID: coupon-{couponCode}
```

**State:**
```csharp
public sealed record Coupon
{
    public string CouponCode { get; init; }
    public Guid PromotionId { get; init; }
    public CouponStatus Status { get; init; }
    public int MaxUses { get; init; }       // 1 for single-use
    public int UseCount { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? RedeemedAt { get; init; }
    public Guid? RedeemedByCustomerId { get; init; }
    public Guid? RedeemedInOrderId { get; init; }
}

public enum CouponStatus { Active, Redeemed, Expired, Revoked }
```

**Core Invariants:**
- A Coupon cannot be redeemed more times than `MaxUses`
- A Coupon cannot be redeemed after `Promotion.EndsAt`
- A Revoked or Expired Coupon is terminal

---

## Core Domain ("Inside") Events

### Pricing BC Events

```csharp
// ── ProductPrice lifecycle ──────────────────────────────────────────────────

/// First time a price is established for a SKU (typically when a new product
/// is added to the catalog and pricing is configured).
public sealed record PriceEstablished(
    string Sku,
    decimal ListPrice,
    string Currency,
    string EstablishedBy,        // "admin" | "vendor" | "import"
    string? Reason,
    DateTimeOffset EstablishedAt);

/// An existing SKU's list price has been changed.
public sealed record PriceRevised(
    string Sku,
    decimal PreviousListPrice,
    decimal NewListPrice,
    string RevisedBy,
    string? Reason,
    DateTimeOffset RevisedAt);

/// A time-bounded price override has been scheduled (e.g., sale price for next week).
public sealed record PriceScheduleCreated(
    string Sku,
    Guid ScheduleId,
    decimal ScheduledPrice,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Reason,
    DateTimeOffset CreatedAt);

/// The scheduled price window has started — this SKU is now on sale.
public sealed record PriceScheduleStarted(
    string Sku,
    Guid ScheduleId,
    decimal ScheduledPrice,
    DateTimeOffset StartedAt);

/// The scheduled price window has ended — this SKU reverts to list price.
public sealed record PriceScheduleEnded(
    string Sku,
    Guid ScheduleId,
    decimal RevertedToPrice,
    DateTimeOffset EndedAt);

/// A scheduled price window was cancelled before it started.
public sealed record PriceScheduleCancelled(
    string Sku,
    Guid ScheduleId,
    string CancelledBy,
    string? Reason,
    DateTimeOffset CancelledAt);

/// A SKU's price is no longer available (product discontinued, delisted, etc.).
public sealed record PriceDeactivated(
    string Sku,
    string DeactivatedBy,
    string? Reason,
    DateTimeOffset DeactivatedAt);

// ── PriceRule lifecycle ─────────────────────────────────────────────────────

/// A new pricing rule has been configured (e.g., "always 40% markup over cost").
public sealed record PriceRuleCreated(
    Guid RuleId,
    string RuleName,
    PriceRuleType RuleType,
    decimal RuleValue,
    IReadOnlyList<string> AppliesTo,   // category codes or SKU patterns
    DateTimeOffset CreatedAt);

public sealed record PriceRuleModified(
    Guid RuleId,
    decimal PreviousRuleValue,
    decimal NewRuleValue,
    string ModifiedBy,
    DateTimeOffset ModifiedAt);

public sealed record PriceRuleDeactivated(
    Guid RuleId,
    string DeactivatedBy,
    DateTimeOffset DeactivatedAt);

public enum PriceRuleType { MarkupFromCost, FixedMarginPercent, CompetitorMatch }
```

### Promotions BC Events

```csharp
// ── Promotion lifecycle ─────────────────────────────────────────────────────

/// A new promotion has been drafted (not yet active or scheduled).
public sealed record PromotionCreated(
    Guid PromotionId,
    string Name,
    string Description,
    DiscountType DiscountType,
    decimal DiscountValue,
    PromotionScope Scope,
    IReadOnlyList<string> IncludedSkus,
    IReadOnlyList<string> ExcludedSkus,
    IReadOnlyList<string> IncludedCategories,
    bool AllowsStacking,
    int? RedemptionCap,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string CreatedBy,
    DateTimeOffset CreatedAt);

/// A draft promotion has been approved for activation (manual or system-scheduled).
public sealed record PromotionActivated(
    Guid PromotionId,
    string ActivatedBy,
    DateTimeOffset ActivatedAt);

/// A promotion was paused mid-campaign (e.g., pricing error, overstock).
public sealed record PromotionPaused(
    Guid PromotionId,
    string PausedBy,
    string Reason,
    DateTimeOffset PausedAt);

/// A paused promotion has been resumed.
public sealed record PromotionResumed(
    Guid PromotionId,
    string ResumedBy,
    DateTimeOffset ResumedAt);

/// A promotion was manually cancelled before its natural end date.
public sealed record PromotionCancelled(
    Guid PromotionId,
    string CancelledBy,
    string Reason,
    DateTimeOffset CancelledAt);

/// A promotion has naturally expired (EndsAt passed or RedemptionCap hit).
public sealed record PromotionExpired(
    Guid PromotionId,
    PromotionExpiryReason ExpiryReason,  // DateReached | RedemptionCapHit
    DateTimeOffset ExpiredAt);

/// The promotion's scope (eligible SKUs/categories) was modified while in Draft state.
public sealed record PromotionScopeRevised(
    Guid PromotionId,
    IReadOnlyList<string> PreviousIncludedSkus,
    IReadOnlyList<string> NewIncludedSkus,
    IReadOnlyList<string> PreviousExcludedSkus,
    IReadOnlyList<string> NewExcludedSkus,
    string RevisedBy,
    DateTimeOffset RevisedAt);

/// A discount was applied to a cart item during checkout evaluation.
public sealed record PromotionRedemptionRecorded(
    Guid PromotionId,
    Guid OrderId,
    Guid CustomerId,
    string? CouponCodeUsed,
    decimal DiscountApplied,
    DateTimeOffset RedeemedAt);

public enum PromotionExpiryReason { DateReached, RedemptionCapHit }

// ── Coupon lifecycle ────────────────────────────────────────────────────────

/// A batch of coupon codes has been generated for a promotion.
public sealed record CouponBatchGenerated(
    Guid PromotionId,
    Guid BatchId,
    int CouponCount,
    int MaxUsesPerCoupon,
    DateTimeOffset GeneratedAt);

/// A single coupon was created as part of a batch.
public sealed record CouponIssued(
    string CouponCode,
    Guid PromotionId,
    Guid BatchId,
    int MaxUses,
    DateTimeOffset IssuedAt);

/// A coupon code was applied to a cart (Shopping BC notifies Promotions BC).
public sealed record CouponApplied(
    string CouponCode,
    Guid PromotionId,
    Guid CartId,
    Guid CustomerId,
    DateTimeOffset AppliedAt);

/// A coupon was redeemed (committed at checkout/order placement).
public sealed record CouponRedeemed(
    string CouponCode,
    Guid PromotionId,
    Guid OrderId,
    Guid CustomerId,
    decimal DiscountApplied,
    DateTimeOffset RedeemedAt);

/// A coupon was removed from a cart before checkout.
public sealed record CouponRemoved(
    string CouponCode,
    Guid CartId,
    Guid CustomerId,
    string Reason,
    DateTimeOffset RemovedAt);

/// A coupon was revoked administratively (fraud, error, etc.).
public sealed record CouponRevoked(
    string CouponCode,
    string RevokedBy,
    string Reason,
    DateTimeOffset RevokedAt);

/// A coupon expired without being redeemed.
public sealed record CouponExpired(
    string CouponCode,
    Guid PromotionId,
    DateTimeOffset ExpiredAt);
```

---

## Integration ("Outside") Events

These messages cross bounded context boundaries via RabbitMQ and live in `Messages.Contracts`.

### Pricing BC → Others

```csharp
// Published when a SKU's effective price has changed and other BCs should be aware.
// Shopping BC uses this to handle PriceRefreshed for items already in cart.
// Product Catalog BC uses this to keep its price display current.
public sealed record PricePublished(
    string Sku,
    decimal NewListPrice,
    string Currency,
    DateTimeOffset EffectiveAt);

// Published when a scheduled sale price starts or ends.
// Allows Shopping BC to display sale badges in real-time.
public sealed record PriceScheduleChanged(
    string Sku,
    Guid ScheduleId,
    decimal EffectivePrice,      // Sale price if started, list price if ended
    PriceScheduleChangeType ChangeType,
    DateTimeOffset ChangedAt);

public enum PriceScheduleChangeType { SaleStarted, SaleEnded }
```

### Promotions BC → Others

```csharp
// Published when a promotion goes live — Shopping BC uses this to display
// applicable promotion badges on cart items.
public sealed record PromotionWentLive(
    Guid PromotionId,
    string Name,
    DiscountType DiscountType,
    decimal DiscountValue,
    PromotionScope Scope,
    IReadOnlyList<string> IncludedCategories,
    IReadOnlyList<string> IncludedSkus,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

// Published when a promotion ends — Shopping BC removes promotion badges.
public sealed record PromotionEnded(
    Guid PromotionId,
    PromotionExpiryReason Reason,
    DateTimeOffset EndedAt);
```

### Shopping BC → Promotions BC (Already Reserved in CONTEXTS.md)

```csharp
// Shopping BC publishes when customer enters a coupon code.
// Promotions BC validates and responds (synchronously via HTTP or async via message).
public sealed record CouponCodeEntered(
    Guid CartId,
    Guid CustomerId,
    string CouponCode,
    DateTimeOffset EnteredAt);

// Shopping BC publishes when customer removes a coupon from cart.
public sealed record CouponCodeRemoved(
    Guid CartId,
    Guid CustomerId,
    string CouponCode,
    DateTimeOffset RemovedAt);
```

### Orders BC → Promotions BC

```csharp
// Orders BC publishes when an order is finalized with a promotion applied.
// Promotions BC uses this to track redemption counts against RedemptionCap.
public sealed record OrderWithPromotionPlaced(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<AppliedPromotion> AppliedPromotions,
    DateTimeOffset PlacedAt);

public sealed record AppliedPromotion(
    Guid PromotionId,
    string? CouponCode,
    decimal DiscountApplied);
```

### Promotions BC → Shopping BC (Response)

```csharp
// Promotions BC responds to CouponCodeEntered with validation result.
public sealed record CouponValidationResult(
    Guid CartId,
    string CouponCode,
    bool IsValid,
    string? InvalidReason,        // null if valid
    Guid? PromotionId,            // populated if valid
    decimal? DiscountValue,       // populated if valid
    DiscountType? DiscountType);  // populated if valid
```

---

## Key Workflows

### Workflow 1: Price Establishment for a New Item

Triggered when a new product is added to the Product Catalog BC.

```
[Product Catalog BC]
ProductCreated (domain event, internal to Catalog)
  └─> ProductListingPublished (integration message → RabbitMQ)

[Pricing BC]
ProductListingPublished (received from RabbitMQ)
  └─> ProductListingReceivedHandler
      ├─> If auto-pricing rule exists for this category:
      │     EstablishPrice (command, derived from PriceRule)
      │       └─> PriceEstablishedHandler
      │           ├─> ProductPrice aggregate created
      │           ├─> PriceEstablished (domain event)
      │           └─> Publish PricePublished → RabbitMQ (other BCs aware of initial price)
      └─> If no rule: create ProductPrice with Status=PendingReview (human required)

[Admin/Vendor action — if pending review]
EstablishPrice (command, from admin UI or vendor portal)
  └─> EstablishPriceHandler
      ├─> ProductPrice aggregate created
      ├─> PriceEstablished (domain event)
      └─> Publish PricePublished → RabbitMQ

[Shopping BC — reaction]
PricePublished (received from RabbitMQ)
  └─> PricePublishedHandler
      └─> Update local price cache (if maintained) or note for next cart hydration
```

---

### Workflow 2: Price Change for an Existing Item

Triggered by a merchandising decision (cost increase, margin adjustment, competitor response).

```
[Admin UI / Pricing BC API]
RevisePrice (command)
  └─> RevisePriceHandler
      ├─> Load ProductPrice aggregate (by SKU)
      ├─> Validate: price > 0, SKU not discontinued, new price ≠ current price
      ├─> PriceRevised (domain event appended to stream)
      └─> Publish PricePublished → RabbitMQ

[Shopping BC — reaction]
PricePublished (received from RabbitMQ)
  └─> PricePublishedHandler
      └─> For each active cart containing this SKU:
            Append PriceRefreshed event to Cart stream
            (customer sees updated price on next page load)
```

---

### Workflow 3: Scheduled Sale Price (Black Friday, etc.)

```
[Admin UI / Pricing BC API]
SchedulePrice (command)
  └─> SchedulePriceHandler
      ├─> Load ProductPrice aggregate
      ├─> Validate: no overlapping schedules, StartsAt < EndsAt, price > 0
      ├─> PriceScheduleCreated (domain event)
      └─> No integration message yet (schedule is in the future)

[Scheduled job — when StartsAt arrives]
  └─> PriceScheduleActivator (Wolverine scheduled message or cron)
      ├─> PriceScheduleStarted (domain event)
      └─> Publish PriceScheduleChanged (SaleStarted) → RabbitMQ

[Shopping BC — reaction]
PriceScheduleChanged (received from RabbitMQ)
  └─> Display sale badge on affected products in cart

[Scheduled job — when EndsAt arrives]
  └─> PriceScheduleActivator
      ├─> PriceScheduleEnded (domain event)
      └─> Publish PriceScheduleChanged (SaleEnded) → RabbitMQ
```

---

### Workflow 4: Promotion Creation & Activation

```
[Marketing/Admin UI / Promotions BC API]
CreatePromotion (command)
  └─> CreatePromotionHandler
      ├─> Validate: dates valid, discount value in range, scope non-empty
      ├─> Promotion aggregate created (Status=Draft)
      ├─> PromotionCreated (domain event)
      └─> No integration message (draft is internal only)

[Optional: Generate coupon batch while still in Draft]
GenerateCouponBatch (command)
  └─> GenerateCouponBatchHandler
      ├─> Load Promotion aggregate, validate Status=Draft
      ├─> CouponBatchGenerated (domain event on Promotion stream)
      ├─> For each coupon in batch:
      │     CouponIssued (domain event on Coupon stream, stream ID = coupon code)
      └─> Coupons ready for distribution (email blast, QR codes, etc.)

[Activation — manual or time-triggered]
ActivatePromotion (command)
  └─> ActivatePromotionHandler
      ├─> Load Promotion aggregate, validate Status=Draft or Scheduled
      ├─> PromotionActivated (domain event)
      └─> Publish PromotionWentLive → RabbitMQ

[Shopping BC — reaction]
PromotionWentLive (received from RabbitMQ)
  └─> PromotionWentLiveHandler
      └─> Update active promotions cache for cart display
          (show "Sale!" badges, auto-apply eligible promotions)
```

---

### Workflow 5: Coupon Code Entry in Cart (Real-Time Validation)

```
[Customer action — Shopping BC]
ApplyCouponToCart (command)
  └─> ApplyCouponToCartHandler
      ├─> Call Promotions BC: ValidateCoupon (HTTP, synchronous)
      │     └─> Promotions API validates:
      │           - Coupon exists and is Active
      │           - Promotion is currently Active (dates, cap not hit)
      │           - Customer hasn't already redeemed (if single-use)
      │           - At least one cart item is eligible (scope check)
      │           Returns: CouponValidationResult
      ├─> If valid:
      │     CouponApplied (domain event on Cart stream)
      │     └─> Cart now shows discounted line items
      └─> If invalid:
            Return error to customer (coupon not found, expired, already used, etc.)

[Coupon removed from cart]
RemoveCouponFromCart (command)
  └─> RemoveCouponFromCartHandler
      ├─> CouponRemoved (domain event on Cart stream)
      └─> Publish CouponCodeRemoved → RabbitMQ
          └─> Promotions BC notes coupon was not committed (no redemption recorded)
```

> **Design Note — Sync vs Async for Validation:**
> Coupon validation is intentionally synchronous (HTTP call) because the customer is waiting for immediate feedback. If the Promotions BC is unavailable, the cart gracefully degrades (coupon validation fails, customer is shown a retry message). Coupon *commitment* (recording the redemption) is async via RabbitMQ and happens when the order is placed.

---

### Workflow 6: Promotion-Aware Price Resolution at Checkout

The Orders BC is the final authority on the effective price. It queries both Pricing BC (for current list price) and Promotions BC (for applicable discounts) and records the result as an immutable fact in the order.

```
[Orders BC — Checkout completion]
CompleteCheckout (command)
  └─> CompleteCheckoutHandler
      ├─> For each line item:
      │     Query Pricing BC: GET /api/pricing/{sku}/effective-price
      │       └─> Returns: list price + any active scheduled price
      │     Query Promotions BC: GET /api/promotions/applicable?sku={sku}&cartId={cartId}
      │       └─> Returns: list of applicable active promotions + discount amounts
      │     Calculate effective price = max(list price - total discount, 0)
      │     Apply stacking rules (if AllowsStacking=false, use highest discount only)
      ├─> CheckoutCompleted (domain event with LineItems including EffectivePrice per item)
      ├─> Publish OrderPlaced (integration message)
      └─> Publish OrderWithPromotionPlaced → Promotions BC (for redemption tracking)

[Promotions BC — reaction to order placement]
OrderWithPromotionPlaced (received from RabbitMQ)
  └─> OrderWithPromotionPlacedHandler
      ├─> For each AppliedPromotion:
      │     Load Promotion aggregate
      │     Increment CurrentRedemptionCount
      │     Append PromotionRedemptionRecorded event
      │     If RedemptionCap hit: append PromotionExpired event
      │                           Publish PromotionEnded → RabbitMQ
      └─> For each CouponCode used:
            Load Coupon aggregate
            Append CouponRedeemed event
            Update Status → Redeemed
```

---

### Workflow 7: Promotion Expiry (Time-Based)

```
[Scheduled job — when EndsAt arrives]
  └─> PromotionExpiryChecker (Wolverine scheduled message / cron)
      ├─> Load all Active promotions where EndsAt <= now
      ├─> For each:
      │     ExpirePromotion (command)
      │       └─> ExpirePromotionHandler
      │           ├─> PromotionExpired (domain event, Reason=DateReached)
      │           └─> Publish PromotionEnded → RabbitMQ
      └─> Publish PromotionEnded → RabbitMQ

[Shopping BC — reaction]
PromotionEnded (received from RabbitMQ)
  └─> Remove promotion badges from active carts
      Remove auto-applied promotions from affected cart items
      (next cart page load reflects non-discounted prices)

[Promotions BC — coupon cleanup]
PromotionExpired (domain event on Promotion stream)
  └─> Cascade: mark all unredeemed Coupons for this Promotion as Expired
      (handled by a downstream projection or saga, not inline)
```

---

## Read Models / Projections

These are the EF Core projections Polecat would power, making the case for its choice over Marten.

### Pricing BC Projections

| Projection | Description | Key Queries |
|-----------|-------------|-------------|
| `CurrentPriceCatalog` | Current list price per SKU (denormalized, fast lookup) | `GET /api/pricing/{sku}`, bulk SKU price fetch for cart |
| `PriceHistory` | Full price change history per SKU | Audit reports, "price over time" chart, vendor dispute resolution |
| `ActivePriceSchedules` | All price schedules currently running or upcoming | Admin dashboard, schedule conflict detection |
| `PriceChangeAuditLog` | Who changed what price when and why | Compliance, finance reconciliation |

### Promotions BC Projections

| Projection | Description | Key Queries |
|-----------|-------------|-------------|
| `ActivePromotions` | All currently live promotions with scope and discount | Cart eligibility check, storefront badge display |
| `PromotionSummary` | Campaign performance: redemption count vs cap, revenue impact | Marketing dashboard, ROI reporting |
| `CouponRedemptionLog` | Which customers redeemed which coupons for which orders | Fraud detection, customer service |
| `CouponAvailability` | Is this coupon code still valid? (fast lookup) | Checkout validation (hot path) |
| `PromotionCalendar` | Scheduled start/end dates across all campaigns | Marketing planning view, conflict detection |

> **EF Core Projection Note:** The `PriceHistory` and `CouponRedemptionLog` projections are particularly well-suited to EF Core's LINQ-based queries — filtering by date range, joining with customer/order data, and aggregating metrics are all idiomatic EF Core territory where Marten's JSONB projections are less ergonomic.

---

## Polecat Suitability Analysis

### Why Event Sourcing is the Right Tool for Pricing

| Characteristic | Business Value |
|---------------|---------------|
| **Full audit trail of price changes** | "What was the price of DOG-BOWL-001 on Black Friday 2025?" is answered definitively from the event stream — no separate audit table needed |
| **Who changed it and why** | Every `PriceRevised` event carries `RevisedBy` + `Reason` — compliance and dispute resolution are built-in |
| **Temporal queries** | Reconstruct price state at any point in time by replaying events up to that timestamp |
| **Price schedule immutability** | `PriceScheduleCreated` → `PriceScheduleStarted` → `PriceScheduleEnded` is a reliable state machine; no mutable "is_active" flags |

### Why Event Sourcing is the Right Tool for Promotions

| Characteristic | Business Value |
|---------------|---------------|
| **Campaign audit trail** | "When was this promotion activated? Who approved it? How many redemptions happened by hour?" — all in the event stream |
| **Redemption cap enforcement** | `PromotionRedemptionRecorded` events allow exact counting; no race conditions with optimistic concurrency on the aggregate |
| **Point-in-time reconstruction** | Reconstruct promotion state at order time for dispute resolution ("was this promotion active when order #12345 was placed?") |
| **Coupon lifecycle** | From `CouponIssued` through `CouponRedeemed` or `CouponExpired` is a natural event-sourced story |

### Why Polecat (SQL Server) Adds Value Here

| Factor | Polecat Advantage |
|--------|------------------|
| **EF Core projections** | `PriceHistory`, `CouponRedemptionLog`, and `PromotionSummary` are relational queries at heart — date-range filtering, aggregation by time bucket, joins against customer/order data. EF Core LINQ over SQL Server tables outperforms Marten's JSONB projections for this class of query. |
| **SQL Server tooling** | Finance/compliance teams familiar with SSMS or Azure Data Studio can query price history directly without learning PostgreSQL — high practical value for real-world adoption |
| **Pedagogical contrast** | Pricing BC or Promotions BC being on Polecat/SQL Server while Orders, Shopping, Inventory are on Marten/PostgreSQL demonstrates the polyglot persistence story — all connected via RabbitMQ, BC boundaries transparent to the event bus |
| **Azure SQL alignment** | E-commerce pricing and promotions systems frequently live in Azure SQL in enterprise settings; Polecat/SQL Server demonstrates that path |

---

## Risks & Open Questions

### Risk Matrix

| Risk | Likelihood | Impact | Applies To | Mitigation |
|------|-----------|--------|-----------|-----------|
| Promotion stacking logic becomes a complexity trap | High (long-term) | Medium | Both options | Defer complex stacking to Phase 2; start with simple "highest discount wins" or "no stacking" |
| Price drift between Pricing BC and checkout price | Low | High | Both options | Orders BC is the final authority — it captures effective price as immutable fact |
| Coupon validation sync call creates coupling | Medium | Medium | Both options | HTTP call acceptable; implement circuit breaker + graceful degradation |
| Domain model under-specified before implementation | Medium | High | Option A | This spike is step 1; require CONTEXTS.md design before cycle kick-off |
| Two greenfield BCs in one cycle is too much scope | Medium | Medium | Option A | Sequence: Pricing BC first (cycle N), Promotions BC second (cycle N+1) |
| Polecat API not ready when implementation begins | Medium | High | Both options | Spike on Polecat NuGet availability before committing a cycle; Returns BC can serve as the first Polecat test |

### Open Questions for Product Owner + Head Dev

1. **BC Boundary:** Does the business have (or anticipate) separate teams for merchandising/pricing and marketing/promotions? If yes → Option A (separate BCs) is strongly favored for team alignment. If no → Option B (unified) is acceptable for now with a planned split later.

2. **Promotion Stacking:** Should CritterSupply support multiple promotions combining on a single order? Or "one promotion per order" for simplicity in the first implementation?

3. **Customer-Specific Pricing:** Does CritterSupply need loyalty pricing, VIP tiers, or customer-segment-specific prices? This would pull in Customer Identity BC as an upstream for Pricing — not modeled in this spike.

4. **Price Ownership:** Who sets the list price? Vendor via Vendor Portal? CritterSupply admin? Imported from cost sheet? The answer affects whether `EstablishPrice` is a vendor command or an admin command.

5. **Regional Pricing:** Is there multi-currency or regional price variation in scope? The current model assumes USD only. Adding multi-currency requires a separate `Currency` dimension on the `ProductPrice` aggregate.

6. **Coupon Code Format:** Random alphanumeric (e.g., `SAVE20-A3X9K`)? Human-friendly (e.g., `SUMMER25`)? Barcode-compatible? This affects Coupon aggregate stream ID design.

7. **Polecat Timing:** Should Pricing/Promotions wait for Polecat's NuGet release, or should it be built with Marten first and migrated? The Returns BC spike recommends using Returns as the "first Polecat BC" — Pricing/Promotions could follow as the second.

8. **Reference Architecture Sequencing:** Returns BC and Vendor Portal BC are already queued (Cycle 21+). Does Pricing/Promotions jump the queue, or does it enter after those are complete?

---

## Recommended Implementation Phasing

### Phase 1 — Pricing BC (Foundation)

**Scope:** `ProductPrice` aggregate + read models + integration publishing

**Deliverables:**
- `EstablishPrice`, `RevisePrice`, `SchedulePrice` commands and handlers
- `ProductPrice` aggregate with full event lifecycle
- `CurrentPriceCatalog` projection (EF Core, via Polecat)
- `PricePublished` integration message → RabbitMQ
- Shopping BC handler for `PricePublished` (append `PriceRefreshed` to Cart)
- Integration tests (TestContainers.MsSql)
- HTTP endpoints: `GET /api/pricing/{sku}`, `POST /api/pricing/{sku}`, `PUT /api/pricing/{sku}`

---

### Phase 2 — Promotions BC (Campaign Engine)

**Scope:** `Promotion` aggregate + `Coupon` aggregate + redemption tracking

**Deliverables:**
- `CreatePromotion`, `ActivatePromotion`, `PausePromotion`, `CancelPromotion` commands
- `GenerateCouponBatch` command + coupon issuance
- `ActivePromotions` and `CouponAvailability` projections (EF Core)
- `PromotionWentLive` / `PromotionEnded` integration messages → RabbitMQ
- Shopping BC handlers for `PromotionWentLive` / `PromotionEnded`
- Coupon validation HTTP endpoint for Shopping BC sync call
- `OrderWithPromotionPlaced` handler (redemption tracking)
- Integration tests
- HTTP endpoints for promotion management + coupon validation

---

### Phase 3 — Promotion-Aware Checkout (Orders BC Enhancement)

**Scope:** Update Orders BC checkout to query Pricing + Promotions for effective price

**Deliverables:**
- Update `CompleteCheckoutHandler` to query both BCs
- `EffectivePrice` captured per line item in `CheckoutCompleted` event
- Stacking rules applied (Phase 3 can start with "no stacking" and add stacking in Phase 4)
- Customer Experience BFF updated to display discounted prices in cart/checkout views

---

## Context Map (Proposed)

```
                         ┌─────────────────────────────────────────────────────┐
                         │                  RabbitMQ Message Bus                │
                         └──────────────────────────────────────────────────────┘
                                │                           │
         ┌──────────────────────┼───────────────────────────┼──────────────────────┐
         │                      │                           │                      │
    ┌────▼─────┐         ┌──────▼──────┐           ┌───────▼──────┐        ┌──────▼──────┐
    │ Pricing  │         │  Promotions │           │   Shopping   │        │   Orders    │
    │    BC    │─────────▶     BC      │           │     BC       │        │     BC      │
    │(Polecat) │PricePublished(Polecat)│CouponCodeEntered         │OrderWithPromotionPlaced
    └──────────┘         └─────────────┘           └──────────────┘        └─────────────┘
         ▲                      │                       ▲    │                    │
         │              PromotionWentLive                │    │ PriceRefreshed     │ CheckoutCompleted
         │              PromotionEnded                   │    └────────────────────┘
    ┌────┴─────┐                │                   ────┘
    │ Product  │         ┌──────▼──────┐
    │ Catalog  ◀─────────│  Customer   │
    │    BC    │ProductListing│ Experience │ (BFF)
    └──────────┘Published└─────────────┘

Relationship Types:
  Pricing → Shopping:     Published Language (PricePublished is the shared vocabulary)
  Promotions → Shopping:  Published Language (PromotionWentLive, PromotionEnded)
  Shopping → Promotions:  Customer/Supplier (Shopping triggers coupon validation)
  Promotions → Orders:    Conformist (Orders captures effective price, Promotions just records)
  Catalog → Pricing:      Upstream/Downstream (Catalog publishes products, Pricing reacts)
```

---

## Summary

| Dimension | Assessment |
|-----------|-----------|
| **Domain Classification** | Pricing = Core Domain; Promotions = Supporting Subdomain |
| **Recommended BC Structure** | Separate BCs (Option A) — phased delivery |
| **Event Sourcing Fit** | Excellent — full audit trail, temporal queries, immutable price history |
| **Polecat Fit** | Strong — EF Core projections are superior for price history and redemption analytics; SQL Server tooling adds enterprise storytelling |
| **Implementation Risk** | Low-Medium (domain design investment required; promotion logic can be complex) |
| **Sequencing Recommendation** | After Returns BC (first Polecat test); Pricing BC (Phase 1), Promotions BC (Phase 2) |
| **Pedagogical Value** | High — demonstrates polyglot persistence (Marten + Polecat), cross-BC event flows, coupon lifecycle |

---

## References

- [CONTEXTS.md](../../../CONTEXTS.md) — Architectural source of truth; Shopping BC future events, Orders BC price-at-checkout
- [polecat-candidates.md](./polecat-candidates.md) — Polecat candidate analysis; Promotions/Pricing Tier 2 recommendation
- [ADR 0002 — EF Core for Customer Identity](../decisions/0002-efcore-for-customer-identity.md) — Precedent for EF Core in identity-adjacent domains
- [skills/marten-event-sourcing.md](../../../skills/marten-event-sourcing.md) — Event sourcing patterns applicable to Polecat (near-identical API)
- [skills/wolverine-message-handlers.md](../../../skills/wolverine-message-handlers.md) — Compound handler patterns for Pricing/Promotions handlers
- [skills/critterstack-testing-patterns.md](../../../skills/critterstack-testing-patterns.md) — TestContainers patterns applicable to SQL Server (Testcontainers.MsSql)
- [Evans, Eric — Domain-Driven Design (2003)](https://www.amazon.com/Domain-Driven-Design-Tackling-Complexity-Software/dp/0321125215) — Bounded context, ubiquitous language, context map patterns

---

*Last Updated: 2026-02-25*  
*Status: 🔬 Spike — For discussion between Product Owner and head developer. Not a final design.*
