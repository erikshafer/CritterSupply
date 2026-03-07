# ADR 0016: UUID v5 for Deterministic Natural-Key Event Stream IDs

**Status:** ✅ Accepted

**Date:** 2026-03-07

**Context:** Pricing BC stream key design — `ProductPrice` aggregate

---

## Context

The Pricing BC introduces the `ProductPrice` aggregate, which is a **natural-key singleton**: exactly one event stream exists per SKU, for the lifetime of that SKU in the system. Multiple independent handlers must resolve the same stream ID from nothing but the SKU string:

```
ProductAddedHandler              → MartenOps.StartStream<ProductPrice>(StreamId(sku), ...)
SetPriceHandler                  → session.Events.WriteToStream(StreamId(sku), ...)
ProductDiscontinuedHandler       → session.Events.WriteToStream(StreamId(sku), ...)
ActivateScheduledPriceChangeHandler → session.Events.WriteToStream(StreamId(sku), ...)
SetPriceFromBulkJobHandler       → session.Events.WriteToStream(StreamId(sku), ...)
```

None of these actors share state at write time. `ProductAddedHandler` creates the stream; `SetPriceHandler` might run ten minutes later in a different process or after a restart. All must land on the **same** stream ID, derived solely from the SKU string.

The existing codebase uses:
- `Guid.CreateVersion7()` for all non-deterministic streams (Cart, Order, Checkout, Shipment, Payment, VendorPriceSuggestion)
- `MD5.Create()` (`CombinedGuid` in `src/Inventory/Inventory/Management/ProductInventory.cs`) for the existing deterministic stream pattern — identified as technical debt

The question raised during Pricing BC design: **why UUID v5 instead of UUID v7**, given that v7 is used everywhere else in the codebase?

---

## Decision

Use **UUID v5 (RFC 4122 §4.3, SHA-1 + namespace)** for any event stream whose identity is derived from a natural domain key.

Use **UUID v7 (`Guid.CreateVersion7()`)** for all other streams — those whose identity has no natural domain key and is generated at creation time.

---

## Why UUID v7 Cannot Satisfy Determinism

**UUID v7 is not a hash function.** It is a timestamp-plus-random generator. Two calls to `Guid.CreateVersion7()` with the same inputs produce two completely different values. You cannot derive a stable v7 from a string.

Using UUID v7 for `ProductPrice` stream IDs would require:
1. Generating the ID in `ProductAddedHandler` and persisting it in a lookup document
2. Performing a **lookup query on every subsequent write** to retrieve that stream ID before touching the aggregate

That is an additional database round-trip on every `SetPrice`, `ActivateScheduledPriceChange`, `ProductDiscontinuedHandler`, and every bulk job line item — plus a new failure mode (what if the lookup doesn't exist yet due to race conditions with `ProductAdded`?).

The pricing event model's **"no synchronous Catalog HTTP call" principle** applies equally within the BC. Handlers must derive the stream ID locally, without a lookup. UUID v7 is incompatible with this requirement.

---

## Why UUID v5 Over MD5 (Inventory BC's `CombinedGuid`)

The Inventory BC uses MD5 for its deterministic GUID, which is explicitly listed in the Pricing BC's lessons-learned as a pattern not to repeat.

| Dimension | MD5 (`CombinedGuid`) | UUID v5 |
|---|---|---|
| RFC 4122 compliant | ❌ Version/variant bits unset | ✅ Self-describing |
| Namespace isolation | ❌ None — same string → same hash regardless of context | ✅ Namespace UUID prevents cross-context collisions |
| Case normalization | ❌ Not enforced in `CombinedGuid` | ✅ `ToUpperInvariant()` explicit in `StreamId()` |
| Hash strength | MD5 (128-bit, known collision attacks) | SHA-1 (160-bit hash, truncated to 128-bit per RFC 4122 UUID format requirement, stronger preimage resistance than MD5) |
| UUID version field | ❌ Garbage in byte 6 | ✅ `0x50` = version 5 |

The MD5 `CombinedGuid` produces values that look like UUIDs but are not recognized as any valid RFC 4122 version by UUID-aware tooling (validators, observability systems, database audit tools). UUID v5 is self-describing — any developer or tool can inspect the UUID and know it is a deterministic v5.

**The Inventory BC's `CombinedGuid` is technical debt.** Its stream IDs are already written and cannot be changed without a data migration, but new aggregates with natural-key streams should follow the UUID v5 pattern.

---

## PostgreSQL/Marten Performance Considerations

The concern about random (non-sequential) UUIDs in PostgreSQL B-tree indexes is real but **does not apply to the `ProductPrice` stream key at expected scale**.

**Where random UUID B-tree degradation actually bites:** Tables with UUID primary keys receiving thousands of inserts per second. The random UUID causes cache-miss-heavy B-tree leaf splits, increasing write amplification over time.

**Why it doesn't apply here:**
- The `mt_events` table — the write-hot table — uses a `seq_id BIGSERIAL` primary key, not the stream ID. Event appends are physically ordered by `seq_id` regardless of the `stream_id` UUID type. Stream ID UUID version has zero impact on event append throughput.
- The `mt_streams` table has a UUID primary key but grows at the rate of **one row per new SKU**, not one row per event. At 100,000 SKUs (a large catalog), the table has 100,000 rows. The B-tree fits in memory; page split pressure at this scale is negligible.

Sequential UUID benefits (v7 vs v5) for `mt_streams` would only be measurable at tens of millions of SKUs with sustained high-volume SKU creation — far outside the planning horizon for CritterSupply.

For read queries (`WHERE stream_id = $1`), the UUID type is irrelevant — equality predicates on B-tree indexes perform identically for random and sequential UUIDs.

---

## When to Use UUID v5 vs UUID v7

| Use UUID v7 | Use UUID v5 |
|---|---|
| Stream identity has no natural domain key | Stream identity is derivable from a stable natural key |
| ID is generated once at creation and stored/referenced by ID | Multiple independent handlers must compute the same ID locally |
| Examples: Cart, Order, Checkout, Shipment, Payment, VendorPriceSuggestion | Examples: `ProductPrice` (key = SKU), `ProductInventory` (key = SKU + WarehouseId) |

---

## Implementation

```csharp
public static Guid StreamId(string sku)
{
    // UUID v5 (RFC 4122 §4.3): deterministic, namespaced SHA-1 of SKU string.
    //
    // WHY NOT UUID v7: v7 is timestamp+random — it cannot produce the same value
    // twice from the same input. Any handler that writes to this stream must derive
    // this ID locally without a lookup. v7 would require a lookup table + round-trip
    // on every write, adding latency and a new failure mode.
    //
    // WHY NOT MD5 (cf. Inventory.ProductInventory.CombinedGuid): MD5 leaves version
    // and variant bits unset (not RFC 4122-compliant), provides no namespace isolation,
    // and has no case normalization. UUID v5 is the standard for this pattern.
    //
    // NAMESPACE: URL namespace (6ba7b810-...) scopes this UUID space so it cannot
    // collide with v5 UUIDs from the same SKU string in other namespaces.
    //
    // NORMALIZATION: sku.ToUpperInvariant() ensures "dog-food-5lb" == "DOG-FOOD-5LB".
    var namespaceBytes = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8").ToByteArray();
    var nameBytes = Encoding.UTF8.GetBytes($"pricing:{sku.ToUpperInvariant()}");
    var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);
    hash[6] = (byte)((hash[6] & 0x0F) | 0x50);  // Version 5
    hash[8] = (byte)((hash[8] & 0x3F) | 0x80);  // Variant RFC 4122
    return new Guid(hash[..16]);
}
```

---

## Rationale

UUID v5 is the only approach that satisfies all three requirements simultaneously:
1. **Deterministic** — same SKU always produces the same stream ID
2. **RFC 4122 compliant** — self-describing, tooling-compatible
3. **Namespace-isolated** — no cross-domain collisions

UUID v7 fails requirement 1. MD5 fails requirements 2 and 3. UUID v5 is the correct standard for this pattern.

---

## Consequences

**Positive:**
- Any handler in the Pricing BC can compute `ProductPrice.StreamId(sku)` locally with no database round-trips
- Idempotency is free: if `ProductAdded` is delivered twice, both attempts derive the same Guid → Marten's stream-already-exists check handles the duplicate at no additional cost
- The UUID is self-describing — monitoring tools, audit logs, and debuggers can identify it as a deterministic v5
- Case normalization is enforced in one place

**Negative:**
- UUID v5 stream IDs are not timestamp-ordered. This is a non-issue for the `mt_streams` table at catalog scale, but a developer unfamiliar with this ADR might try to "fix" the UUID type. This ADR and the inline `StreamId()` comment prevent that regression.
- The Inventory BC's `CombinedGuid` cannot be retroactively fixed without a data migration. It should be annotated with a comment referencing this ADR as the preferred pattern.

---

## Alternatives Considered

| Alternative | Why Rejected |
|---|---|
| UUID v7 | Cannot produce a deterministic value from a natural key |
| UUID v4 (random) | Same problem as v7 |
| MD5 hash (Inventory BC pattern) | Not RFC 4122-compliant; no namespace isolation; no normalization |
| Raw string SKU as stream ID | Marten event streams use UUID stream IDs by convention; string stream IDs require different API |
| Lookup table (v7 stored at creation) | Adds round-trip on every write; new failure mode; contradicts "no synchronous Catalog call" principle |

---

## References

- [RFC 4122 §4.3 — Version 5 UUID](https://www.rfc-editor.org/rfc/rfc4122#section-4.3)
- [`docs/planning/pricing-event-modeling.md`](../planning/pricing-event-modeling.md) — Lesson 3: "Don't Use MD5 for Deterministic Guids"
- [`src/Inventory/Inventory/Management/ProductInventory.cs`](../../src/Inventory/Inventory/Management/ProductInventory.cs) — `CombinedGuid()` (technical debt reference)
