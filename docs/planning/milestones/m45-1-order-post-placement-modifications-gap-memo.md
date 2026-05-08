# M45.1 — Order Post-Placement Modifications Gap Memo

> **Status:** Charter input — not implemented
> **Date:** 2026-05-08
> **Author:** Principal Architect (M45.1 cycle)
> **Source:** PO sign-off in `docs/research/state-of-repo-2026-05.md` §7.1 / §7.3, item S4
> **Audience:** Whoever scopes the **Orders remaster**

## Purpose

The PO flagged "**Order post-placement modifications** (address change, line
cancel) — no feature file; likely silently unimplemented." This memo documents
the verification result so the Orders remaster charter can carry it forward.

## Findings

The audit conclusion is unambiguous: **post-placement modifications are not
implemented, and there is no Gherkin documenting the intended behavior**.

| Concern                                                            | State                                                                                                                             |
| ------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------- |
| `ChangeShippingAddress` / `UpdateShippingAddress` command          | ❌ `grep -rn "ChangeShippingAddress\|UpdateShippingAddress\|ChangeAddress" src/Orders/` returns zero results.                       |
| `CancelLineItem` / `RemoveLineItem` command                        | ❌ `grep -rn "CancelLineItem\|RemoveLineItem" src/Orders/` returns zero results.                                                   |
| `ChangeQuantity` / `UpdateLineItem` command                        | ❌ Not present.                                                                                                                    |
| `ModifyOrder` / `UpdateOrder` umbrella command                     | ❌ Not present.                                                                                                                    |
| HTTP endpoints on the Orders API                                   | ❌ `src/Orders/Orders.Api` exposes only: `GET /api/orders/{id}`, `GET /api/orders`, `GET /api/orders/search`, `GET /api/orders/{id}/returnable-items`, `POST /api/orders/{id}/cancel` (whole-order cancel only). |
| Domain events for partial mutation                                 | ❌ The only Orders events that mutate state post-placement are `OrderCancelled`, `OrderHeldOnHold` *(planned, see S5 memo)*, and the eventually-shipped lifecycle (`ShipmentDispatched`, `OrderDelivered`). No `ShippingAddressChanged`, no `LineItemCancelled`, no `LineItemQuantityChanged`. |
| `Order` saga handlers for partial mutation                         | ❌ `Order.cs` has handlers for the placement / payment / inventory / fulfillment / returns lifecycle. No handler accepts a partial-mutation command. |
| Gherkin in `docs/features/orders/`                                 | ❌ **The directory does not exist.** `find docs/features -type d -name orders` returns nothing. There is *no* Gherkin coverage of any Orders behavior — neither placement nor cancellation, let alone modification. |
| README acknowledgement                                             | ⚠️ Partial — the Orders README (`src/Orders/Orders.Api/README.md`) frames Orders as a saga that "owns the order lifecycle" but does not enumerate post-placement mutations as either supported or not. The unspoken assumption is "you cancel the whole order or you don't change it." |
| Backoffice analogue                                                | ❌ Backoffice Customer Service can `POST /api/backoffice/orders/{id}/cancel` (`tests/Backoffice/.../OrderCancellationTests.cs`) — same whole-order semantics. There is no CS-side address-update or line-cancel tool either. |

The "no Gherkin for Orders at all" finding is the most striking. Of the 13
implemented BCs, Orders is the **only** placement-and-fulfillment-critical BC
without a `docs/features/orders/` subdirectory. Returns, Fulfillment, Inventory,
Backoffice, Customer Experience, Pricing, Product Catalog, Vendor Portal, and
Vendor Identity all have feature files. Orders has none.

## Why this matters in practice

Address change and partial cancel are table-stakes for any e-commerce
platform — they are the two most common post-placement support requests. The
current shape forces the workaround:

- **Address change** → cancel the order, refund, recreate. Customer-hostile,
  and the recreated order may price differently or hit out-of-stock.
- **Line cancel** → same pattern. Cannot reduce quantity on a single line; the
  customer must cancel the entire order.

The PO's framing ("likely silently unimplemented") is exactly right — the
absence is invisible to engineers because the codebase does not advertise the
gap. There is no `ModifyOrderHandler.cs` returning a `// TODO` or a README
footnote saying "address changes not yet supported." It simply was not
designed.

## Why this was deferred from M45.1

Implementing post-placement modifications is not a fix; it is a substantial
charter expansion for the Orders BC. A minimum-viable scope would touch:

1. **Eligibility envelope** — define the matrix of which `OrderStatus` values
   permit which mutations. Address change is plausible up through
   `InventoryCommitted`; after handoff to Fulfillment it requires coordination
   with the warehouse. Line cancel must compensate any committed inventory and
   any captured payment.
2. **New commands + events** — at minimum:
   - `ChangeShippingAddress(OrderId, ShippingAddress)` →
     `ShippingAddressChanged`. Compensation: re-route the existing fulfillment
     request if not yet picked, otherwise reject with a clear reason.
   - `CancelLineItem(OrderId, Sku)` → `LineItemCancelled`. Compensation:
     release the inventory reservation for that SKU + issue a partial refund.
   - `ChangeLineItemQuantity(OrderId, Sku, NewQuantity)` →
     `LineItemQuantityChanged`. Compensation: release the delta or reserve the
     delta, plus the payment adjustment.
3. **Saga state extension** — the `Order` saga's `ReservationIds`,
   `CommittedReservationIds`, and `ExpectedReservationCount` must remain
   coherent across mid-flight reductions. The "all reservations confirmed" /
   "all reservations committed" derived booleans (`IsInventoryReserved`,
   `IsInventoryCommitted`) must continue to behave correctly when items leave
   the order.
4. **Inventory side** — partial release commands. The Inventory remaster S3
   introduced quarantine and transfer; partial-cancel-of-reservation is a
   cleanly adjacent slice but is not implemented today.
5. **Payments side** — partial refund (already exists for refund flow; needs
   to be reusable in this orchestration).
6. **Fulfillment side** — handle `address-changed-mid-flight` and
   `line-removed-mid-flight` from the warehouse perspective. If picking has
   started, may need to recall or short-pick.
7. **Backoffice tooling** — CS agents need address-edit and line-cancel
   surfaces. Today they only have whole-order cancel.
8. **Storefront tooling** — customer-self-service address edit (with cutoff
   based on order status). Out of scope for v1, plausibly.
9. **Gherkin** — net-new `docs/features/orders/` directory with at minimum:
   `order-address-change.feature`, `order-line-cancellation.feature`, and
   `order-post-placement-eligibility.feature` (the matrix).
10. **Authorization** — who can change what. Customer can edit address pre-
    handoff; CS can edit during a wider window; nobody can edit post-shipment
    except via Returns.

That is **multiple multi-session arcs**, not a single fix.

## Recommended placement

This work most naturally lives in:

1. **An Orders remaster** *(if/when Orders is selected for remastering)*. The
   PO's stated preference per §7.1 is that Orders is one of the next two
   remasters (alongside Returns). Post-placement modifications, fraud-review
   (S5), and cross-product exchange (S3) are the three biggest charter
   expansions for Orders/Returns and should likely be sequenced together.
2. **As a Tier 3 net-new feature slice** following the Orders remaster — once
   the saga is decomposed (likely DCB, per the broader trajectory), the
   address-change slice becomes much smaller because the saga's coupling to
   the original placement command is loosened.

The PO's preference (§7.1) is "Orders or Returns remaster," so this is option
1 territory.

## Acceptance criteria for "complete"

When this gets picked up, "done" means:

- A `docs/features/orders/` directory exists with at least three feature files
  (placement, cancellation, modification — the directory's mere existence
  closes the most striking finding above).
- The eligibility matrix is documented in an ADR (which `OrderStatus` values
  permit which mutations, and what compensation each triggers).
- `ChangeShippingAddress`, `CancelLineItem`, and `ChangeLineItemQuantity` are
  implemented end-to-end with Inventory and Payments compensation, including
  the "you cannot do this any more" guards keyed off saga state.
- Backoffice CS tooling exposes the new commands.
- The carryover memo for the customer-self-service Storefront UI is filed
  (likely deferred to a separate Customer Experience cycle).

## Out of scope for this memo

- The customer-self-service vs. CS-only question. Defaulting to CS-only is
  the conservative starting point.
- Whether DCB-style decomposition of the `Order` saga is a prerequisite. It
  may be — the saga's current shape (single document, multiple counters,
  derived booleans) is on the thicker end of saga state and would benefit
  from decomposition before partial-mutation logic is added.
- The pricing-recalculation question (does an address change re-quote
  shipping? does a line cancel re-evaluate promotions?). These are real but
  belong to the remaster's scope, not this memo.

## Out of scope for this memo *(but worth flagging)*

The "no `docs/features/orders/` directory" finding is independently a
documentation-hygiene gap. Even before the Orders remaster lands, the team
could file Gherkin for the **already-implemented** Orders behavior
(placement, cancellation) as a low-risk M45.x or M46.x slice. This would
narrow the BDD coverage gap and give the eventual remaster a behavioral
baseline to refactor against. Filed as carryover #2 in the M45.1
retrospective.

---

*End of memo.*
