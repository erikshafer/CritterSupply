# M43.0 — Slice 12 (`OrderPlacedHandler`) Retirement — Retrospective

> **Date:** 2026-05-06 · **Source:** [Plan](./m43-0-plan.md) · **Item:** **1A** from `docs/research/state-of-repo-2026-05.md` §4
> **Next:** Item **1B** — concurrency-exhaustion gap #13 (next session, `M43.1`).

## Outcome

✅ **Slice 12 complete.** The legacy in-process `Inventory.OrderPlacedHandler` is
retired. The routing-aware reservation flow now runs end-to-end over RabbitMQ
across **Orders → Fulfillment → Inventory → Orders**.

## What shipped

- **Fulfillment** — `FulfillmentRequestedHandler` now returns
  `OutgoingMessages` and emits one
  `Messages.Contracts.Fulfillment.StockReservationRequested` per line item with
  the routing-engine-selected `WarehouseId` and a unique
  `Guid.CreateVersion7()` `ReservationId`. Idempotency: existing-stream guard
  short-circuits with empty `OutgoingMessages` (no re-emit on duplicate
  delivery).
- **Fulfillment.Api** — `PublishMessage<StockReservationRequested>().ToRabbitQueue("inventory-fulfillment-events")`.
- **Inventory** — `StockReservationRequestedHandler` is now self-contained
  (no `Before`/`ProblemDetails`): emits `ReservationFailed` on unknown
  inventory or insufficient stock; idempotency guard via
  `inventory.Reservations.ContainsKey(message.ReservationId)` prevents
  double-reserve / double-confirm / double-expiry-schedule on at-least-once
  redelivery.
- **Inventory.Api** — both `ReservationConfirmed` and `ReservationFailed`
  publish to a new `orders-inventory-events` queue.
- **Inventory** — `OrderPlacedHandler.cs` deleted.
- **Tests (Inventory)** — `OrderPlacedFlowTests.cs` deleted; coverage
  migrated and expanded onto `StockReservationRequestedTests.cs`
  (5 scenarios: happy / insufficient / unknown / duplicate / multi-SKU).
- **Tests (Fulfillment)** — `FulfillmentRequestedHandlerTests.cs` extended
  with two new scenarios proving the per-line-item emission and the
  duplicate-`FulfillmentRequested` no-re-emit guard.
- **Orders.Api** — listens on `orders-inventory-events` (mirrors the
  `orders-fulfillment-events` and `orders-returns-events` idioms).
- **Orders** — `OrderDecider.cs` comment refreshed; `Orders.Api/README.md`
  and `Inventory.Api/README.md` integration tables, sequence diagrams, and
  status tables refreshed.
- **CONTEXTS.md** — Inventory ↔ Fulfillment row now lists
  `StockReservationRequested` as the routing-informed trigger.
- **docs/features/inventory/routing-integration.feature** — Phase 1 (dual-publish
  bridge) scenario removed; "Phase 2 — legacy handler removed" is now the
  active spec.
- **docs/planning/CURRENT-CYCLE.md** — Quick Status, Active Milestone, and
  M42.x carryover bullets refreshed.

## Verification

- `dotnet build` — **0 errors**, 351 warnings (down from 358 baseline; net
  reduction from deletions).
- Inventory: 109/109 integration + 151/151 unit ✅
- Fulfillment: 80/80 integration + 40/40 unit ✅
- Orders: 55/55 integration + 144/144 unit ✅
- QA agent (`@qa-engineer`) reviewed and approved as mergeable; build/test
  spot-checked. Findings captured below.

## QA findings — disposition

**SHOULD-FIX (deferred to follow-up, not blocking merge):**

1. **`ReservationFailed` redelivery is not deduplicated on the Inventory
   side.** A redelivered insufficient-stock or unknown-SKU
   `StockReservationRequested` will re-publish `ReservationFailed`. Standard
   at-least-once-with-idempotent-consumer contract; the Orders saga's
   terminal-state guards in `HandleReservationFailed` already prevent
   double-compensation. **Action:** add a regression test on the saga side
   in a follow-up cycle.
2. **No explicit assertion that `ExpireReservation` is not re-scheduled on
   duplicate delivery.** Proven by code inspection (early-return precedes
   the `outgoing.Delay(...)`); a direct assertion would harden against
   future refactors. **Action:** one-line addition to
   `Duplicate_ReservationId_Is_Idempotent` in a follow-up.
3. **No end-to-end `POST /api/checkouts/{id}/complete` →
   `InventoryReserved` test that exercises the new `orders-inventory-events`
   listener.** Per-handler tests + `OrderDecider` unit tests already cover
   the slices; the new wiring is structurally validated by `Program.cs`.
   **Action:** add a saga-listener integration test in a follow-up.
4. **Multi-SKU at *different* warehouses is uncovered.** Theoretical today
   (the routing engine returns one FC per region), but cheap insurance for
   future routing-engine changes that split across FCs. **Action:** defer.

**DEFERRED / NICE-TO-HAVE:**

- `docs/workflows/inventory-workflows.md` and
  `docs/workflows/orders-workflows.md` still describe `Orders → Inventory:
  OrderPlaced` as the trigger. Plan §3.D4 listed these for refresh; not
  blocking — flagged for the next docs-only sweep.
- Minor staleness in `Orders.Api/README.md` line 9 ("local queues" wording)
  and the queue/local-queues column of the Published table — partially
  refreshed but could be tightened further.

## Carryover into next session (M43.1 — Item 1B)

- `ConcurrencyException → RetryOnce → RetryWithCooldown → Discard` policy
  silently drops messages; should terminate in `MoveToErrorQueue()` now that
  the DLQ log sink exists. Documented in
  `inventory-remaster-s2-retrospective.md` and as Gap #13 in the inventory
  remaster register.
- The four QA SHOULD-FIX items above can be picked up alongside 1B if there
  is bandwidth, since two of them are saga-side tests in Orders that mesh
  naturally with the DLQ work.
