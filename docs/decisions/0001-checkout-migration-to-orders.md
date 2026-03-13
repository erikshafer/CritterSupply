# ADR 0001: Checkout Migration to Orders BC

**Status:** ✅ Accepted

**Date:** 2026-01-13

**Cycle:** 8

---

## Context

CritterSupply's initial architecture placed the Checkout aggregate inside the Shopping BC alongside the Cart. As the system matured, the team recognized that checkout is fundamentally a transactional concern — distinct from the exploratory, pre-purchase nature of cart management.

Having Checkout in Shopping BC created a blurred boundary: Shopping would have owned both the product browsing/cart experience (pre-purchase) and the payment-commitment flow (transactional). This made the Shopping BC responsible for concerns that span all the way to order fulfillment.

---

## Decision

Migrate the Checkout aggregate from the Shopping BC to the Orders BC.

- **Shopping BC** retains: Cart, product browsing, wishlists (future), search (future)
- **Orders BC** owns: Checkout, Order lifecycle, fulfillment coordination

The integration contract is a published integration event: `Shopping.CheckoutInitiated` (published by Cart when checkout begins) → consumed by Orders BC as `Orders.CheckoutStarted`.

---

## Rationale

- **Workflow split matches intent:** Cart management is exploratory (add, remove, browse); Checkout is a transactional commitment leading to payment and fulfillment. These are naturally different lifecycle phases.
- **Alignment with Orders saga:** The Order saga orchestrates Inventory, Payments, and Fulfillment. Checkout is the entry point for that saga. Placing it in Shopping would force Shopping to publish to the Orders saga — a cross-context coupling better expressed as a clean integration message.
- **Simpler future additions:** Wishlists, saved carts, abandoned cart flows all belong in Shopping. Checkout is not in that family.

---

## Consequences

**Positive:**
- Shopping BC is focused on pre-purchase experience
- Orders BC fully controls the transactional order lifecycle from checkout through delivery
- Clean integration contract: `CheckoutInitiated` integration message has a clear producer (Shopping) and consumer (Orders)

**Negative:**
- Requires careful migration of existing Checkout tests to Orders BC
- Integration message adds one hop between BCs for checkout initiation

---

## Alternatives Considered

**Keep Checkout in Shopping BC:** Rejected because Shopping would have become responsible for payment-commitment flows, blurring its identity as a pre-purchase BC.

**Create a dedicated Checkout BC:** Over-engineered for current scale. Orders BC is the natural owner since it already orchestrates the post-checkout workflow.

---

## References

- Cycle 8 retrospective: `docs/planning/cycles/cycle-08-checkout-migration.md`
- [ADR 0014: Checkout Migration Completion](./0014-checkout-migration-completion.md) — subsequent completion of the migration
