# CritterSupply Priority Review: Post-Cycle 23

**Date:** 2026-03-11  
**Initiated by:** Product Owner  
**Status:** ✅ Complete — PO draft + UX review + Principal Architect sign-off  
**Context:** Completed Cycle 23 (Vendor Portal E2E Testing). Cycle 24 (Admin Portal Phase 1) is planned but not started.

---

## Purpose

This document captures a cross-functional priority review conducted after the completion of Cycle 23. Rather than immediately beginning Cycle 24 as planned, we are pausing to ask:

> *Are we building the right things in the right order?*

This review synthesizes three perspectives:
1. **Product Owner** — business value, customer experience gaps, revenue impact
2. **UX Engineer** — user research insights, frontend maturity, design system readiness
3. **Principal Software Architect** — technical debt, architectural completeness, system integrity

---

## Part I: Product Owner Analysis

*Authored by: Product Owner*

---

### Where We Are

After 23 cycles, CritterSupply has a remarkably complete foundation for a reference architecture:

- **Core transaction BCs** (Orders, Payments, Shopping, Inventory, Fulfillment) are all implemented and integrated
- **Customer Identity** provides address book and customer profile
- **Product Catalog** is live with vendor assignment
- **Customer Experience** (Storefront) delivers a real Blazor Server frontend with SSE → SignalR real-time updates and a working checkout wizard
- **Vendor Portal** is a production-quality Blazor WASM frontend with JWT auth, SignalR, and a full change request workflow — completing Cycles 22 and 23

This is impressive. The architecture is coherent. The patterns are well-established. The frontends are real.

But here's what's keeping me up at night from a business perspective:

---

### The Gaps That Matter Most

#### 🔴 Gap 1: Customers Cannot Return Items

This is the most glaring customer-facing omission. CritterSupply can take a customer's money and ship them a product, but if something goes wrong — wrong item, defective product, change of mind — **there is no path for the customer to get their money back** beyond manual intervention.

The Returns BC has a complete domain specification (`docs/returns/RETURNS-BC-SPEC.md`), Principal Architect sign-off, and integration contracts defined in CONTEXTS.md. It is shovel-ready. There is no business justification for deferring this further.

**Business impact if unaddressed:**
- Customer trust: every e-commerce retailer offers returns. No return policy = no repeat business.
- Regulatory risk: consumer protection laws in most jurisdictions require a return mechanism.
- Orders saga is incomplete: the saga handles `ReturnApproved`, `ReturnCompleted`, `ReturnRejected` — but these events can never be published. The saga has dead handlers.

#### 🔴 Gap 2: The Fulfillment P0 Bug

There is a **confirmed production-severity bug** documented in `docs/planning/fulfillment-evolution-plan.md`:

> `ShipmentDeliveryFailed` domain event is never cascaded to the integration message. No HTTP endpoint exists to record a delivery failure. The Orders saga handler exists but is permanently unreachable — orders are stuck in `Shipped` forever when delivery fails.

This means any order where the carrier fails to deliver is permanently orphaned. The customer cannot get a refund. The order never closes. This is not a theoretical edge case — carrier delivery failures happen on every platform with meaningful order volume.

**This must be fixed before Returns is built**, because Returns depends on `ShipmentDelivered` to establish the return eligibility window. We need the delivery lifecycle to be correct end-to-end.

#### 🟡 Gap 3: Customers Don't Know What's Happening

There is no Notifications BC. When a customer places an order, they get no email confirmation. When their order ships, they get no tracking notification. When their return is approved, they find out only by logging back in and checking.

This is table stakes for e-commerce. Without Notifications, every other workflow is "silent" from the customer's perspective. Returns in particular becomes frustrating without notifications — imagine waiting for a return label that never arrives with no email confirmation.

**However:** Notifications is also the most "infrastructure-y" of the gaps. It requires integrating with an email/SMS provider, and designing for eventual delivery guarantees. It's important but can follow immediately after Returns.

#### 🟡 Gap 4: No Promotions or Discounts

Promotions BC is the "growth" layer of e-commerce. Cart abandonment emails, first-purchase discounts, BOGO deals, and loyalty coupons are how retailers retain customers. Without Promotions, CritterSupply has no growth lever — just the base transaction.

This matters less urgently than Returns, but it matters for the reference architecture's completeness as a teaching tool.

#### 🟢 Gap 5: Admin Portal

The Admin Portal is important. It is how internal teams manage the business: customer service reps handling escalations, pricing managers setting promotions, warehouse clerks acknowledging stock alerts, executives reading dashboards.

**But the problem statement is right to defer this.** The Admin Portal is architecturally complex — it touches every BC, requires sophisticated RBAC, and needs us to have mastered the Blazor WASM + SignalR + JWT patterns that we only finished establishing in Cycles 22 and 23. The event modeling is done. The personas and role matrix are defined. Let it marinate while we shore up the customer-facing foundation.

---

### Priority Recommendation

Based on business value, architectural readiness, and the principle of building a coherent customer journey before building administrative tooling:

| Priority | Work Item | Rationale |
|---|---|---|
| 🔴 **P0** | Fix Fulfillment P0/P1 bugs | Production bug: delivery failures orphan orders permanently |
| 🔴 **P1** | Returns BC (Phase 1: Self-Service Returns) | Most critical customer-facing gap; spec is complete; completes Order saga |
| 🟡 **P2** | Notifications BC (Phase 1: Transactional) | Returns without notifications is unusable; order confirmation, return approval emails |
| 🟡 **P3** | Promotions BC (Phase 1: Coupons) | Growth lever; Shopping BC already has `CouponApplied`/`CouponRemoved` placeholder events |
| 🟢 **P4** | Admin Portal (Phase 1: Read-only dashboards) | High value, but deferred until WASM/SignalR/RBAC patterns are solidified |
| 🟢 **P5** | Product Catalog Evolution (Variants, Listings) | Strategic, but not blocking customer journeys |

---

### On the Admin Portal Specifically

I want to be explicit: I am **not** deprioritizing Admin Portal because it is unimportant. I am deprioritizing it because:

1. **The Vendor Portal just taught us a lot.** Cycles 22 and 23 demonstrated the full WASM + SignalR + JWT + RBAC pattern. Let that knowledge settle before we scale it to a much more complex internal portal.

2. **Admin Portal touches everything.** A half-built Admin Portal is worse than no Admin Portal, because it creates the impression of administrative control where none actually exists. Better to build it right after we've fixed the underlying gaps it would expose.

3. **Customer experience is the foundation.** An internal portal without working customer workflows (returns, notifications) is a façade. Customer service reps would look at it and ask "why can't I process a return?"

---

### What Success Looks Like After These Priorities

When P0–P3 are complete, a customer can:
1. Browse products → add to cart → checkout → receive order confirmation email *(Notifications)*
2. Track their order in real-time via the storefront *(already working)*
3. Initiate a return request within the return window *(Returns)*
4. Receive an email when their return label is generated *(Notifications)*
5. See their refund status in order history *(Returns + Orders saga completion)*
6. Use a coupon code during checkout *(Promotions)*

That is a **complete, trust-worthy customer journey**. That is what CritterSupply should demonstrate as a reference architecture.

---

*Product Owner analysis complete. Handing to UX Engineer for review.*

---

## Part II: UX Engineer Review

*Authored by: UX Engineer*

---

### Overall Reaction

The PO's priority ordering is sound from a user-experience perspective, and I endorse the headline sequence: fix the delivery failure bug, ship Returns, then Notifications, then Promotions. What I want to add is texture about *why* this order is right, flag a handful of UX gaps the priority list doesn't surface, and push back on one assumption that is quietly creating user-trust debt right now.

---

### The Lie We're Telling Customers Today

Before anything else: `OrderConfirmation.razor` contains a bulleted checklist that reads *"You'll receive an order confirmation email shortly."* No email will ever arrive. There is no Notifications BC. This is not a minor cosmetic issue — it is an explicit broken promise made to every customer who completes checkout today.

This copy must be corrected immediately, independent of any cycle prioritization. The fix is a one-line edit: remove or qualify that bullet until Notifications is live. Shipping a known false promise erodes trust faster than shipping no promise at all. I will make this change as a patch.

---

### UX Insights on the P0/P1 Fulfillment Bugs

The `ShipmentDeliveryFailed` cascade bug has a direct user-facing consequence: a customer whose delivery failed sees their order page perpetually frozen at "Shipped." There is no status update, no prompt to act, no path to resolution. From the customer's perspective, their order simply… stops. This is the most anxiety-inducing state an e-commerce customer can experience — worse than a clear failure message, because silence implies the problem might still resolve itself.

When this is fixed, the delivery failure event must also produce a visible status transition in the storefront's `OrderConfirmation` component. The `OnSseEvent` handler already maps `shipment-delivered` — a `shipment-delivery-failed` case must be added so customers see actionable copy ("Your delivery was unsuccessful — our team has been notified") rather than a stalled progress state. Fix the event cascade in the backend *and* the status display in the frontend simultaneously.

---

### UX Insights on Returns (P1)

The Returns BC spec is well-reasoned from a domain perspective. Here is what the frontend must get right:

**Entry point:** Returns must be initiated from Order History. `OrderHistory.razor` is currently a placeholder showing only an empty state. Before Returns can be customer-facing, Order History must become a real page. These must ship together.

**Return window countdown:** The 30-day eligibility window is a meaningful deadline. Customers should see it clearly — something like *"Return window closes in 12 days"* displayed on each eligible order in Order History. A customer who doesn't know the clock is ticking will miss their window and blame the platform.

**Restocking fee disclosure timing:** The spec places restocking fee communication at approval time (`ReturnApproved`). That is too late. A customer who learns about a 15% fee *after* they have committed to a return will feel ambushed, even if the fee is contractually valid. The return initiation flow must disclose the potential restocking fee upfront — before submission — for non-defective reasons. `Defective` and `WrongItem` reasons should show no fee language (those are merchant-fault returns); `Unwanted` and `Other` should show a clear disclosure.

**Auto-approval as a trust signal:** The spec allows auto-approval for defective and wrong-item cases. This is the right call, and the UI should reflect it: customers in those categories should see an immediate approval response with label generation, not a "your request is under review" holding state. Speed of resolution for obvious merchant-fault cases is a loyalty-building moment.

**Denials need plain language:** Auto-denial for non-returnable items or an expired window must use plain language that explains *why*, not just that the request was rejected. "We're unable to process this return because your 30-day return window closed on March 14" is actionable. "Return request denied" is not.

**The CS agent gap:** FR-03 calls for customer service agents to manually approve returns. Without Admin Portal, there is no UI for this workflow. The Returns API endpoints exist (`POST /api/returns/{id}/approve`), but no one can reach them. Before shipping Returns BC to production, the team must decide: do we expose a minimal internal tool, or do we document that manual approvals are API-only until Admin Portal ships? This is a real operational gap that will surface immediately.

---

### UX Insights on Notifications (P2)

The PO is right that Returns without notifications is "unusable." I want to be specific about the priority stack within Notifications Phase 1:

1. **Order confirmation email** — highest priority; fixes the broken promise in `OrderConfirmation.razor`
2. **Shipping notification with tracking link** — currently a customer must log back in to discover their order shipped
3. **Return approval/denial email** — a customer waiting on return status has no other way to know
4. **Return label delivery** — a customer who approved a return but never received a label is stuck

In-app notifications (a bell icon in the storefront nav) are valuable but lower priority than transactional email. Email reaches the customer even when they have closed the browser tab. The storefront's existing SignalR infrastructure is excellent for real-time in-session updates — but email is the durable, out-of-session channel that closes the loop on every async workflow.

One integration consideration: the `OrderConfirmation.razor` component already subscribes to SignalR for real-time status pushes. When Notifications ships, in-app notification events should flow through the same SignalR hub, so customers see toast-style alerts when they are actively browsing while their order status changes. The hub plumbing is already there — Notifications just needs to publish to it.

---

### UX Insights on Deferring Admin Portal (P4)

I agree with the deferral. The strongest argument is not the technical pattern-maturation argument (though that is valid) — it is the workflow completeness argument. Admin Portal Phase 1 as originally scoped is read-only dashboards. A read-only dashboard during a cycle when customers cannot yet submit returns would be a dashboard that surfaces problems with no resolution path. That is a demoralizing tool to hand to customer service staff.

Build the workflows first. Build the observability second.

**Risk to flag:** The UX research doc I produced for Admin Portal (`admin-portal-ux-research.md`) includes a CustomerService Workbench with an order lookup and action surface. That research is ready. The risk of deferral is not losing the design work — it is losing the institutional memory of *why* the CS dashboard was designed the way it was. I recommend we explicitly carry the research doc forward as a living artifact and revisit it during Admin Portal planning, not re-derive it from scratch.

---

### Additional UX Concerns the PO May Have Missed

**Product Catalog variants and non-returnable categories (P5 dependency on Returns):** The Returns spec enforces non-returnable item categories at the domain level, but the Product Catalog has no variant or attribute model yet. If a product is marked non-returnable, where does that attribute live today? Customers discovering non-returnability only at return-request time is poor UX. The product detail page should surface return eligibility information proactively. This is a cross-priority dependency worth discussing with the architect.

**Empty states throughout the storefront:** `OrderHistory.razor` is a full placeholder. The absence of real order history means real customer journeys (Returns, order tracking) have no UI home. OrderHistory must be treated as a prerequisite to Returns, not a follow-on task.

---

### Priority Endorsement

I endorse the PO's sequence as written, with three addenda:

| # | Addendum |
|---|----------|
| 1 | Patch `OrderConfirmation.razor` immediately to remove the false email promise |
| 2 | Treat Order History as a prerequisite to Returns (P1), not a separate backlog item |
| 3 | Document the CS agent approval gap explicitly — agree on operational posture (API-only or minimal tool) before Returns ships to production |

The overall priority list is strategically correct. A complete, trust-worthy customer journey is the right milestone before internal tooling. Ship Returns + Notifications, and CritterSupply becomes a platform customers can rely on.

---

*UX Engineer review complete. Handing to Principal Software Architect.*

---

## Part III: Principal Software Architect Review

*Authored by: Principal Software Architect*

---

### Overall Assessment

The PO and UX have produced a clear, well-reasoned priority list and I endorse the headline sequence. But there are three architectural findings from code inspection that neither perspective surfaced, and at least one of them is severe enough to block Returns from working end-to-end even after the Fulfillment P0 fix. I'll lead with those, because they change what "ready to start Returns" actually means.

---

### 1. Priority Ordering — Architectural Perspective

The sequence is correct. The architecture demands it.

Core transaction integrity must precede new BCs. An orphaned order stuck in `Shipped` state is not a cosmetic defect — it is corrupted saga state that will interact unpredictably with every feature we build on top of the Orders lifecycle. Fixing Fulfillment first is not cautious; it is load-bearing.

Returns before Notifications is also architecturally right. Notifications is a pure subscriber — it reacts to events other BCs publish. Returns produces the events Notifications needs (`ReturnApproved`, `ReturnCompleted`, `ReturnDenied`). You cannot sensibly design and test the Notifications subscription surface against a BC that doesn't exist yet. Build the publisher before the subscriber.

One structural challenge to the PO's framing: the P0 Fulfillment fix and the Orders saga prerequisite work for Returns are not two separate items. They are part of the same saga correctness problem. I will explain below.

---

### 2. Fulfillment Bugs — Severity Assessment and Fix Strategy

#### The RabbitMQ P2 Bug Is Actually P0.5

The most underclassified bug in the list is not in the P0 slot — it's the RabbitMQ transport omission in `Fulfillment.Api/Program.cs`. Fulfillment currently runs `UseDurableLocalQueues()` only. In the Docker Compose topology — the environment where every cross-BC scenario is actually tested — `ShipmentDispatched` and `ShipmentDelivered` integration messages from Fulfillment BC cannot be delivered to Orders BC across process boundaries. The Orders saga never receives them.

This means that right now, in the multi-process deployment, the entire Fulfillment→Orders integration chain is dark. The P0 fix (adding the delivery failure cascade) will be pointless until this is resolved. **Fix the RabbitMQ transport wiring first, as part of Phase 0, before any other Fulfillment work.** It is a four-line change in `Program.cs` following ADR 0008. It unblocks everything else.

#### P0 — Delivery Failure Cascade

The bug is confirmed. `ConfirmDelivery.cs` has no failure path. The Orders saga has a `Handle(FulfillmentMessages.ShipmentDeliveryFailed)` handler that is correctly implemented (it keeps the order in `Shipped` state, consistent with carrier retry semantics), but the integration message is never published from Fulfillment because no HTTP endpoint exists to record the failure. Fix: add `RecordDeliveryFailure` endpoint + publish the integration message. The fulfillment evolution plan's Phase 0.1 code sketch is correct. Land this with the RabbitMQ fix in a single atomic cycle.

The UX Engineer is also right that the frontend must handle `shipment-delivery-failed` in the SSE event mapping. This is the other half of the fix. Backend and frontend should ship together.

#### P1 — ShippingAddress Type Inconsistency (Breaking Change Strategy)

This is the trickiest of the batch. `Messages.Contracts.Orders.ShippingAddress` uses `(Street, Street2, City, State, PostalCode, Country)` while `Messages.Contracts.Fulfillment.ShippingAddress` uses `(AddressLine1, AddressLine2, City, StateProvince, PostalCode, Country)`. Consolidating them is a deserialization breaking change if done naïvely.

**Recommended approach — two-phase migration:**

**Phase A (safe, non-breaking):**
- Introduce a canonical `Messages.Contracts.Shared.ShippingAddress` record.
- Add `[JsonPropertyName]` annotations for both naming conventions on each field (e.g., `Street`/`AddressLine1`, `State`/`StateProvince`).
- Both BCs adopt the shared type. Existing persisted messages in the Wolverine inbox/outbox and Marten event store remain readable because the deserializer will resolve either field name.
- Mark the legacy-named properties `[Obsolete]` to signal intent without breaking callers.

**Phase B (cleanup, following cycle):**
- After all in-flight messages have drained (one full deployment cycle), remove the legacy-named properties and the `[Obsolete]` annotations.

Do not attempt a single-phase hard cutover. The at-least-once delivery guarantee means there will be in-flight messages using the old field names during any deployment window. A hard rename will silently produce null addresses for those messages.

#### P1 — Shipment.Create() Mints Its Own ID

Simple fix: remove the `Guid.CreateVersion7()` call in `Shipment.Create()`. The stream ID is the aggregate identity in Marten event sourcing — the aggregate must not mint a competing one. This creates a silent inconsistency between the stream key and the `Id` property that will surface as a debugging nightmare when reading events. Fix is one line; do it in Phase 0.

#### P1 — Picking and Packing Dead States

Remove `Picking` and `Packing` from `ShipmentStatus` for now. Dead enum values that cannot be reached through any valid event path are misleading to anyone reading the code and dangerous if a deserializer ever accidentally produces one. We will add them back with proper events in Fulfillment Phase 1. Clean code over exhaustive stubs.

#### P2 — Non-Idempotent Shipment Creation

Apply ADR 0016 (UUID v5 derived from `OrderId`) in `FulfillmentRequestedHandler`. This is a one-line fix and should accompany Phase 0 since it closes a real duplicate-processing risk.

---

### 3. Returns BC Architectural Readiness — A Critical Prerequisite Gap

The Returns BC spec is architecturally sound. The domain model is well-reasoned, the event names are precise, the integration contracts are clean, and the spec correctly delegates refund processing to Orders and restocking to Inventory rather than doing either directly. The prior architect sign-off stands.

**However**, code inspection of the Orders saga reveals a prerequisite gap that neither the PO nor UX flagged, and it will prevent Returns from working end-to-end:

**The Orders saga's `Handle(ReturnWindowExpired)` unconditionally calls `MarkCompleted()`.**

When delivery is confirmed, the saga schedules a `ReturnWindowExpired` message 30 days out and waits. If no return is submitted, that message fires and the saga closes — correct. But if a customer submits a return before the window closes, that scheduled message is already in flight and cannot be cancelled (Wolverine does not support cancelling a scheduled message by logical ID). When `ReturnWindowExpired` fires 30 days after delivery, the saga will close regardless of whether the return inspection is still in progress — potentially weeks before `Returns.ReturnCompleted` arrives.

When `Returns.ReturnCompleted` finally fires, it needs the Orders saga to receive it and publish `Payments.RefundRequested` (only Orders knows the `PaymentId`). But by then, the saga is completed and gone. The refund never happens.

**The required saga changes before Returns ships:**

1. Add `bool IsReturnInProgress` and `Guid? ActiveReturnId` properties to the `Order` saga.
2. Add a handler for a new `Returns.ReturnRequested` integration event (or react to it via a separate listener) that sets `IsReturnInProgress = true`.
3. Guard `Handle(ReturnWindowExpired)` to call `MarkCompleted()` only when `!IsReturnInProgress`.
4. Add a handler for `Returns.ReturnCompleted` that publishes `Payments.RefundRequested` with the correct `PaymentId` and the `FinalRefundAmount` from the return, then calls `MarkCompleted()`.
5. Add a handler for `Returns.ReturnDenied` that sets `IsReturnInProgress = false` and, if `ReturnWindowExpired` has already fired, calls `MarkCompleted()`.

These are the saga changes that **must land before Returns BC is shipped**. They are not large, but they are architecturally critical. I recommend this work be scoped into the first Returns cycle as a prerequisite vertical slice, not treated as an afterthought.

One other gap: the `ReturnEligibilityWindow` read model requires a snapshot of eligible line items from the Orders BC at delivery time (per FR-01). This means Returns BC needs a query endpoint on Orders API — `GET /api/orders/{orderId}/returnable-items` — that does not yet exist. Add it to the Returns prerequisite list.

The non-returnable item attribute (`IsReturnable`) is missing from the Product Catalog. For Returns v1, I recommend the Returns BC maintain its own non-returnable category policy configuration rather than taking a hard dependency on a Product Catalog attribute that doesn't exist. Add a `RETURNS_NON_RETURNABLE_CATEGORIES` configuration key. This is pragmatic and can be refactored to a Product Catalog attribute when the variant model is implemented.

---

### 4. Notifications BC — Design Considerations

Notifications is a pure choreography subscriber. No orchestration is needed or appropriate — Notifications reacts to facts published by other BCs and delivers them to customers. This is the right design and the CONTEXTS.md description is sound.

**Wolverine/Marten patterns that should govern this BC:**

The at-least-once delivery guarantee from Wolverine's inbox pattern means Notifications must be idempotent by design. Every handler must check a stored `MessageId` (or `CorrelationId`) before dispatching to the email provider. Store seen message IDs in a Marten document (`NotificationRecord`) keyed by `(MessageId, Channel)`. A duplicate delivery must be a silent no-op, not a second email to the customer.

The outbox pattern for email dispatch is subtler than it looks: we do not need a Wolverine outbox for the integration events themselves (Wolverine's transactional inbox handles that), but we do need to ensure that the call to the email provider (SendGrid, Postmark, etc.) is treated as a side effect at the edge — not inline in the handler. The handler should publish an internal `SendEmail` command which is then picked up by a dedicated `EmailDispatchHandler`. This keeps the integration event handler pure (it just records intent and publishes the internal command) and makes the email dispatch independently retryable if the provider is temporarily unavailable.

**Integration contracts needed that do not yet exist:**

- `Returns.ReturnApproved` (from Returns BC) — Notifications needs this to send the return label email
- `Returns.ReturnDenied` (from Returns BC) — Notifications needs this to explain the rejection
- `Returns.ReturnCompleted` (from Returns BC) — Notifications needs this to confirm refund triggered
- `Returns.ReturnExpired` (from Returns BC) — Notifications needs this to alert the customer

All of these depend on Returns BC shipping first. Notifications Phase 1 can begin with `OrderPlaced` and `ShipmentDispatched` (both already published by existing BCs) and expand to Returns events in Phase 1b once Returns is live.

---

### 5. Admin Portal Deferral — Architectural View

I agree with the deferral, and the PO's reasoning is sound. But I want to name a concrete risk the PO and UX did not surface: **the RBAC model.**

Admin Portal requires meaningful role-based access control — customer service reps should not have access to pricing tools; pricing managers should not be able to approve returns on behalf of customers. The longer we defer Admin Portal, the longer the RBAC model remains unspecified. When we do build it, every BC we've shipped in the interim will need to be retrofitted with authorization checks against a model we haven't designed yet.

**Recommended action during deferral:** Author an ADR that defines the Admin Portal role taxonomy (CustomerService, Merchandising, WarehouseManager, Finance, Admin) and the permission surface per BC. This ADR does not need to be implemented — it just needs to exist so that when we add Admin Portal endpoints to existing BCs, we're fitting them to a pre-agreed model rather than inventing one on the fly.

The UX research doc is a living artifact and should be maintained as the UX recommends.

---

### 6. Product Catalog Evolution (Variants/Listings) — Status

Seven of ten decisions are outstanding (D2, D3, D6, D7, D8, D9, D10), all awaiting Owner/Erik input. D1 (parent/child hierarchy), D4 (category-to-marketplace mapping lives in Marketplaces BC), and D5 (PSA-decided) are resolved. D1 being decided is necessary but far from sufficient — the unresolved decisions cover whether variants can have independent pricing, who creates variants, what happens to marketplace listings when a parent product is updated, and whether bundle listings are in scope. These are foundational decisions that determine the shape of every aggregate, every integration event, and every projection in three new BCs (Product Catalog Evolution, Listings BC, Marketplaces BC).

**Do not move this up in priority.** It is not implementation-ready. The discovery doc is excellent, but the outstanding decisions create irresolvable design ambiguity. Attempting to implement against an incomplete decision set produces a worse outcome than waiting for the decisions — you build to an assumption, the assumption is overturned, and you refactor under time pressure.

My recommendation: use the Returns and Notifications cycle to drive Owner/Erik decisions on D2–D10. Hold a focused decision session specifically on the outstanding items. The companion evolution plan is ready; the glossary is ready; the variant model doc is ready. This is a decision bottleneck, not a technical bottleneck.

---

### 7. What the PO and UX Missed — Architectural Standpoint

#### The `ReturnWindowExpired` Saga Bug (Critical)

Covered in detail above. This is the most important finding in this review. Returns cannot work end-to-end without the saga changes. This was not visible from product or UX perspectives because it requires reading `Order.cs` and `OrderDecider.cs` together.

#### The Missing Orders API Endpoint for Returns Eligibility Snapshot

Returns BC's `FR-01` requires querying Orders BC at delivery time to snapshot returnable line items. That query endpoint does not exist on the Orders API. It is a prerequisite for Returns, not a nice-to-have.

#### The RabbitMQ Wiring Gap Makes the P0 Fix Incomplete in Isolation

The RabbitMQ transport omission in Fulfillment.Api means the P0 fix, while correct, would be untestable in the Docker Compose environment without also wiring the transport. These two fixes must ship together.

#### Message Contract Versioning

Returns BC and Notifications BC will introduce several new integration messages. Before those contracts are written, the team should establish the contract versioning convention (namespace strategy, version suffix, backward compatibility guarantees). ADR 0018 or similar. This is low-ceremony to establish and expensive to retrofit.

---

### 8. Revised Priority List — Architectural Endorsement

I endorse the PO's sequence with these refinements:

| Priority | Work Item | Architectural Rationale |
|---|---|---|
| 🔴 **P0** | Fulfillment transport + P0 bug (RabbitMQ wiring + delivery failure cascade + UX SSE handler) | These must ship together; the cascade fix is dark without the transport fix |
| 🔴 **P0.5** | Fulfillment P1 bug sweep (ShipmentId, dead states, idempotent handler) | Low-complexity; ship in same cycle as P0 to avoid context-switching back |
| 🔴 **P0.5** | Orders saga prerequisite changes for Returns (`IsReturnInProgress`, `ReturnCompleted` handler, `returnable-items` endpoint) | Must precede Returns BC implementation; no new BC needed, but saga must be correct |
| 🔴 **P0.5** | `ShippingAddress` consolidation Phase A (dual-read shared type) | Non-breaking; unblocks correct serialization before Returns adds more cross-BC address usage |
| 🔴 **P1** | Returns BC (Phase 1: Self-Service Returns) + Order History page | Spec is ready; saga prerequisites must be in place first |
| 🟡 **P2** | Notifications BC (Phase 1: Transactional email — OrderPlaced, ShipmentDispatched; Phase 1b: Returns events after Returns ships) | Pure choreography; depends on Returns for the full contract surface |
| 🟡 **P3** | Promotions BC (Phase 1: Coupons) | Unblocked; Shopping BC placeholder events exist |
| 🟠 **P3.5** | RBAC ADR for Admin Portal | Non-implementation; author during Promotions cycle to protect investment |
| 🟢 **P4** | Admin Portal (Phase 1: read-only dashboards) | Deferred; RBAC model must be authored first; Vendor Portal patterns are solid foundation |
| 🟢 **P5** | Product Catalog Evolution (Variants/Listings) | Blocked on Owner/Erik decisions D2–D10; use interim cycles to resolve |

The PO's and UX's instincts are architecturally sound. The priority list they produced is right. What changes with this review is the scope of "P0" — it is larger than a single bug fix, and getting that scope right is the difference between a working system and a working-in-isolation system.

---

*Principal Software Architect review complete.*

---

## Resolution & Next Steps

*All three perspectives incorporated. Document status: ✅ Complete.*

---

### Agreed Priority Sequence

All three perspectives — Product Owner, UX Engineer, and Principal Software Architect — are aligned. The following priority sequence replaces the previously planned Cycle 24 (Admin Portal Phase 1):

| Priority | Cycle (Proposed) | Work Item | Lead Concern |
|---|---|---|---|
| 🔴 **P0** | Cycle 24 | Fulfillment bug fix: RabbitMQ transport wiring + `ShipmentDeliveryFailed` cascade + UX SSE handler for delivery failure | Architecture (system integrity) |
| 🔴 **P0.5** | Cycle 24 (same) | Fulfillment P1 sweep: `Shipment.Create()` ID, dead `Picking`/`Packing` states, idempotent `FulfillmentRequestedHandler` | Architecture (clean code) |
| 🔴 **P0.5** | Cycle 24 (same) | Orders saga prerequisites for Returns: `IsReturnInProgress` guard, `ReturnCompleted`/`ReturnDenied` handlers, `returnable-items` endpoint | Architecture (saga correctness) |
| 🔴 **P0.5** | Cycle 24 (same) | `ShippingAddress` consolidation Phase A (shared type + dual-read JSON annotations) | Architecture (breaking change safety) |
| 🔴 **P1** | Cycle 25 | Returns BC Phase 1: Self-Service Returns + Order History page | Product (customer trust) |
| 🟡 **P2** | Cycle 26 | Notifications BC Phase 1: Transactional email (OrderPlaced, ShipmentDispatched; Phase 1b adds Returns events) | Product (closes the silent-workflow gap) |
| 🟡 **P3** | Cycle 27 | Promotions BC Phase 1: Coupons and discounts | Product (growth lever) |
| 🟠 **P3.5** | During Cycle 27 | RBAC ADR for Admin Portal — author during Promotions cycle | Architecture (protect Admin Portal investment) |
| 🟢 **P4** | Cycle 28+ | Admin Portal Phase 1: Read-only dashboards + customer service tooling | Product + UX (internal tooling) |
| 🟢 **P5** | TBD | Product Catalog Evolution: Variants, Listings, Marketplaces — blocked on Owner/Erik decisions D2–D10 | Product (strategic) |

---

### Immediate Actions (Pre-Cycle 24)

1. ✅ **OrderConfirmation.razor patched** — UX Engineer removed the false email promise and replaced it with honest copy about in-page tracking. *(Completed in this review cycle.)*
2. 📋 **Update CURRENT-CYCLE.md** — Revise next-cycle plan from Admin Portal to Fulfillment bug fix sweep.
3. 📋 **GitHub Milestone** — Update/rename Milestone 17 from "Admin Portal Phase 1" to "Fulfillment Integrity + Returns Prerequisites" (Cycle 24), and create a new Milestone for Admin Portal at the appropriate horizon.
4. 📋 **Inform stakeholders** — The Admin Portal event modeling is complete and preserved. It will not be discarded — it is deferred to Cycle 28+.
5. 📋 **Owner/Erik decisions D2–D10** — Use the Returns and Notifications cycles to drive resolution of outstanding Product Catalog variant decisions. Hold a focused decision session.

---

### Documents Produced or Updated

| Document | Change |
|---|---|
| `docs/planning/priority-review-post-cycle-23.md` | ✅ Created (this document) |
| `src/Customer Experience/Storefront.Web/Components/Pages/OrderConfirmation.razor` | ✅ Patched — removed false email promise |
| `docs/planning/CURRENT-CYCLE.md` | 📋 To be updated |

---

*Priority review complete. Reviewed by: Product Owner, UX Engineer, Principal Software Architect. Date: 2026-03-11.*
