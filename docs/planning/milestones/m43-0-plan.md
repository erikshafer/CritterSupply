# M43.0 — Slice 12: `OrderPlacedHandler` Retirement (Plan)

> **Author:** Principal Architect
> **Date:** 2026-05-06
> **Scope:** Top-tier item **1A** from `docs/research/state-of-repo-2026-05.md` §4.
> **Source ADR:** [0060 — Inventory BC Remaster Rationale](../../decisions/0060-inventory-bc-remaster-rationale.md), §1
> **Carryover from:** `inventory-remaster-s4-retrospective.md` §4 (the only ⛔ on the Inventory remaster scoreboard)
> **Out of scope (next session):** Item **1B** — concurrency-exhaustion gap #13.

---

## 1. Goal

Retire the legacy **`OrderPlacedHandler`** in Inventory and finish wiring the
routing-aware **`StockReservationRequested`** flow end-to-end across **Orders →
Fulfillment → Inventory → Orders** so that:

- Fulfillment owns the routing decision (which warehouse).
- Inventory only ever reserves stock at a routing-informed `WarehouseId`
  (no more hardcoded `WH-01`).
- The dual-publish migration bridge from M42.x is removed.
- All deployed-path messages (`StockReservationRequested`, `ReservationConfirmed`,
  `ReservationFailed`) flow over RabbitMQ between BCs and are covered by tests.

This is a **three-BC coordinated change** (Orders, Fulfillment, Inventory).
No new business behavior — the remaster decision is already made; this is
execution + cleanup + test reinforcement.

---

## 2. Current state (verified read of code 2026-05-06)

| Concern | Today | Target |
|---|---|---|
| Trigger for Inventory reservation | `Inventory.OrderPlacedHandler.Handle(OrderPlaced)` — fans out `ReserveStock` per SKU at hardcoded `WH-01` (in-process Wolverine handler discovery only; **not** wired over RabbitMQ in any `Program.cs`). | `Fulfillment.FulfillmentRequestedHandler` sends `StockReservationRequested` per SKU at the routing-engine-selected FC over RabbitMQ. Inventory's `StockReservationRequestedHandler` already exists and reserves correctly. |
| `StockReservationRequested` publishing | Type exists in `Messages.Contracts.Fulfillment`. `FulfillmentRequestedHandler` does **not** emit it. `Fulfillment.Api/Program.cs` has **no** `PublishMessage<StockReservationRequested>` route. | Emitted by `FulfillmentRequestedHandler` per SKU after FC assignment; published to `inventory-fulfillment-events` queue (which Inventory already listens on). |
| `ReservationConfirmed` / `ReservationFailed` routing to Orders | `StockReservationRequestedHandler` and `ReserveStockHandler` add them to `OutgoingMessages`, but `Inventory.Api/Program.cs` has **no** `PublishMessage<...>` route, and `Orders.Api/Program.cs` has **no** matching `ListenToRabbitQueue`. | Inventory publishes both to `orders-inventory-events`; Orders listens on it and processes inline (same idiom as `orders-fulfillment-events` and `orders-returns-events`). |
| Idempotency for routing-driven reservations | `StockReservationRequestedHandler` has no idempotency guard against duplicate delivery of the same `ReservationId`. | Add a `Before`/load-side check that no-ops if the reservation already exists on the aggregate (prevents double-reserve under at-least-once delivery). |
| Inventory `OrderPlacedHandler` | Lives in `src/Inventory/Inventory/Management/OrderPlacedHandler.cs`. Marked "MIGRATION BRIDGE … Remove after Orders saga sends FulfillmentRequested before reservation (M42.x S4)". | Deleted. |
| Inventory tests for OrderPlaced flow | `tests/Inventory/Inventory.Api.IntegrationTests/Management/OrderPlacedFlowTests.cs` (3 tests). | Removed; equivalent multi-SKU/duplicate-SKU coverage migrated onto `StockReservationRequestedHandler` (extending `RoutingIntegrationTests` if present, or net-new `StockReservationRequestedHandlerTests.cs`). |
| Docs (CONTEXTS.md, BC READMEs, routing-integration.feature, workflows) | Still describe `OrderPlaced → Inventory` as the primary trigger and the dual-publish bridge as active. | Describe `FulfillmentRequested → StockReservationRequested → ReservationConfirmed/Failed` as the only path. The "Migration Phase 2" scenario already exists in `routing-integration.feature` — light edits only. |

> **Discovery note:** While reading the code I confirmed that `OrderPlaced` is
> currently published only to `storefront-notifications` from `Orders.Api`, and
> that `Inventory.Api` does **not** subscribe to it via RabbitMQ. The "dual-publish
> bridge" comment on `OrderPlacedHandler` is therefore in-process-only today.
> Production routing of reservations was effectively unfinished at the end of
> M42.x; this plan completes that wiring at the same time it removes the legacy
> handler. This expands Slice 12 slightly beyond the literal "delete handler"
> scope — but it's the minimum viable end-to-end correctness.

---

## 3. Deliverables

Grouped by BC, ordered for safe rollout (Fulfillment first publishes, Inventory
listens & responds, Orders subscribes to responses, then legacy handler is
deleted).

### D1. Fulfillment BC — emit `StockReservationRequested`

- `src/Fulfillment/Fulfillment/Shipments/FulfillmentRequestedHandler.cs`
  - After `FulfillmentCenterAssigned`, build one `Messages.Contracts.Fulfillment.StockReservationRequested` per line item using the assigned FC.
  - Use `Guid.CreateVersion7()` for each `ReservationId`.
  - Return via `OutgoingMessages` (the handler currently returns `Task` — change to `Task<OutgoingMessages>` to remain idiomatic).
  - Idempotency: when the existing-stream guard fires (duplicate `FulfillmentRequested`), do **not** re-emit reservation requests — return empty `OutgoingMessages`.
- `src/Fulfillment/Fulfillment.Api/Program.cs`
  - Add `opts.PublishMessage<Messages.Contracts.Fulfillment.StockReservationRequested>().ToRabbitQueue("inventory-fulfillment-events");`

### D2. Inventory BC — publish `ReservationConfirmed` / `ReservationFailed`; harden `StockReservationRequestedHandler`; delete legacy handler

- `src/Inventory/Inventory.Api/Program.cs`
  - Add explicit RabbitMQ publication routes for both contracts to a new `orders-inventory-events` queue (mirrors `orders-fulfillment-events`, `orders-returns-events`).
  - Keep `inventory-fulfillment-events` listener as is (already wired in M42.x).
- `src/Inventory/Inventory/Management/StockReservationRequestedHandler.cs`
  - Add idempotency guard in `Before(...)`: if the aggregate already contains a reservation with the same `ReservationId`, return `WolverineContinue.NoProblems` and skip in `Handle(...)` (or short-circuit by returning a no-op `OutgoingMessages`). Pattern reference: existing `FulfillmentRequestedHandler` `FetchStreamStateAsync` pre-check.
  - When `Before` decides "insufficient stock", convert to publishing `Messages.Contracts.Inventory.ReservationFailed` via `OutgoingMessages` instead of returning a `ProblemDetails` (HTTP semantics don't apply to a queued integration message).
- `src/Inventory/Inventory/Management/OrderPlacedHandler.cs`
  - **Delete file.**
- `src/Inventory/Inventory.Api/README.md`
  - Replace `Orders → Inventory: OrderPlaced` references with the routing-aware flow. Update the integration table.

### D3. Orders BC — subscribe to inventory events; stop documenting the legacy path

- `src/Orders/Orders.Api/Program.cs`
  - Add `opts.ListenToRabbitQueue("orders-inventory-events").ProcessInline();` (mirrors the other two `orders-*-events` listeners).
  - **No** removal needed for "Orders publishing OrderPlaced to Inventory" — that route never existed in `Program.cs`. Confirm nothing else publishes `Messages.Contracts.Orders.OrderPlaced` to a queue Inventory listens on.
- `src/Orders/Orders.Api/README.md`
  - Remove the "Orders → Inventory: OrderPlaced" entries from the diagrams and the integration table.
- `src/Orders/Orders/Placement/OrderDecider.cs` line 46 comment ("Inventory BC creates one reservation per distinct SKU in OrderPlacedHandler") — update wording to reflect Fulfillment-driven reservation.

### D4. Documentation

- `CONTEXTS.md` — Inventory section: replace "subscribes to `Orders.OrderPlaced`" with "subscribes to `Fulfillment.StockReservationRequested`". Update integration arrows.
- `docs/features/inventory/routing-integration.feature` — mark the "Migration Phase 1 (dual-publish bridge)" scenario as `@deprecated` or remove; ensure "Migration Phase 2 — legacy handler removed" is kept and is now the active spec.
- `docs/workflows/inventory-workflows.md` and `docs/workflows/orders-workflows.md` — refresh sequence diagrams to omit `OrderPlaced → Inventory`.
- `docs/planning/CURRENT-CYCLE.md` — add M43.0 Recent Completions entry; refresh Quick Status; close the Slice 12 carryover bullet from the M42.4 retro.

### D5. Tests (QA agent ownership — see §5)

A separate `@qa-engineer` pass is part of this milestone (see §5 below). At a
minimum the following test changes are expected:

- **Inventory:** Delete `OrderPlacedFlowTests.cs`. Add or extend
  `StockReservationRequestedHandlerTests.cs` (Inventory.Api.IntegrationTests)
  to cover:
  - happy path (single SKU, routing-informed FC),
  - duplicate-delivery idempotency (same `ReservationId` arrives twice → only one reservation),
  - insufficient stock → `ReservationFailed` published with correct contract fields,
  - multi-SKU per FC (one request per SKU, aggregate per (SKU, WH)).
- **Fulfillment:** Extend `FulfillmentRequestedHandlerTests.cs` to assert that
  one `StockReservationRequested` per line item is on the outgoing message bus
  with the routing-engine-selected `WarehouseId` and a unique `ReservationId`
  per SKU. Cover the duplicate-`FulfillmentRequested` idempotency case (no
  re-emit).
- **Orders:** Add or extend an integration test in `Orders.Api.IntegrationTests`
  asserting the saga transitions to `InventoryReserved` upon receipt of
  `ReservationConfirmed` arriving on the new `orders-inventory-events` queue
  (or via in-process publish — whichever the existing test fixture pattern
  uses).
- **Memory caveat applied:** `tracked.Sent.MessagesOf<T>()` does not capture
  integration messages without configured routes. For Fulfillment tests
  asserting `StockReservationRequested` emission, prefer the route-configured
  test host (so the new `inventory-fulfillment-events` route is wired) or
  assert against the Wolverine outbox, **not** against `tracked.Sent`. Reference:
  `tests/Inventory/Inventory.Api.IntegrationTests/Management/AlertFeedViewTests.cs`.
- **Build + full test sweep** (`dotnet build` then `dotnet test`) at the end —
  expect 0 errors and 0 net new warnings.

---

## 4. Rollout order (single session)

1. **Plan committed** (this file).
2. **D1 Fulfillment** — emit + publish wiring.
3. **D2 Inventory** — outbound `Reservation*` routes; idempotency guard; delete `OrderPlacedHandler`; delete `OrderPlacedFlowTests`.
4. **D3 Orders** — listener wiring + comment/doc fix.
5. **D4 Docs** — CONTEXTS.md, READMEs, feature file, CURRENT-CYCLE.md.
6. **D5 Tests** — extend/add per §3.D5 (with QA agent assist — §5).
7. **Build + full test sweep.**
8. **Retrospective stub** (`docs/planning/milestones/m43-0-retrospective.md`) — only if scope completes; otherwise leave plan intact and document deferred items.

---

## 5. QA verification (`@qa-engineer` collaboration)

Per the user's instruction: pull in the QA agent to double-check the
implementation. The QA pass happens **after** §4 step 5 and **before** the
retrospective. The QA agent is expected to:

1. Confirm that the new test coverage in §3.D5 reflects all the
   business-critical paths (happy / duplicate / insufficient stock / multi-SKU).
2. Identify any test that asserts on the **legacy** `OrderPlaced → ReserveStock`
   path and is now misleading (besides the deleted `OrderPlacedFlowTests`).
3. Verify the Wolverine routing-table memory caveat is handled correctly in any
   new Fulfillment-side assertions.
4. Run `dotnet test` for at minimum the three impacted suites
   (`Inventory.Api.IntegrationTests`, `Fulfillment.Api.IntegrationTests`,
   `Orders.Api.IntegrationTests`) and report green.

---

## 6. Acceptance criteria

- ✅ `src/Inventory/Inventory/Management/OrderPlacedHandler.cs` does not exist.
- ✅ `tests/Inventory/Inventory.Api.IntegrationTests/Management/OrderPlacedFlowTests.cs` does not exist.
- ✅ `Fulfillment.Api/Program.cs` publishes `StockReservationRequested` to `inventory-fulfillment-events`.
- ✅ `Inventory.Api/Program.cs` publishes `ReservationConfirmed` and `ReservationFailed` to `orders-inventory-events`.
- ✅ `Orders.Api/Program.cs` listens on `orders-inventory-events`.
- ✅ `FulfillmentRequestedHandler` emits one `StockReservationRequested` per line item with the routing-assigned `WarehouseId` and a unique `ReservationId`.
- ✅ `StockReservationRequestedHandler` is idempotent on duplicate `ReservationId` and emits `ReservationFailed` (rather than `ProblemDetails`) on insufficient stock.
- ✅ CONTEXTS.md, the impacted BC READMEs, `routing-integration.feature`, workflow diagrams, and `CURRENT-CYCLE.md` are refreshed.
- ✅ `dotnet build` reports 0 errors and no new warnings vs. the M42.4 baseline.
- ✅ All Inventory, Fulfillment, and Orders test suites pass.
- ✅ QA agent review is captured in the retrospective.

---

## 7. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Existing OrderPlacedFlowTests assert behavior that's no longer reproducible. | Delete those tests (their semantics are superseded by `StockReservationRequestedHandler` tests). |
| Other code paths still depend on `Inventory.OrderPlacedHandler` (e.g., test fixtures, in-process Wolverine discovery in shared assemblies). | Compiler will surface them. Integration tests will surface runtime gaps. |
| `tracked.Sent.MessagesOf<T>()` returns empty for `StockReservationRequested` in Fulfillment tests because no route was configured. | Wire the publish route in `Program.cs` *before* running the test (D1 happens before tests). For tests with no broker, assert against the Marten event store or via the Wolverine outbox table. Apply the memory documented in `AlertFeedViewTests.cs`. |
| `ReservationFailed` on insufficient stock arrives back to Orders saga differently than `ProblemDetails` did. | The Orders saga's `HandleReservationFailed` already exists (`OrderDecider.cs`); the failed-reservation contract is unchanged. The change is *which transport* surfaces it (inline outgoing message vs. HTTP problem). |
| Cycle scope creep into Slice 12 stretch (e.g., race-condition retry/reroute on routing miss). | Out of scope. The race condition is documented in `routing-integration.feature` and remains a deferred Phase 3 item. |

---

## 8. Out of scope (next session — item 1B)

- Concurrency-exhaustion gap #13 (`ConcurrencyException → … → Discard` silently
  drops messages). Will be addressed in the next chat session as item **1B**.

---

*End of plan.*
