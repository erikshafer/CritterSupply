# CritterSupply: Cycle to Milestone Mapping

**Purpose:** This document maps legacy "Cycle N" and "Cycle N Phase M" identifiers to the new Milestone-Based Versioning schema (`M<MAJOR>.<MINOR>`).

**Status:** Living document — updated as new milestones are created

**Related:** [ADR 0032: Milestone-Based Planning Schema](../decisions/0032-milestone-based-planning-schema.md)

---

## Quick Reference

| Old Identifier | New Milestone ID | Description | Status |
|----------------|------------------|-------------|--------|
| Cycle 1-18 | *(archived)* | Early development cycles | ✅ Complete |
| Cycle 19 | M19.0 | Authentication & Authorization | ✅ Complete |
| Cycle 19.5 | M19.1 | Complete Checkout Workflow | ✅ Complete |
| Cycle 20 | M20.0 | Automated Browser Testing (Playwright) | ✅ Complete |
| Cycle 21 | M21.0 | Pricing BC Phase 1 | ✅ Complete |
| Cycle 22 | M22.0 | Vendor Portal + Vendor Identity (6 phases) | ✅ Complete |
| Cycle 23 | M23.0 | Vendor Portal E2E Testing | ✅ Complete |
| Cycle 24 | M24.0 | Fulfillment Integrity + Returns Prerequisites | ✅ Complete |
| Cycle 25 | M25.0 | Returns BC — Core lifecycle | ✅ Complete |
| Cycle 26 | M25.1 | Returns BC — Mixed inspection | ✅ Complete |
| Cycle 27 | M25.2 | Returns BC — Exchanges | ✅ Complete |
| Cycle 28 | M28.0 | Correspondence BC — Email delivery | ✅ Complete |
| Cycle 29 Phase 1 | M29.0 | Admin Identity BC — JWT auth | ✅ Complete |
| Cycle 29 Phase 2 | M29.1 | Promotions BC — Core lifecycle | ✅ Complete |
| Cycle 30 (planned) | M30.0 | Promotions BC — Redemption workflow | 📋 Planned |
| Cycle 31 (planned) | M31.0 | Correspondence BC — Extended integrations & SMS | 📋 Planned |
| Cycle 32+ (planned) | M32.0 | Admin Portal — Read-only dashboards | 📋 Planned |

---

## Detailed Mapping with Deliverables

### M19.0: Authentication & Authorization (Cycle 19)
**Date:** 2026-02-25 to 2026-02-26
**BC:** Customer Identity + Storefront
**Deliverables:**
- Cookie-based authentication (ASP.NET Core)
- Login/Logout pages (MudBlazor)
- Protected routes (Cart, Checkout)
- AppBar authentication UI
- Cart persistence via localStorage

**Files:**
- Plan: `docs/planning/cycles/cycle-19-authentication-authorization.md`
- Retrospective: `docs/planning/cycles/cycle-19-retrospective.md`
- Issues Export: `docs/planning/cycles/cycle-19-issues-export.md`

---

### M19.1: Complete Checkout Workflow (Cycle 19.5)
**Date:** 2026-03-04
**BC:** Customer Experience (Storefront)
**Deliverables:**
- Wired checkout stepper to backend APIs
- Checkout initialization + CheckoutId persistence
- Error handling with MudSnackbar toasts
- End-to-end manual testing

**Files:**
- GitHub Milestone: [Cycle 19.5](https://github.com/erikshafer/CritterSupply/milestone/13)

**Note:** This was a "half-cycle" (hence .5 notation), which becomes M19.1 under new schema.

---

### M20.0: Automated Browser Testing (Cycle 20)
**Date:** 2026-03-04 to 2026-03-07
**BC:** Infrastructure (E2E Testing)
**Deliverables:**
- Playwright + Reqnroll E2E infrastructure
- Real Kestrel servers (SignalR testing)
- Page Object Model with data-testid selectors
- MudBlazor component interaction patterns
- Stub coordination via TestIdProvider
- Playwright tracing for CI
- Full coverage: browsing, cart, checkout, order history, SignalR

**Files:**
- Plan: `docs/planning/cycles/cycle-20-automated-browser-testing.md`
- Retrospective: `docs/planning/cycles/cycle-20-retrospective.md`
- Issues Export: `docs/planning/cycles/cycle-20-issues-export.md`
- GitHub Milestone: [Cycle 20](https://github.com/erikshafer/CritterSupply/milestone/2)

---

### M21.0: Pricing BC Phase 1 (Cycle 21)
**Date:** 2026-03-07 to 2026-03-08
**BC:** Pricing
**Deliverables:**
- ProductPrice event-sourced aggregate (UUID v5)
- Money value object (140 unit tests)
- CurrentPriceView inline projection
- Shopping BC security fix (server-authoritative pricing)
- 5 ADRs (UUID v5, price freeze, Money VO, bulk jobs, MAP vs Floor)
- 151 Pricing tests + 56 Shopping tests
- Docker Compose integration

**Files:**
- Plan: `docs/planning/pricing-event-modeling.md`
- Retrospective: `docs/planning/cycles/cycle-21-retrospective.md`
- GitHub Milestone: [Cycle 21](https://github.com/erikshafer/CritterSupply/milestone/15) (closed)

---

### M22.0: Vendor Portal + Vendor Identity (Cycle 22)
**Date:** 2026-03-08 to 2026-03-10
**BC:** Vendor Portal + Vendor Identity
**Deliverables:**
- Phase 1: JWT Auth (VendorIdentity.Api, token lifecycle)
- Phase 2: Vendor Portal API (analytics, dashboard, multi-tenant)
- Phase 3: Blazor WASM Frontend (SignalR, in-memory JWT)
- Phase 4: Change Request Workflow (7-state machine, Catalog integration)
- Phase 5: Saved Views + VendorAccount (preferences, saved dashboards)
- Phase 6: Full Identity Lifecycle + Admin Tools (8 endpoints, compensation)
- 143 integration tests (100% pass rate)

**Files:**
- Event Modeling: `docs/planning/vendor-portal-event-modeling.md`
- Retrospective: `docs/planning/cycles/cycle-22-retrospective.md`
- GitHub Milestone: [Cycle 22](https://github.com/erikshafer/CritterSupply/milestone/16)

**Note:** This was a multi-phase cycle (6 phases), but delivered as a single milestone. Under new schema, could have been M22.0-M22.5, but retrospectively treated as M22.0 (single major delivery).

---

### M23.0: Vendor Portal E2E Testing (Cycle 23)
**Date:** 2026-03-11
**BC:** Vendor Portal (Testing)
**Deliverables:**
- 3-server E2E fixture (VendorIdentity + VendorPortal + WASM static host)
- 12 BDD scenarios (P0 + P1a) across 3 feature files
- Page Object Models (Login, Dashboard, Change Requests, Submit, Settings)
- SignalR hub message injection testing
- Collaborative design: PA + QA + PO

**Files:**
- Plan: `docs/planning/cycles/cycle-23-vendor-portal-e2e-testing.md`
- Retrospective: `docs/planning/cycles/cycle-23-retrospective.md`
- Skills Update: `docs/skills/e2e-playwright-testing.md`

---

### M24.0: Fulfillment Integrity + Returns Prerequisites (Cycle 24)
**Date:** 2026-03-12
**BC:** Fulfillment + Orders (Prerequisites)
**Deliverables:**
- RabbitMQ transport wired in Fulfillment.Api
- RecordDeliveryFailure endpoint + ShipmentDeliveryFailed cascade
- shipment-delivery-failed SSE case in OrderConfirmation.razor
- UUID v5 idempotent shipment creation
- SharedShippingAddress with dual JSON annotations
- Orders saga return handlers + IsReturnInProgress guard
- GET /api/orders/{orderId}/returnable-items endpoint
- Event modeling exercise (PO + UXE)

**Files:**
- Plan: `docs/planning/cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md`

**Note:** This cycle prepared infrastructure for Returns BC (M25.x).

---

### M25.0: Returns BC — Core Lifecycle (Cycle 25)
**Date:** 2026-03-12
**BC:** Returns
**Port:** 5245
**Deliverables:**
- Event-sourced Return aggregate (10 states, 9 events)
- 6 command handlers (RequestReturn, ApproveReturn, DenyReturn, ReceiveReturn, SubmitInspection, ExpireReturn)
- ReturnEligibilityWindow (from Fulfillment.ShipmentDelivered)
- Auto-approval logic + restocking fee calculation
- 7 API endpoints
- 48 unit tests + 5 integration tests

**Files:**
- Plan & Retrospective: `docs/planning/cycles/cycle-25-returns-bc-phase-1.md`

---

### M25.1: Returns BC — Mixed Inspection (Cycle 26)
**Date:** 2026-03-12 to 2026-03-13
**BC:** Returns
**Deliverables:**
- ReturnCompleted expanded with per-item disposition
- 5 new integration events (ReturnApproved, ReturnRejected, etc.)
- ReturnDenied expanded with CustomerId and Message
- Mixed inspection three-way logic (all-pass/all-fail/mixed)
- GetReturnsForOrder query (Marten inline snapshots)
- RabbitMQ dual-queue routing
- Fulfillment → Returns queue wiring fix
- Orders saga: DeliveredAt + ReturnRejected/ReturnExpired handlers
- CS agent runbook
- ~99 total tests (53 unit + 34 integration + 12 saga)

**Files:**
- Plan: `docs/planning/cycles/cycle-26-returns-bc-phase-2.md`
- Retrospective: `docs/planning/cycles/cycle-26-returns-bc-phase-2-retrospective.md`

---

### M25.2: Returns BC — Exchanges (Cycle 27)
**Date:** 2026-03-13
**BC:** Returns
**Deliverables:**
- Exchange workflow (UC-11): ReturnType enum, ExchangeRequest record
- 5 exchange domain events, 3 command handlers
- 6 integration messages for exchange workflow
- CE SignalR handlers (7 handlers, ReturnStatusChanged event)
- Sequential returns: IsReturnInProgress → ActiveReturnIds refactor
- Anticorruption layer: EnumTranslations static class
- GetReturnableItems DeliveredAt fix + $0 refund guard
- Cross-BC smoke tests (3-host Alba fixture)
- Fraud detection patterns documentation

**Files:**
- Plan: `docs/planning/cycles/cycle-27-returns-bc-phase-3.md`
- Retrospective: `docs/planning/cycles/cycle-27-returns-bc-phase-3-retrospective.md`

---

### M28.0: Correspondence BC — Email Delivery (Cycle 28)
**Date:** 2026-03-13 to 2026-03-14
**BC:** Correspondence
**Port:** 5248
**Deliverables:**
- Message aggregate (event-sourced) — 4 domain events
- Provider interfaces (IEmailProvider, StubEmailProvider)
- OrderPlacedHandler (email order confirmations)
- SendMessage handler with exponential backoff retry
- MessageListView projection (inline)
- HTTP query endpoints (GetMessagesForCustomer, GetMessageDetails)
- 12 unit tests + 5 integration tests
- CONTEXTS.md integration

**Files:**
- Retrospective: `docs/planning/cycles/cycle-28-correspondence-bc-phase-1-retrospective.md`

---

### M29.0: Admin Identity BC — JWT Auth (Cycle 29 Phase 1)
**Date:** 2026-03-14
**BC:** Admin Identity
**Port:** 5249
**Deliverables:**
- ADR 0031: RBAC model (7 roles, policy-based authorization)
- EF Core entity model: AdminUser, AdminRole, AdminUserStatus
- Authentication handlers: Login, RefreshToken, Logout
- User management handlers: CreateAdminUser, GetAdminUsers, ChangeRole, DeactivateUser
- JWT token generation with 7 authorization policies
- API endpoints: 3 auth + 4 user management (Wolverine HTTP)
- Infrastructure: Docker Compose, Aspire, database, port 5249

**Files:**
- Retrospective: `docs/planning/cycles/cycle-29-admin-identity-phase-1-retrospective.md`
- ADR: `docs/decisions/0031-admin-portal-rbac-model.md`

---

### M29.1: Promotions BC — Core Lifecycle (Cycle 29 Phase 2)
**Date:** 2026-03-14 to 2026-03-15
**BC:** Promotions
**Port:** 5250
**Deliverables:**
- Event-sourced Promotion aggregate (UUID v7) — 6 domain events
- Event-sourced Coupon aggregate (UUID v5) — 4 domain events
- Command handlers: CreatePromotion, ActivatePromotion, IssueCoupon
- CouponLookupView projection (case-insensitive validation)
- ValidateCoupon query endpoint
- Marten snapshot projections (Promotion + Coupon)
- 11 integration tests
- Pattern discoveries: IStartStream return type, snapshot projection requirement

**Files:**
- Retrospective: `docs/planning/cycles/cycle-29-phase-2-retrospective-notes.md`

**Deferred to M30.0:** Redemption tracking, batch generation, Shopping/Pricing integration

---

### M30.0: Promotions BC — Redemption Workflow (Planned)
**Status:** 📋 Planned
**BC:** Promotions
**Planned Deliverables:**
- RedeemCoupon command + handler (usage limit via optimistic concurrency)
- RevokeCoupon command + handler (admin action)
- Expire Coupon scheduled message (Wolverine delayed messaging)
- Shopping BC integration: ApplyCouponToCart, RemoveCouponFromCart
- Pricing BC integration: Floor price enforcement during discount calculation
- GenerateCouponBatch handler (fan-out pattern)
- OrderPlacedHandler for coupon redemption recording
- ActivePromotionsView projection
- RabbitMQ integration messages: PromotionActivated, PromotionExpired
- Docker Compose + Aspire configuration

---

### M31.0: Correspondence BC — Extended Integrations & SMS (Planned)
**Status:** 📋 Planned
**BC:** Correspondence
**Planned Deliverables:**
- Phase 2a: ShipmentDispatched, ShipmentDelivered, ShipmentDeliveryFailed (Fulfillment)
- Phase 2b: ReturnApproved, ReturnDenied, ReturnCompleted, ReturnExpired (Returns)
- Phase 2c: RefundCompleted (Payments)
- SMS channel implementation (Twilio integration)
- Template system for email/SMS message formatting

---

### M32.0: Admin Portal — Read-Only Dashboards (Planned)
**Status:** 📋 Planned
**BC:** Admin Portal
**Prerequisites:**
- Multi-issuer JWT support in domain BCs
- HTTP endpoint gaps closed (Customer Identity email search, Inventory HTTP layer, etc.)

**Planned Deliverables:**
- Read-only dashboards: Orders, Returns, Customers, Inventory
- Customer Service tooling: Return approval/denial, correspondence history
- Event Modeling: `docs/planning/admin-portal-event-modeling-revised.md`
- Integration Gap Register: `docs/planning/admin-portal-integration-gap-register.md`

---

## Usage Guidelines

### For Historical References

When reading old retrospectives or commit messages:
1. Use this mapping to translate "Cycle N" to "M<N>.<M>"
2. Historical files remain unchanged (don't rename 100+ files)
3. Example: "See Cycle 25 retrospective" = "See M25.0 retrospective at `docs/planning/cycles/cycle-25-returns-bc-phase-1.md`"

### For New Work

1. Use new milestone IDs in all planning docs: `M30.0`, `M30.1`, etc.
2. GitHub Milestones: `M30.1: Promotions Redemption Workflow`
3. Commit messages: `(M30.1)` or `[M30.1]`
4. Retrospective files: `m30.1-retrospective.md` (lowercase, new `milestones/` folder)

### For AI Agents

When asked about cycles:
- "What was delivered in Cycle 25?" → "M25.0: Returns BC Core — see mapping above"
- "What's the current cycle?" → "Current milestone is M29.1 (Promotions BC Core, just completed)"
- "When was Cycle 19.5?" → "M19.1: Complete Checkout Workflow (2026-03-04)"

---

## Rationale for Specific Mappings

### Why M25.0/M25.1/M25.2 (not M25/M26/M27)?

Returns BC had 3 distinct "phases" delivered sequentially:
- Phase 1 (Cycle 25) = M25.0 (core lifecycle)
- Phase 2 (Cycle 26) = M25.1 (mixed inspection)
- Phase 3 (Cycle 27) = M25.2 (exchanges)

These are **incremental features** within the **same bounded context**, so they share a MAJOR number (25) with different MINOR numbers (0, 1, 2).

### Why M29.0 and M29.1 (not M29 and M30)?

Admin Identity (M29.0) and Promotions BC Core (M29.1) were delivered in the same "milestone window" (March 14-15, 2026). While they're separate BCs, they're both **foundational** for the Admin Portal epic, so they share a MAJOR number (29).

**Alternative rationale:** Could treat each BC as MAJOR (M29.0 = Admin Identity, M30.0 = Promotions BC Core). The team chose to keep them together as M29.x to reflect the tight delivery cadence.

### Why M30.0 for "Promotions BC Phase 2"?

Promotions BC Phase 2 (redemption workflow) is a **significant feature** (not just an incremental add-on). It could be:
- M29.2 (if treating as minor follow-up to M29.1)
- M30.0 (if treating as major feature deserving its own milestone)

The team chose **M30.0** to give it prominence and reflect the complexity (Shopping BC integration, Pricing BC integration, batch generation, scheduled messages).

---

**Last Updated:** 2026-03-15
**Maintainer:** Update this file when creating new milestones
