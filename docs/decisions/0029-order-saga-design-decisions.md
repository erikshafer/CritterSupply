# ADR 0029: Order Saga Design Decisions

**Status:** ✅ Accepted

**Date:** 2026-03-13

**Cycle:** 20

## Context

The Order saga is the most complex piece of CritterSupply's event-driven architecture. It coordinates three downstream bounded contexts (Inventory, Payments, Fulfillment) across a workflow that spans minutes to hours, handling confirmations, failures, compensation chains, cancellations, and time-based lifecycle events (return window).

During implementation and subsequent hardening, the team made several significant design decisions that warrant explicit documentation. Without capturing the "why," these decisions look arbitrary in code review and are likely to be undone by well-meaning future contributors.

This ADR documents the four most consequential design choices made in the Order saga implementation.

## Decision 1: Document-Based Saga, Not Event-Sourced

### Decision

The `Order` saga is persisted as a **mutable JSON document** in Marten (using `opts.Schema.For<Order>().UseNumericRevisions(true)`), **not** as an event-sourced stream.

### Rationale

A saga's purpose is orchestration: it answers "what do I do next?" based on current state. It is not primarily a historical record. Storing saga state as a document gives direct access to current state — no event replay required per message received.

The Order saga processes messages frequently and in parallel. With an event-sourced saga:
- Every `Handle()` call would need to replay the event stream before processing
- Projection latency could delay orchestration decisions
- The event stream would grow proportionally to the number of messages processed, creating storage and replay overhead for a write-heavy, read-light object

The individual bounded contexts (Inventory event streams, Payment event streams, Shipment event streams) already maintain the full audit trail with immutable history. The Order saga consuming from them does not need to duplicate that history — it needs to know *current coordination state* efficiently.

**Key insight:** The saga's value is coordination velocity, not history. The history lives in the downstream BCs.

### Alternatives Considered

**Event-sourced saga:** Would provide a replay-able audit of every coordination decision. Rejected because: (a) replay overhead on every message, (b) the audit trail is already provided by downstream BC event streams, (c) Marten's document store with numeric revisions provides the optimistic concurrency we need without event sourcing overhead.

**In-memory saga (no persistence):** Rejected because: (a) saga state is lost on restart, (b) at-least-once delivery requires durable state to enforce idempotency, (c) long-running workflows (30-day return window) require durable scheduled messages.

### Consequences

- Order saga state is always immediately available without replay
- Full audit trail of payment, inventory, and fulfillment decisions lives in downstream BC event streams — accessible via cross-BC queries if needed
- Numeric revisions (`UseNumericRevisions(true)`) provide optimistic concurrency, requiring retry configuration for `ConcurrencyException`
- Saga documents accumulate in Marten until `MarkCompleted()` is called — discipline required to ensure all terminal paths call it

## Decision 2: Separate `PlaceOrderHandler` for Saga Initialization

### Decision

Saga initialization is performed in a **separate static `PlaceOrderHandler` class**, not in the `Order` saga class itself. `PlaceOrderHandler.Handle(CheckoutCompleted)` returns `(Order, OrderPlaced)`, which Wolverine recognizes as the pattern for starting a new saga document.

### Rationale

The `Order` saga class is responsible for state transitions — evolving from one known state to another in response to integration events. If initialization logic lives in the saga class, the class serves two distinct purposes: construction and state machine coordination. These are different concerns with different change drivers.

Separating them provides:

1. **Clarity of intent:** The saga's `Handle()` methods all operate on *existing* saga state. There is no ambiguity about which methods run on a new instance vs. an existing one.

2. **Testability:** `PlaceOrderHandler` can be tested independently of the saga's state machine, and the saga's `Handle()` methods can be tested with pre-constructed `Order` instances without any initialization ceremony.

3. **Delegate to Decider:** Both the handler and the saga's Handle methods delegate to `OrderDecider` pure functions. This is only possible if initialization is in a separate class that can also call `OrderDecider.Start()`.

4. **Wolverine convention alignment:** Wolverine's tuple return `(SagaType, ...)` pattern for starting sagas is naturally expressed in a standalone handler class. Placing it on the saga class would make the initialization method feel inconsistent with the saga's other methods.

### Alternatives Considered

**Initialization in `Order.Start(CheckoutCompleted)` static factory:** Cleaner from an OOP perspective (factory method on the type). Rejected because Wolverine's handler discovery treats all `Handle`/`Load`/etc. methods on a saga class as operating on *existing* instances. A static factory on the saga would require additional naming convention gymnastics.

**Initialization in the HTTP layer:** The `CompleteCheckout` endpoint already exists and could directly create the `Order` saga. Rejected because: (a) the trigger for saga creation is a `CheckoutCompleted` integration message, not a direct HTTP call — the saga starts asynchronously, (b) keeping the saga start in a message handler ensures it is durable (inbox/outbox guaranteed), not at-risk from HTTP timeout failures.

### Consequences

- Handler discovery must use `IncludeAssembly()`, not `IncludeType<Order>()`, to ensure `PlaceOrderHandler` is discovered alongside `Order`
- The `[assembly: WolverineModule]` attribute in `AssemblyAttributes.cs` is required for `IncludeAssembly` to work correctly
- Code reviewers must understand that `PlaceOrderHandler` is the saga's entry point — its separation is intentional, not an oversight

## Decision 3: Decider Pattern with Separate Static `OrderDecider` Class

### Decision

All business logic for Order saga state transitions is implemented in a **static `OrderDecider` class** containing pure functions. The saga's `Handle()` methods are thin adapters: call the decider, apply the `OrderDecision`, return outgoing messages.

### Rationale

The naive alternative puts business logic directly in the saga's `Handle()` methods. This makes the logic only testable through integration tests (requiring a full Wolverine + Marten stack), and conflates orchestration mechanics with business rules.

The Decider pattern provides:

1. **Unit testability:** `OrderDecider.HandlePaymentCaptured(order, message, timestamp)` is a pure function. Given an `Order` state object, an incoming message, and a timestamp, it returns an `OrderDecision`. No Marten, no Wolverine, no test fixtures required. The entire business rule suite can run in milliseconds.

2. **Time control:** All decider functions accept `DateTimeOffset timestamp` as a parameter. Tests can inject a known timestamp to verify time-dependent behavior (e.g., `PlacedAt` is set correctly, return window duration is applied).

3. **Single responsibility:** The `Order` class owns serializable state and Wolverine handler wiring. `OrderDecider` owns business logic. Neither bleeds into the other.

4. **Explicit state transitions:** The `OrderDecision` record makes state transitions visible as data, not as mutation side effects buried in a `Handle()` method. Code review is easier when "what changed" is expressed in a returned value.

The `OrderDecision` record uses nullable fields (`OrderStatus? Status`, `bool? IsPaymentCaptured`, etc.) to express "no change" — if a decision doesn't change a field, it's null, and the saga's adapter code skips it. This avoids the alternative of returning a complete new `Order` state on every decision (which would require the decider to know about fields it doesn't care about).

### Alternatives Considered

**Business logic directly in `Order.Handle()` methods:** Simpler file structure. Rejected because: (a) only testable via integration tests, (b) makes the saga class a god object, (c) harder to review because mutation and decision-making are interleaved.

**Business logic in a `IOrderService` injected into saga:** Would allow dependency injection (e.g., for clocks). Rejected because: (a) pure functions are simpler than interfaces, (b) time is already injected as a parameter, (c) sagas should not depend on DI services — this couples them to the IoC container and makes testing harder, not easier.

**Event sourcing the decisions (returning domain events):** Each `Handle()` could return events like `PaymentCaptureAcknowledged`, `InventoryReservationTracked`, etc. Rejected because: (a) these would be internal coordination events with no external meaning, (b) this adds event-sourced overhead to a document-backed saga, (c) the downstream BCs already produce the authoritative domain events.

### Consequences

- All Order saga business logic can be unit-tested without infrastructure
- `OrderDecision` record must be updated when new saga state fields are introduced
- Developers unfamiliar with the Decider pattern may find the indirection surprising — the `docs/skills/wolverine-sagas.md` skill document explains it
- The pattern creates two layers (saga adapter + decider) where naive implementations have one — this is an intentional tradeoff for testability and clarity

## Decision 4: `CommittedReservationIds` as `HashSet<Guid>` (vs. `int` counter)

### Decision

Inventory commitment tracking uses a **`HashSet<Guid> CommittedReservationIds`** as the authoritative source of truth. The `CommittedReservationCount` property is computed from `CommittedReservationIds.Count`, not stored separately.

### Rationale

The initial implementation stored a `CommittedReservationCount` integer alongside `CommittedReservationIds`. This created two sources of truth that could drift if a bug caused one to be updated without the other.

The `HashSet<Guid>` approach solves three problems simultaneously:

1. **Idempotency:** `HashSet<Guid>` naturally prevents double-counting. The idempotency guard `if (current.CommittedReservationIds.Contains(message.ReservationId)) return new OrderDecision()` is the correct check — and `Contains` is O(1) on a `HashSet`.

2. **Correctness:** The multi-SKU race condition fix causes `HandleReservationConfirmed` to re-issue commit requests for all previously confirmed reservations. Without a set-based guard, duplicate `ReservationCommitted` messages would increment the count beyond `ExpectedReservationCount`, triggering premature fulfillment dispatch.

3. **No drift:** `CommittedReservationCount => CommittedReservationIds.Count` is computed, never stored. There is exactly one place where committed reservations are tracked. Any bug that fails to add to the set will also fail the derived count — the failure is visible rather than hidden by a stale counter.

An `int` counter with a separate idempotency check (e.g., `if (current.ProcessedCommits.Contains(id))`) would work but introduces two data structures. A single `HashSet` is simpler and eliminates the drift risk entirely.

Marten serializes `HashSet<Guid>` as a JSON array, which round-trips correctly. The slightly larger storage footprint (storing GUIDs vs. an integer) is acceptable for the correctness guarantee.

### Alternatives Considered

**`int CommittedReservationCount` (stored) with separate `HashSet<Guid>` idempotency set:** Both structures needed. Rejected because of drift risk — two sources of truth for the same fact.

**`int CommittedReservationCount` only (no set):** Simple. Rejected because idempotency cannot be enforced without knowing *which* reservations were already counted. An integer alone cannot distinguish "this is a new commit" from "this is a duplicate."

**`List<Guid>` instead of `HashSet<Guid>`:** Would work for correctness but O(n) for `Contains`. At realistic order sizes (1–20 SKUs) the difference is negligible, but `HashSet` expresses intent more clearly.

### Consequences

- `CommittedReservationCount` must never be stored as a separate property — code review should reject any change that introduces a stored count alongside `CommittedReservationIds`
- Marten serializes `HashSet<Guid>` as a JSON array; deserialization is handled correctly by Marten's System.Text.Json integration
- Unit tests for idempotency can verify behavior by pre-populating `CommittedReservationIds` and asserting that duplicate messages produce no state change

## References

- [`src/Orders/Orders/Placement/Order.cs`](../../src/Orders/Orders/Placement/Order.cs) — Saga implementation
- [`src/Orders/Orders/Placement/OrderDecider.cs`](../../src/Orders/Orders/Placement/OrderDecider.cs) — Pure business logic
- [`src/Orders/Orders/Placement/PlaceOrderHandler.cs`](../../src/Orders/Orders/Placement/PlaceOrderHandler.cs) — Saga initialization handler
- [`docs/skills/wolverine-sagas.md`](../skills/wolverine-sagas.md) — Comprehensive skill document
- [Wolverine Saga Documentation](https://wolverinefx.net/guide/durability/marten/sagas.html)
- [Decider Pattern (Jérémie Chassaing)](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider)
- [Process Manager Pattern (EIP)](https://www.enterpriseintegrationpatterns.com/patterns/messaging/ProcessManager.html)
