# ADR 0020: MAP vs Floor Price Distinction

**Status:** ✅ Accepted

**Date:** 2026-03-08

**Context:** Pricing BC Phase 1 & Phase 2+ — How do we model price constraints with different business semantics?

---

## Context

Pricing BC enforces price bounds to protect business margins and comply with vendor contracts. Two types of price constraints exist with **fundamentally different semantics**:

### 1. Floor Price (Internal Business Rule)

**Definition:** The minimum price required to maintain acceptable profit margins.

**Business Owner:** Internal (merchandising team, finance)

**Purpose:** Margin protection — ensures we don't sell at a loss or below minimum profitability threshold

**Enforcement:** Internal policy — we *choose* not to price below this threshold

**Example:** Dog food costs us $12/unit → floor price $15 (25% margin minimum)

---

### 2. MAP — Minimum Advertised Price (External Legal Obligation)

**Definition:** The minimum price we are **contractually obligated** to advertise, as agreed with vendors/manufacturers.

**Business Owner:** External (vendor contract, manufacturer agreement)

**Purpose:** Brand protection — prevents "race to the bottom" pricing that damages brand perception

**Enforcement:** Legal/contractual — violating MAP can result in:
- Loss of vendor relationship
- Termination of supply agreement
- Legal action (contract breach)
- Removal from authorized reseller list

**Example:** Premium dog food brand requires MAP $24.99 — we cannot advertise below this price, even if our floor is $18

---

### The Problem

**Can Floor Price and MAP be the same attribute?** No. They have different:
- **Sources of truth** (internal policy vs external contract)
- **Change triggers** (margin analysis vs contract renegotiation)
- **Enforcement consequences** (lost profit vs lost vendor)
- **Lifecycle** (floor changes quarterly, MAP changes on contract renewal)

**What if MAP > Floor?** Common scenario: Vendor requires MAP $24.99, but our floor is $18. We're contractually bound to advertise ≥$24.99, but could theoretically profit at $18. MAP is the binding constraint.

**What if Floor > MAP?** Less common, but possible: Our costs increased (floor now $28), but vendor MAP is still $24.99. We must either (a) price above MAP, (b) renegotiate contract, or (c) stop carrying product. System must model both constraints.

---

## Decision

**Phase 1:** Single `FloorPrice` attribute (simpler, sufficient for MVP)

**Phase 2+:** Separate `FloorPrice` and `MapPrice` attributes (distinct constraints)

### Phase 1 Implementation (Now)

```csharp
public sealed record ProductPrice(
    Guid Id,
    string Sku,
    PriceStatus Status,
    Money? BasePrice,
    Money? FloorPrice,     // ← Internal margin protection
    Money? CeilingPrice,   // ← Internal maximum (used for anomaly detection)
    ...);
```

**Phase 1 constraints:**
- `BasePrice >= FloorPrice` (validated in `ChangePrice` handler)
- `BasePrice <= CeilingPrice` (optional, for anomaly detection)
- Promotions BC validates `DiscountedPrice >= FloorPrice`

**Why defer MAP?**
- No vendor contracts in Phase 1 (Vendor Portal not implemented)
- Simpler model reduces Phase 1 scope
- Easy to add later without breaking existing logic

---

### Phase 2+ Implementation (Future)

```csharp
public sealed record ProductPrice(
    Guid Id,
    string Sku,
    PriceStatus Status,
    Money? BasePrice,
    Money? FloorPrice,     // ← Internal margin protection
    Money? MapPrice,       // ← NEW: Vendor contractual minimum
    Money? CeilingPrice,
    ...);
```

**Phase 2+ constraints:**
- `BasePrice >= MAX(FloorPrice, MapPrice)` — whichever is higher wins
- `BasePrice <= CeilingPrice` (optional)
- Promotions BC validates `DiscountedPrice >= MAX(FloorPrice, MapPrice)`
- Vendor suggestion approval checks MAP compliance

**New events:**
```csharp
public sealed record MapPriceSet(
    Guid ProductPriceId,
    string Sku,
    Money? OldMapPrice,
    Money MapPrice,
    Guid VendorContractId,  // ← Link to vendor agreement
    Guid SetBy,
    DateTimeOffset SetAt,
    DateTimeOffset? ExpiresAt);  // Contract renewal date
```

---

## Rationale

### Why Separate Attributes?

**1. Different Sources of Truth**

- **Floor Price:** Derived from cost analysis, margin policies, business intelligence
- **MAP:** Derived from vendor contracts, manufacturer agreements, legal documents

Conflating them hides the source of the constraint. When debugging "why can't I price at $20?", the answer matters:
- *"Floor is $22"* → talk to finance about margin policy
- *"MAP is $22"* → talk to vendor about contract renegotiation

**2. Different Lifecycles**

- **Floor Price:** Changes quarterly (cost fluctuations, margin policy updates)
- **MAP:** Changes on contract renewal (annual/bi-annual)

Separate attributes allow independent updates without conflicting.

**3. Different Enforcement**

- **Floor Price Violation:** Internal policy violation → warning, requires manager override
- **MAP Violation:** Legal/contractual violation → hard block, no override allowed (risk of vendor termination)

Enforcement logic differs fundamentally.

**4. Audit Trail Clarity**

Event stream should distinguish:
- `FloorPriceSet` → "Finance updated margin policy for dog food category"
- `MapPriceSet` → "New vendor contract #VND-2026-042 requires $24.99 MAP"

Conflating them muddies the audit trail.

---

### Why Not Phase 1?

**YAGNI (You Aren't Gonna Need It):**
- No vendor contracts exist in Phase 1
- Vendor Portal not yet implemented
- VendorPriceSuggestion workflow (which checks MAP) is Phase 2
- Simpler model = faster Phase 1 delivery

**Easy Phase 2 Migration:**
- Add `MapPrice?` field to `ProductPrice` record (nullable, default: null)
- Add `MapPriceSet` event
- Update `ChangePrice` handler: `NewPrice >= MAX(FloorPrice, MapPrice)`
- No breaking changes to existing events or projections

---

## Consequences

### Positive

✅ **Separation of concerns:** Internal policy vs external contract are distinct
✅ **Audit clarity:** Event stream shows *why* constraint exists
✅ **Enforcement flexibility:** Different rules for Floor vs MAP
✅ **Deferred complexity:** Phase 1 simpler, Phase 2 adds when needed

### Negative

⚠️ **Phase 2 migration required:** Add `MapPrice` field, update validation logic

**Mitigation:** Non-breaking change (nullable field). Existing products with `MapPrice = null` use `FloorPrice` only.

⚠️ **Potential confusion:** "Which constraint applies?"

**Mitigation:** Handler logic is clear:
```csharp
var minimumPrice = Math.Max(
    aggregate.FloorPrice?.Amount ?? 0,
    aggregate.MapPrice?.Amount ?? 0);

if (command.NewPrice < minimumPrice)
    return new ValidationError($"Price below minimum: {minimumPrice}");
```

---

## Alternatives Considered

### Alternative 1: Single "MinimumPrice" Attribute

**Pattern:** One field for both internal floor and vendor MAP

```csharp
public sealed record ProductPrice(..., Money? MinimumPrice);
```

**Pros:**
- ✅ Simpler model (one field, not two)
- ✅ Validation logic is trivial: `NewPrice >= MinimumPrice`

**Cons:**
- ❌ **Lost semantics:** Cannot distinguish internal policy vs vendor contract
- ❌ **Audit trail ambiguity:** `MinimumPriceSet` event doesn't tell you *why* it changed
- ❌ **Lifecycle conflicts:** Floor changes quarterly, MAP changes on contract renewal — how to manage?
- ❌ **Enforcement confusion:** Should override be allowed? Depends on *which* constraint, but we can't tell

**Why rejected:** Loses critical business information. Separate attributes preserve intent.

---

### Alternative 2: MAP as a Promotions BC Concern

**Pattern:** Floor in Pricing BC, MAP in Promotions BC (promotions validate MAP)

**Pros:**
- ✅ Pricing BC stays simple (floor only)
- ✅ Promotions BC owns vendor contract compliance

**Cons:**
- ❌ **Vendor suggestions check MAP:** VendorPriceSuggestion approval (in Pricing BC) must validate MAP, but MAP is in Promotions BC → cross-BC query required
- ❌ **Pricing BC incomplete:** Cannot answer "what's the minimum I can price this?" without querying Promotions
- ❌ **Promotions BC doesn't own contracts:** Vendor agreements are pricing concerns, not promotion concerns

**Why rejected:** MAP belongs in Pricing BC. Vendor contracts define base price constraints, not promotion-specific rules.

---

### Alternative 3: MAP in Vendor Portal BC

**Pattern:** VendorContract aggregate in Vendor Portal, carries MAP for each SKU

**Pros:**
- ✅ Co-locates MAP with vendor agreements

**Cons:**
- ❌ **Pricing BC incomplete:** Must query Vendor Portal BC on every `ChangePrice` → temporal coupling
- ❌ **Vendor Portal doesn't enforce pricing:** Pricing BC enforces constraints; Vendor Portal manages relationships
- ❌ **Cross-BC validation in hot path:** `ChangePrice` synchronously calls Vendor Portal → availability risk

**Why rejected:** Pricing BC must own all pricing constraints (floor, MAP, ceiling). Vendor Portal is source of MAP *data*, but Pricing BC enforces it.

---

## Implementation (Phase 2+)

### Migration Steps

**1. Add MapPrice Field**
```csharp
public sealed record ProductPrice(
    ...,
    Money? FloorPrice,
    Money? MapPrice,    // ← NEW (nullable, default: null)
    ...);
```

**2. Add MapPriceSet Event**
```csharp
public sealed record MapPriceSet(
    Guid ProductPriceId,
    string Sku,
    Money? OldMapPrice,
    Money MapPrice,
    Guid VendorContractId,
    Guid SetBy,
    DateTimeOffset SetAt,
    DateTimeOffset? ExpiresAt);  // Contract expiry triggers MAP review
```

**3. Update ChangePrice Handler**
```csharp
public static class ChangePriceHandler
{
    public static object Before(
        ChangePrice command,
        ProductPrice aggregate)
    {
        // Determine binding minimum (MAX of Floor and MAP)
        var floorAmount = aggregate.FloorPrice?.Amount ?? 0;
        var mapAmount = aggregate.MapPrice?.Amount ?? 0;
        var minimumPrice = Money.Of(Math.Max(floorAmount, mapAmount), "USD");

        if (command.NewPrice < minimumPrice)
        {
            var reason = mapAmount > floorAmount
                ? $"Price below MAP ({minimumPrice}) — vendor contract violation"
                : $"Price below floor ({minimumPrice}) — margin protection";

            return new ValidationError(reason);
        }

        // ... rest of validation
    }
}
```

**4. Update Promotions BC Validation**

Promotions BC queries Pricing BC for both `FloorPrice` and `MapPrice`, uses MAX for validation.

**5. Vendor Suggestion Approval**

When vendor suggests price $X:
- Query ProductPrice for MAP
- If `X < MAP` → reject with reason: *"Suggested price violates MAP requirement"*
- If `X >= MAP` → approve (assuming other validations pass)

---

### Admin Portal UI (Phase 2+)

**Pricing Dashboard Column:**
| SKU | Base Price | Floor | MAP | Binding Constraint |
|-----|------------|-------|-----|-------------------|
| DOG-FOOD-5LB | $24.99 | $18.00 | $22.00 | MAP |
| CAT-LITTER-10LB | $29.99 | $24.00 | $20.00 | Floor |
| FISH-FOOD-1LB | $9.99 | $6.00 | — | Floor |

**Binding Constraint Logic:**
- If `MAP > Floor` → "MAP"
- If `Floor > MAP` → "Floor"
- If `MAP == null` → "Floor"

**UX:** Hovering over "MAP" shows vendor contract ID + expiry date.

---

## References

- **Event Modeling:** `docs/planning/pricing-event-modeling.md` — "Open Decisions Resolved" section (Decision #9)
- **Vendor Portal Event Modeling:** `docs/planning/vendor-portal-event-modeling.md` — Vendor price suggestions (checks MAP)
- **Admin Portal Event Modeling:** `docs/planning/admin-portal-event-modeling.md` — Pricing manager workflows
- **Skill:** `docs/skills/marten-event-sourcing.md` — Immutable aggregate design

---

## Open Questions

**Q: Should MAP be enforceable for non-advertised channels (B2B, wholesale)?**

**A (Phase 2 decision):** No. MAP applies to *advertised* prices (storefront, marketing materials). B2B/wholesale prices are negotiated separately and don't violate MAP. This distinction can be modeled via `Channel` enum if multi-channel pricing becomes a requirement.

**Q: Who can set MAP in Admin Portal?**

**A (Phase 2 decision):** Only **Merchandising Managers** (role check). MAP changes require vendor contract reference. Operations Managers can set Floor, but not MAP (different authority).

**Q: What if MAP expires (contract renewal missed)?**

**A (Phase 2 decision):** `MapPriceSet.ExpiresAt` triggers background job:
- Send alert to Merchandising Manager 30 days before expiry
- On expiry: Either (a) MAP reverts to null, or (b) system locks pricing changes until MAP renewed
- Business decision: lock vs revert

---

**This ADR establishes Floor Price and MAP as distinct attributes with different business semantics, defers MAP implementation to Phase 2 (when Vendor Portal contracts exist), and defines a clear migration path.**
