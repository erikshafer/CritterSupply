# Admin Portal — Revised Decision Log

**Date:** 2026-03-14 (Re-modeling session)
**Status:** Companion to [Revised Event Model](admin-portal-event-modeling-revised.md)
**Participants:** Principal Software Architect (PSA), Product Owner (PO), UX Engineer (UXE)

> This document records every original modeling decision that was revisited during the re-modeling session. For each, we state what was kept, changed, or removed — and the rationale.

---

## Reading Key

| Tag | Meaning |
|-----|---------|
| **KEPT** | Original decision confirmed as still valid |
| **CHANGED** | Decision revised based on new information |
| **REMOVED** | Decision no longer applicable |

| Consensus | Meaning |
|-----------|---------|
| ✅ 3/3 | All three agents agree |
| ⚠️ 2/3 | Majority; dissent noted |
| 🔴 Escalated | Could not reach consensus; owner must decide |

---

## Architecture Decisions

### D-1: BFF Pattern for Admin Portal
- **Original:** Admin Portal is a BFF that composes views from multiple BCs
- **Status:** **KEPT** ✅ 3/3
- **Rationale:** Pattern validated by both Customer Experience BFF (Storefront) and Vendor Portal BFF. Consistent architecture.

### D-2: Admin Portal Does Not Own Business Logic
- **Original:** BFF delegates to domain BCs; does not execute business logic
- **Status:** **KEPT** ✅ 3/3
- **Rationale:** Confirmed by codebase audit — all business logic lives in domain BCs. Admin Portal composes and orchestrates.

### D-3: Single SignalR Hub with Role-Based Groups
- **Original (research doc):** Single hub at `/hub/admin`, role-based groups with supervisory inheritance
- **Status:** **KEPT** ✅ 3/3
- **Rationale:** Vendor Portal's single-hub pattern (`/hub/vendor-portal`) works well. Supervisory group inheritance (OperationsManager joins WH + CS groups) is elegant.

### D-4: JWT Authentication via AdminIdentity BC
- **Original:** Separate AdminIdentity BC with JWT tokens
- **Status:** **KEPT** ✅ 3/3 — **Now implemented** (Cycle 29 Phase 1)
- **Rationale:** Mirrors VendorIdentity pattern. No tenant concept needed for internal employees. 7-role AdminRole enum confirmed.
- **Implementation note:** JWT claims: `sub` (userId GUID), `role` (single role string), `email`, `name`. Issuer: `https://localhost:5249`. Audience: self-referential in Phase 1 (to be updated for Admin Portal API in Phase 2+). See [ADR 0031](../decisions/0031-admin-portal-rbac-model.md).

### D-5: Single Role Per Admin User
- **Original (research doc §11):** Single role per user; OperationsManager as "Swiss Army knife" for small teams
- **Status:** **KEPT** ✅ 3/3 — **Now implemented** (Cycle 29 Phase 1)
- **Rationale:** PO decision confirmed. Revisit multi-role composition at 50+ admin users.
- **Implementation note:** `AdminRole` enum enforces single role at entity level. `AdminUser.Role` is a single enum value, not a collection.

---

## Frontend Technology

### D-6: Frontend Framework Choice
- **Original (event model):** React/Next.js SSR recommended
- **Status:** **CHANGED** ✅ 3/3
- **Revised:** **Blazor WASM**
- **Rationale:**
  - Research doc override (§4) is authoritative — scored Blazor WASM 5-2 over React on weighted criteria
  - Vendor Portal WASM pattern is fully proven (143 tests, 100% pass rate)
  - Code reuse: AuthState, AuthStateProvider, HttpClient patterns, MudBlazor components, SignalR client all transfer directly
  - Counter-proposal accepted: Operations Dashboard BC (developer-facing) is a better React candidate
  - **UXE sign-off:** UX research document (admin-portal-ux-research.md) already uses MudBlazor component names throughout — no rework needed

### D-7: SSR Concern (Original React Justification)
- **Original:** React's SSR advantage for first paint
- **Status:** **REMOVED** ✅ 3/3
- **Rationale:** Admin Portal is an internal tool on managed desktops (Chrome/Edge). WASM payload is cached after first load. SSR advantage is minimal for this use case. Research doc §4 explicitly addresses this.

---

## Phasing Decisions

### D-8: Phase 1 Scope — Returns BC Integration
- **Original:** Returns BC listed as "Phase 2" with "HTTP read (return history)" only
- **Status:** **CHANGED** ✅ 3/3
- **Revised:** Returns BC is Phase 1 with both read AND write (approve/deny)
- **Rationale:**
  - Returns/refund initiation is 20-25% of CS tickets (Rank #2)
  - Returns BC is fully implemented with all 9 needed endpoints
  - CS agents have a runbook but no UI to execute it
  - Return approval/denial is a CS day-one need — these endpoints already exist
  - **PO validation:** "This is the single most obvious re-prioritization"

### D-9: Phase 1 Dependency — Analytics BC
- **Original:** Executive dashboard depends on Analytics BC for projections
- **Status:** **CHANGED** ✅ 3/3
- **Revised:** Analytics BC dependency **removed entirely**. Dashboard KPIs sourced from BFF-owned Marten projections.
- **Rationale:**
  - Analytics BC is 🟢 Low Priority with no implementation timeline
  - No `src/Analytics/` directory exists
  - BFF can subscribe to `OrderPlaced`, `PaymentFailed`, etc. via RabbitMQ and maintain its own lightweight projections
  - The original event model actually hinted at this approach (Flow 4: "Update AdminMetrics Marten document") but contradicted it in the dependency table

### D-10: Phase 3 Dependency — Store Credit BC
- **Original:** Phase 3 includes "CustomerService store credit issuance"
- **Status:** **CHANGED** ✅ 3/3
- **Revised:** Removed from Phase 3. Moved to Phase 4+ (blocked indefinitely).
- **Rationale:**
  - Store Credit BC is 🟡 Medium Priority with no timeline
  - PO decision (research doc §11): manual tracking via order notes is the interim workflow
  - Order notes aggregate (AdminPortal-owned) serves as the workaround
  - Store credit becomes urgent only if return policy changes to store-credit-default

### D-11: New Phase 0.5 — Domain BC Endpoint Gaps
- **Original:** Not in original model
- **Status:** **NEW** ✅ 3/3
- **Rationale:**
  - Codebase audit revealed 8 Phase 0.5 blockers across 6 BCs
  - Lesson from Cycle 26: Fulfillment queue wiring bug discovered mid-cycle blocked integration work
  - Pre-building domain BC endpoints prevents repeating that pattern
  - 4-5 sessions of focused work to close all Phase 0.5 gaps

### D-12: Correspondence BC in Phase 1
- **Original:** Not in original model (Correspondence BC didn't exist)
- **Status:** **NEW** ✅ 3/3
- **Rationale:**
  - Correspondence BC Phase 1 shipped (Cycle 28)
  - Both query endpoints exist: `GET /api/correspondence/messages/customer/{id}` and `GET /api/correspondence/messages/{id}`
  - "Did the customer get their order confirmation?" is a real CS call pattern
  - < 1 session effort to add a "Messages" tab in CS customer detail view
  - **UXE sign-off:** Natural fit in the customer detail view layout

### D-13: Promotions BC Integration
- **Original:** Not in original model
- **Status:** **NEW** ✅ 3/3
- **Revised:** Phase 2 (read-only), Phase 3 (write)
- **Rationale:**
  - Promotions BC shipping Cycle 29 — will exist before Admin Portal Phase 2
  - PricingManager owns promotions (no separate role until 50+ users)
  - Read-only view in Phase 2: "See active coupons while setting prices"
  - Write operations in Phase 3: create/deactivate promotions

---

## Port & Numbering Decisions

### D-14: AdminIdentity.Api Port
- **Original (research doc):** Port 5245
- **Status:** **CHANGED** ✅ 3/3 — **Confirmed by implementation** (launchSettings.json: `http://localhost:5249`)
- **Revised:** Port **5249**
- **Rationale:** Returns.Api launched on port 5245 (Cycle 25). Collision. Next available port: 5249 (5243=Admin Portal.Api, 5244=Admin Portal.Web, 5245=Returns, 5246-5248 reserved/used).

### D-15: ADR Numbers for Admin Portal
- **Original (research doc):** ADRs 0026-0029
- **Status:** **CHANGED** ✅ 3/3 — **ADR 0031 now exists** (`docs/decisions/0031-admin-portal-rbac-model.md`)
- **Revised:** ADRs **0031-0034**
- **Rationale:** ADRs 0026-0030 were taken by: Polecat SQL Server (0026), Per-BC Postgres DBs (0027), JWT for Vendor Identity (0028), Order Saga Design (0029), Notifications→Correspondence rename (0030). Next available: 0031.
  - ADR 0031: Admin Portal RBAC Model — ✅ **Accepted** (Cycle 29 Phase 1)
  - ADR 0032: Multi-Issuer JWT Strategy — 📋 Reserved
  - ADR 0033: Blazor WASM for Admin Portal — 📋 Reserved
  - ADR 0034: Admin Portal SignalR Hub Design — 📋 Reserved

---

## Role & Permission Decisions

### D-16: PricingManager Owns Promotions
- **Original:** No promotions role defined
- **Status:** **NEW** ✅ 3/3
- **Rationale:** PO decision — pricing and promotions are "commercial terms" owned by one person in small teams. No separate PromotionsManager until team exceeds 50 admin users.

### D-17: Return Approval Authority
- **Original:** Not addressed (Returns was Phase 2 read-only)
- **Status:** **NEW** ✅ 3/3
- **Revised:** CustomerService + OperationsManager + SystemAdmin can approve/deny returns
- **Rationale:** Consistent with order cancellation pattern — CS handles the customer interaction, Ops has supervisory authority.

### D-18: WarehouseClerk Return Scope
- **Original:** Not addressed
- **Status:** **NEW** ✅ 3/3
- **Revised:** WarehouseClerk sees: items pending inspection + own inspection history. Does NOT see full return lifecycle or CS approval/denial flow.
- **Rationale:** Information minimization. Clerk needs to know "what do I inspect?" not "why was this return approved?" Full lifecycle is CS/Ops territory.

### D-18a: Password Hashing Algorithm [NEW — Resolved by Implementation]
- **Original (research doc §2):** Argon2id assumed
- **Status:** **CHANGED** (resolved by Cycle 29 Phase 1 implementation)
- **Revised:** **PBKDF2-SHA256** via ASP.NET Core Identity `PasswordHasher<T>` (100,000 iterations by default)
- **Rationale:** ADR 0031 explicitly documents this decision: "PBKDF2-SHA256 via ASP.NET Core Identity's `PasswordHasher<T>` ... Argon2id would require a custom `IPasswordHasher<T>` implementation and is deferred to Phase 2+ if needed." PBKDF2 is the default provided by ASP.NET Core Identity and is well-tested.

### D-18b: User Provisioning — Direct Creation [NEW — Resolved by Implementation]
- **Original (research doc §11):** Invitation flow (72-hour token, email link, self-service password setup)
- **Status:** **CHANGED** (resolved by Cycle 29 Phase 1 implementation)
- **Revised:** **Direct creation by SystemAdmin** via `POST /api/admin-identity/users`. SystemAdmin provides email, password, first/last name, and role in the request body.
- **Rationale:** The implementation chose the simpler direct-creation pattern. This resolves the discrepancy in the original research doc (§11 PO Decision #3 said invitation flow; Appendix B said direct creation). **Direct creation is the implemented and correct answer.**
- **Note:** If invitation flow is desired in the future, it can be added as a separate endpoint without changing the existing CreateAdminUser handler.

---

## Naming Decisions

### D-19: Notifications → Correspondence
- **Original:** "Notifications BC" referenced in original model
- **Status:** **CHANGED** ✅ 3/3
- **Revised:** "Correspondence BC" per ADR 0030
- **Rationale:** Avoid ambiguity with real-time UI notifications (handled by SignalR in Customer Experience BC). "Correspondence" specifically means transactional customer communications.

### D-20: LowStockAlert → LowStockDetected
- **Original:** "LowStockAlert" used in event model
- **Status:** **CHANGED** ✅ 3/3
- **Revised:** `LowStockDetected` — the actual name in `src/Shared/Messages.Contracts/Inventory/LowStockDetected.cs`
- **Rationale:** Codebase is source of truth for integration message names.

### D-21: AdminRole Enum Values
- **Original (event model):** CopyWriter, PricingManager, WarehouseClerk, CustomerService, OperationsManager, Executive, SystemAdmin
- **Status:** **KEPT** ✅ 3/3 — **Confirmed by implementation** (`AdminIdentity.Identity.AdminRole` enum)
- **Rationale:** All 7 roles confirmed across research doc, UX research, feature files, AND now codebase. Enum values: CopyWriter=1, PricingManager=2, WarehouseClerk=3, CustomerService=4, OperationsManager=5, Executive=6, SystemAdmin=7.
- **UXE sign-off:** Role names used in UX wireframes match. CopyWriter (not "ContentEditor"), CustomerService (not "CSRep"), WarehouseClerk (not "Warehouse").

---

## Aggregate Decisions

### D-22: OrderNote Aggregate Ownership
- **Original:** Not in original model
- **Status:** **NEW** ✅ 3/3
- **Revised:** OrderNote lives in Admin Portal BC, NOT Orders BC
- **Rationale:** Internal CS notes are operational tooling metadata, not part of the order's business lifecycle. Orders BC events should not be polluted with `OrderNoteAdded`. If customer-visible notes are needed later, that's a separate aggregate in Orders BC with different semantics.

### D-23: Admin Portal Has No Sagas
- **Original:** No sagas identified
- **Status:** **KEPT** ✅ 3/3
- **Rationale:** Reassessment confirms: Admin Portal delegates orchestration to domain BCs (Order saga, BulkPricingJob saga). Admin Portal owns only simple Marten documents (OrderNote, AlertAcknowledgment, EscalationTicket) with straightforward state transitions.

---

## Removed Decisions

### D-24: React/Next.js Frontend (Removed)
- **Original:** React/Next.js recommended for "exploring non-Blazor frontend technology"
- **Status:** **REMOVED** ✅ 3/3
- **Rationale:** Conflicted with delivery goal. Blazor WASM wins on velocity, code reuse, and reference architecture coherence. React exploration deferred to Operations Dashboard BC.

### D-25: Analytics BC Dependency (Removed)
- **Original:** Phase 1 depends on Analytics BC for executive dashboard projections
- **Status:** **REMOVED** ✅ 3/3
- **Rationale:** Analytics BC does not exist, is 🟢 Low Priority, and has no timeline. BFF-owned projections are the correct approach.

### D-26: Store Credit BC Dependency (Removed from Phasing)
- **Original:** Phase 3 depends on Store Credit BC
- **Status:** **REMOVED** from active phasing ✅ 3/3
- **Rationale:** Store Credit BC has no timeline. Order notes workaround is sufficient. Moved to Phase 4+.

---

## Summary

| Category | Kept | Changed | Removed | New |
|----------|------|---------|---------|-----|
| Architecture | 5 | 0 | 0 | 0 |
| Frontend | 0 | 1 | 1 | 0 |
| Phasing | 0 | 3 | 0 | 3 |
| Port/Numbering | 0 | 2 | 0 | 0 |
| Roles/Permissions | 0 | 0 | 0 | 3 |
| Naming | 1 | 2 | 0 | 0 |
| Aggregates | 0 | 0 | 0 | 2 |
| Removed | 0 | 0 | 3 | 0 |
| **Implementation-confirmed** | 0 | 2 | 0 | 0 |
| **Total** | **6** | **10** | **4** | **8** |

All original 26 decisions reached **3/3 consensus**. 2 additional decisions (D-18a: password hashing, D-18b: user provisioning) were resolved by the Cycle 29 Phase 1 implementation.

> **Post-implementation update (2026-03-14):** Decisions D-4, D-5, D-14, D-15, and D-21 have been confirmed by the Admin Identity BC implementation (PR #375). D-18a and D-18b are new decisions that emerged from comparing the original research doc assumptions against the actual implementation.

See [Open Questions](admin-portal-open-questions.md) for items that require additional information or owner sign-off.
