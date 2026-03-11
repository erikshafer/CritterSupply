# CritterSupply Priority Review: Post-Cycle 23

**Date:** 2026-03-11  
**Initiated by:** Product Owner  
**Status:** 🔄 In Review — PO draft → UX review → Principal Architect sign-off  
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

*To be completed by Principal Software Architect.*

---

## Resolution & Next Steps

*To be completed after all three perspectives are incorporated.*
