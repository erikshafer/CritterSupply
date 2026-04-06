# M40.0 Milestone Closure Retrospective — Dynamic Consistency Boundary: Promotions BC

**Date:** 2026-04-06
**Milestone:** M40.0
**Sessions:** S1 (Implementation), S1B (Real DCB API), S2 (Documentation Closure)
**ADR:** [0058 — DCB Promotions Coupon Redemption](../../decisions/0058-dcb-promotions-coupon-redemption.md)

---

## Milestone Overview

**Goal:** Introduce Marten's Dynamic Consistency Boundary (DCB) pattern to CritterSupply via the Promotions BC coupon redemption — replacing a two-command fan-out (`RedeemCoupon` + `RecordPromotionRedemption`) with a single atomic decision spanning the Coupon and Promotion event streams.

**Sessions:**
- **S1 (Implementation):** Built the DCB architecture using manual `LoadAsync` multi-stream aggregation. 30/30 tests. ADR 0058 written.
- **S1B (Real DCB API):** Replaced the manual approach with Marten's native `EventTagQuery` + `[BoundaryModel]` + `IEventBoundary<T>` API. All 6 write handlers updated to tag events. 31/31 tests (+1 DCB concurrency test). ADR 0058 updated. Research doc committed.
- **S2 (Documentation Closure):** Skill doc rewrite, CONTEXTS.md assessment, README accuracy pass + Mermaid diagram update, milestone closure retrospective, CURRENT-CYCLE.md update.

**Final Build State:** 0 errors, 19 warnings (unchanged from M39.0 baseline)
**Final Test Count:** 31/31 Promotions integration tests passing

---

## What M40.0 Delivered

### S1 — The Architecture

One atomic redemption decision replaced the two-command fan-out pattern. Key deliverables:

- `CouponRedemptionState` as the boundary model projecting from both Coupon and Promotion streams
- `RedeemCouponHandler` with `Load()` / `Before()` / `Handle()` compound pattern validating coupon status, promotion status, and usage cap in one pass
- `RecordPromotionRedemptionHandler` converted from command handler to choreography consumer reacting to `CouponRedeemed`
- `RedeemCoupon` command extended with `PromotionId` (required for loading both streams)

### S1B — The Genuine Implementation

S1B replaced S1's manual `LoadAsync` workaround with Marten 8.28.0's real tag-based DCB API:

- **Strong-typed tag IDs:** `CouponStreamId(Guid Value)` and `PromotionStreamId(Guid Value)` — wrapper records required because `Guid` has 2 public instance properties in .NET 10
- **All 7 write handlers tag events at write time:** `BuildEvent()` + `AddTag()` + `Append()` in IssueCoupon, RevokeCoupon, RedeemCoupon, CreatePromotion, ActivatePromotion, GenerateCouponBatch, and RecordPromotionRedemption
- **`EventTagQuery` spanning two streams:** Queries `CouponStreamId` for Coupon events and `PromotionStreamId` for Promotion events
- **`[BoundaryModel] IEventBoundary<CouponRedemptionState>`:** Cross-stream optimistic concurrency via `AssertDcbConsistency`
- **`DcbConcurrencyException` retry policy:** Added alongside `ConcurrencyException` (they are siblings, not parent-child)
- **DCB concurrency integration test:** Proves that after one `CouponRedeemed` is appended, a concurrent redemption attempt is rejected by the boundary state

---

## What Was Learned About Marten's DCB API

The tag-based DCB API (`EventTagQuery` + `[BoundaryModel]` + `IEventBoundary<T>`) required an extra session (S1B) because the tagging constraint was not obvious from Wolverine/Marten documentation alone. The critical discovery: tag tables (`mt_event_tag_*`) are populated **only** when events are explicitly tagged at write time via `BuildEvent()` + `AddTag()`. Standard write patterns (`[WriteAggregate]`, `IStartStream`, raw `session.Events.Append(streamId, rawEvent)`) do NOT populate tag tables.

This meant S1's initial implementation — which used `LoadAsync` + `AggregateStreamAsync` to manually load both streams — worked functionally but lacked the cross-stream optimistic concurrency that the real DCB API provides. Understanding this required reading the Marten source code (documented in `docs/research/marten-dcb-tagging-mechanics.md`).

Additional non-obvious findings:
- `StartStream` re-wraps `IEvent` objects, losing tags — must use `Append` instead
- `EventTagQuery.For(tag)` without `.AndEventsOfType<>()` creates no condition and throws at runtime
- `[BoundaryModel]` on both `Before()` and `Handle()` causes CS0128 in Wolverine codegen
- Boundary models need `public Guid Id { get; set; }` (Marten treats them as documents)

---

## What CritterSupply Now Demonstrates

**Before M40.0:** No DCB example anywhere in the codebase. The skill doc contained planning guidance and candidate analysis but no working reference.

**After M40.0:** A complete, working DCB reference covering:
- Strong-typed tag ID records (`CouponStreamId`, `PromotionStreamId`)
- Tag type registration with aggregate binding (`RegisterTagType<T>().ForAggregate<T>()`)
- Tagged write handlers across all stream types in the BC
- `EventTagQuery` multi-stream boundary construction
- `[BoundaryModel] IEventBoundary<T>` for atomic append with cross-stream concurrency
- `AssertDcbConsistency` enforcing optimistic concurrency across the tag boundary
- `DcbConcurrencyException` retry policy alongside `ConcurrencyException`
- Choreography consequence handler (`RecordPromotionRedemptionHandler`) with proper tagging
- DCB concurrency integration test as proof of cross-stream consistency

---

## README and Mermaid Diagrams

### Assessment

The README contained two Mermaid diagrams:
1. **Customer-Facing Flow** — showed 12 BCs and their integration arrows
2. **Vendor & Operations Flow** — showed 10 BCs (5 identity/portal + 5 shared business)

**Findings:**

*Customer-Facing Flow:*
- **Missing integration arrow:** Shopping → Promotions (ValidateCoupon/CalculateDiscount queries, added M30.1). Added a dotted arrow for this integration.
- **Missing integration arrows:** Returns and Payments publish events consumed by Correspondence (M31.0). Added dotted arrows for Returns → Correspondence and Payments → Correspondence.
- All BC names and descriptions were accurate. Listings and Marketplaces are correctly absent — they have no customer-facing interactions.

*Vendor & Operations Flow:*
- **Missing BCs:** Listings and Marketplaces — Backoffice has admin pages for both (added M36.1). Added both to the diagram.
- **Missing BC:** Returns — Backoffice has ReturnManagement page (added M33.0). Added Returns to the diagram.
- **Missing BC:** Correspondence — Backoffice has correspondence history views. Added Correspondence to the diagram.

### Third Diagram Decision

A third diagram showing the DCB pattern was considered — specifically the `EventTagQuery` boundary spanning Coupon and Promotion streams with `CouponRedemptionState`, `AssertDcbConsistency`, and the choreography path to `RecordPromotionRedemptionHandler`.

**Decision: Not added.** The DCB pattern is an internal implementation detail of the Promotions BC. It does not change the BC's external behavior or integration arrows. The skill doc (`docs/skills/dynamic-consistency-boundary.md`) and research doc (`docs/research/marten-dcb-tagging-mechanics.md`) provide sufficient detail for developers implementing DCB. A Mermaid diagram in the README would add complexity without communicating something the existing diagrams don't — README diagrams show BC interactions, not internal handler patterns.

---

## CONTEXTS.md Assessment

The Promotions entry in CONTEXTS.md was reviewed for accuracy after the DCB refactor. **Verified, no changes needed.** The entry describes the BC's responsibilities and communication partners correctly. The DCB refactor was an internal implementation change — the BC's external behavior (redemption workflow, Shopping queries, planned Pricing integration) is unchanged. The entry contains no implementation details like `session.Events.Append()` or handler patterns.

---

## Inherited by Next Milestone

Carried forward from M39.0:
1. **Returns cross-BC saga tests (6 skipped):** Re-evaluate at Wolverine 6.x — both `InvokeAsync()` and `TrackActivity()` approaches failed on 5.27.0
2. **Vendor Portal cold-start test flakes:** 56/86 fail on first container run, all pass on retry
3. **eBay orphaned draft background sweep:** Detection in place, cleanup deferred

Resolved from M39.0 inherited items:
- `ActivatePromotionHandler` return type — no longer relevant after S1B refactored all Promotions handlers to use tagged `Append` patterns
