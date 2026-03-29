# ADR 0040: `*Requested` Integration Event Convention

**Status:** ✅ Accepted

**Date:** 2026-03-28

**Context:**

CritterSupply uses integration messages (defined in `src/Shared/Messages.Contracts/`) for cross-BC communication via RabbitMQ. Most integration messages use past-tense naming to describe facts that have occurred (e.g., `OrderPlaced`, `PaymentCaptured`, `InventoryAdjusted`). However, a subset of integration messages represent **command-intent**: they ask a downstream BC to perform an action rather than announcing a fact.

These command-intent messages use the `*Requested` suffix. Without a documented convention, this naming pattern could be confused with domain events or arbitrarily changed during naming audits.

**Decision:**

The `*Requested` suffix is a deliberate naming convention for integration messages that carry **command intent** — messages sent from one bounded context to another asking it to perform an action.

### The Convention

- **`*Requested` suffix** signals a command-intent integration message sent from one BC to another
- The sender publishes the message using **choreography** (RabbitMQ pub/sub), not Wolverine's internal command routing
- The receiver treats the message as an instruction to act, not a notification of something that already happened

### What It Is Not

- **Not a past-tense domain event.** Domain events use `*ed` / `*Created` / `*Completed` / `*Failed` suffixes (e.g., `OrderPlaced`, `PaymentCaptured`, `RefundCompleted`)
- **Not an internal command.** Internal commands within a single BC use imperative verb naming without the `Requested` suffix (e.g., `RequestPayment`, `RequestRefund`, `CancelOrder`)
- **Not a query.** Queries are read-only operations that do not change state

### Canonical Examples

| Message | Publisher | Subscriber | Purpose |
|---------|-----------|------------|---------|
| `FulfillmentRequested` | Orders | Fulfillment | Ask Fulfillment BC to begin shipping process |
| `ReservationCommitRequested` | Orders | Inventory | Ask Inventory BC to hard-commit a soft reservation |
| `ReservationReleaseRequested` | Orders | Inventory | Ask Inventory BC to release a held reservation |
| `RefundRequested` | Orders | Payments | Ask Payments BC to issue a refund |

All four messages originate from the Orders BC's saga orchestration and are defined in `src/Shared/Messages.Contracts/`.

### Why Not Use Wolverine Routed Commands?

Wolverine supports routed commands that could replace these messages with direct command routing (e.g., `bus.InvokeRemoteAsync<RequestFulfillment>(...)`). The `*Requested` convention is a deliberate choice for now because:

1. **Loose coupling:** The sender does not need to know the receiver's internal command type. The contract is the integration message shape, not the handler's input type.
2. **Multiple subscribers:** RabbitMQ pub/sub allows multiple BCs to subscribe to the same message (e.g., both Fulfillment and Correspondence could subscribe to `FulfillmentRequested`).
3. **Proven pattern:** The convention has been stable since M28.0 with zero naming collisions.

### Future Evolution

Converting `*Requested` messages to Wolverine routed commands is a candidate for a future milestone if:
- A BC needs request/response semantics (not just fire-and-forget)
- The message has exactly one receiver (no fan-out needed)
- The tight coupling is acceptable for that specific integration

This conversion would be a deliberate architectural decision (new ADR) per integration point, not a blanket migration.

**Rationale:**

- Naming conventions reduce cognitive load: developers can immediately distinguish command-intent (`*Requested`) from fact-reporting (`*ed`) integration messages
- The convention prevents naming collisions between internal commands and integration messages (e.g., internal `RequestRefund` vs. integration `RefundRequested`)
- Documenting the convention prevents future naming audits from incorrectly "fixing" these names

**Consequences:**

- **Positive:** Clear naming taxonomy for all integration messages. New integration messages follow a predictable pattern.
- **Positive:** Internal commands (e.g., `RequestPayment`, `RequestRefund`) and integration events (e.g., `RefundRequested`) are unambiguously distinguished by name.
- **Negative:** The `*Requested` suffix is slightly awkward grammatically — it reads as past tense but carries imperative intent. This is an acceptable trade-off for consistency.

**Alternatives Considered:**

1. **Use imperative naming for integration messages too** (e.g., `RequestFulfillment` instead of `FulfillmentRequested`): Rejected because it creates naming collisions with internal commands and blurs the boundary between intra-BC and inter-BC communication.
2. **Use Wolverine routed commands directly:** Deferred — adds tight coupling that is not needed for the current pub/sub integration pattern.

**References:**

- `src/Shared/Messages.Contracts/Fulfillment/FulfillmentRequested.cs`
- `src/Shared/Messages.Contracts/Orders/ReservationCommitRequested.cs`
- `src/Shared/Messages.Contracts/Orders/ReservationReleaseRequested.cs`
- `src/Shared/Messages.Contracts/Payments/RefundRequested.cs`
- M36.0 Track C naming audit (C-1, C-2, C-3 renames of internal commands)
- `docs/skills/integration-messaging.md`
