# Backoffice Frontend Design Alignment Analysis

**Date:** 2026-03-17
**Analyst Role:** Senior Technical Analyst
**Task:** Determine conflicts, alignments, and required owner decisions between the Frontend Design Document and M32.1 planning artifacts

---

## Executive Summary

**Overall Assessment:** ✅ **STRONG ALIGNMENT** with ⚠️ **MINOR ADJUSTMENTS NEEDED**

The Backoffice Frontend Design document validates and extends the M32.1 plan without fundamental conflicts. The design provides critical detail missing from the milestone plan (visual theme, navigation UX, notification model, wireframes) while respecting architectural decisions already made.

**Critical Finding:** 4 escalated blockers (E1-E4) in the design document require closure before M32.1 Session 4 (frontend build start). These are **not design conflicts** — they are **implementation gaps** that the design document correctly surfaced.

**No Event Modeling Deprecation:** The revised event model remains valid. The design document provides UI/UX details for the same backend contracts.

**Recommended Action:** Close escalated blockers E1-E3 before proceeding to M32.1 Session 4. Owner decision E4 (Phase 2 placeholder UI) is low-priority cosmetic.

---

## Step 1 — Design Document Summary

**Source:** `docs/planning/backoffice-frontend-design.md` (2026-03-17, 3/3 consensus)

### Core Decisions (Stage 2)

1. **SPA Architecture:** Blazor WASM with persistent client-side state
2. **Real-Time Model:** SignalR for dashboards/alerts, 3-tier notification system (Critical/Warning/Info)
3. **Visual Theme:** Light mode only, slate blue primary (#37474F), professional palette
4. **Navigation:** Left sidebar with role-filtered visibility, hide (not disable) inaccessible items

### Key Insights (Stage 1 — Product Owner)

- **CS agents are the highest-frequency users** (40-80 interactions/day)
- **CS landing page is a search workspace** (not a dashboard)
- **Performance targets:** Customer search < 1.5s, order detail < 3s, alert delivery < 5s
- **No extra friction for high-frequency actions** (add OrderNote, acknowledge alert)
- **Confirmation dialogs required for destructive actions** (order cancellation, return denial, stock adjustment)

### Wireframes (Stage 3)

- **Sketch 1:** CS search workspace (highest-traffic page)
- **Sketch 2:** Order detail page (most data-dense, 4 BC fan-out)
- **Sketch 3:** Operations alert feed with SignalR push
- **Sketch 4:** Role-constrained views (WarehouseClerk vs. OperationsManager)

### Escalated Blockers (Stage 4)

| ID | Description | Impact | Target |
|----|-------------|--------|--------|
| E1 | 5 notification handlers missing SignalR return types | Blocks real-time alert feed | Session 4 |
| E2 | BackofficeHub.OnConnectedAsync() role group assignment is no-op | Blocks role-scoped SignalR | Session 4 |
| E3 | JWT audience mismatch (BackofficeIdentity self-referential) | Blocks authentication | Session 4 |
| E4 | Phase 2 placeholder UI (PO preference vs. scope creep risk) | Cosmetic only | Low priority |

---

## Step 2 — Current Planning State Survey

### M32.1 Milestone Plan

**Source:** `docs/planning/milestones/m32-1-backoffice-phase-2-plan.md`

**Structure:** 16-session milestone (Sessions 1-3: gap closure, 4-8: Blazor WASM, 9-12: write operations UI, 13-15: E2E tests, 16: documentation)

**Tech Stack Decisions:**
- Blazor WASM (port 5244)
- MudBlazor component library
- In-memory JWT storage (VendorAuthState pattern)
- SignalR JWT Bearer auth with AccessTokenProvider
- Named HttpClient per BC
- Policy-based authorization (7 roles)

**Session 1 Goals:**
- Close Product Catalog write endpoint gaps
- Write ADRs 0034-0037 (M32.0 architectural decisions)
- 10+ integration tests

**Deferred to Phase 3:**
- Promotions management UI
- CSV/Excel exports
- Bulk operations pattern
- Returns analytics dashboard
- Audit log viewer

### Event Modeling (Revised)

**Source:** `docs/planning/backoffice-event-modeling-revised.md` (2026-03-14)

**Status:** 🟡 Revised Model — Awaiting Owner Sign-Off on Open Questions

**Key Decisions:**
- BFF pattern (not pure API gateway)
- EF Core for BackofficeIdentity BC (not Marten)
- Direct user creation (not invitation flow) — confirmed by Cycle 29 implementation
- BFF-owned projections for KPIs (not Analytics BC)
- OrderNote aggregate ownership (BFF-owned, not Orders BC)

**Phases:**
- Phase 0: BackofficeIdentity BC ✅ COMPLETE (Cycle 29)
- Phase 0.5: Domain BC endpoint gaps (M32.1 Sessions 1-3)
- Phase 1: Read-only dashboards (M32.0 COMPLETE)
- Phase 2: Write operations (M32.1 Sessions 4-12)
- Phase 3: Advanced features (future)

### Open Questions Document

**Source:** `docs/planning/backoffice-open-questions.md` (2026-03-14)

**Status Counts:**
- ✅ Resolved: 6
- ⚠️ Provisional: 5
- 🔴 Escalated: 2 (Q-9 Inventory HTTP layer, Q-12 Correspondence retry)

**Blocking Phase 0.5/1:**
- Q-9: Inventory BC HTTP endpoints (Option A/B/C decision)
- Q-8: Order cancellation admin attribution (extend existing vs. admin variant)
- OQ-2: Alert acknowledgment semantics (dismiss-only vs. domain event)

### CURRENT-CYCLE.md

**Source:** `docs/planning/CURRENT-CYCLE.md` (2026-03-17)

**M32.1 Status:** 🚀 IN PROGRESS — Session 1 of ~16

**Next Session:** Session 1: Gap closure + ADRs (4 ADRs to document M32.0)

**Phase 2 Blockers:** 9 endpoint gaps (Product Catalog write, Pricing write, Inventory write, Payments query)

---

## Step 3 — Conflict and Alignment Analysis

### ✅ ALIGNED — Can Proceed As-Is

1. **SPA Architecture (Decision 1)**
   - **Design:** Blazor WASM SPA with client-side routing and persistent state
   - **M32.1 Plan:** Matches Session 4-8 deliverables exactly
   - **Status:** ✅ No conflict — design validates plan

2. **Technology Stack**
   - **Design:** Blazor WASM, MudBlazor, in-memory JWT, SignalR JWT Bearer
   - **M32.1 Plan:** Identical stack specified in Session 4-8
   - **Status:** ✅ No conflict — design provides visual theme (missing from plan)

3. **Role-Based Navigation**
   - **Design:** Hide (not disable), 7 roles, domain-grouped sections
   - **M32.1 Plan:** Session 5 ("role-based menu items (7 roles)")
   - **Event Model:** Permission matrix for 7 roles
   - **Status:** ✅ No conflict — design provides navigation tree structure (missing from plan)

4. **Dashboard UI**
   - **Design:** Executive KPIs with real-time SignalR updates
   - **M32.1 Plan:** Session 6 ("5 KPI cards with real-time updates")
   - **Status:** ✅ No conflict — design provides wireframe and component details

5. **CS Workflows**
   - **Design:** Search workspace as CS landing (not dashboard), order detail with 4 BC fan-out
   - **M32.1 Plan:** Session 7 ("customer search, order detail, return approval")
   - **Status:** ✅ No conflict — design provides critical UX insight (CS agents don't care about dashboards)

6. **Write Operations Scope**
   - **Design:** Product admin, pricing admin, warehouse admin, user management
   - **M32.1 Plan:** Sessions 9-12 (identical scope)
   - **Status:** ✅ No conflict — design provides form patterns and confirmation dialog guidance

7. **Performance Targets**
   - **Design:** Customer search < 1.5s, order detail < 3s, dashboard < 2s
   - **M32.1 Plan:** No explicit performance targets
   - **Status:** ✅ Enhancement — design adds measurable acceptance criteria

---

### ⚠️ NEEDS ADJUSTMENT — Minor Changes Required

1. **CS Landing Page Route**
   - **Design:** CS agents land on `/customer-search` (search workspace), NOT `/dashboard`
   - **M32.1 Plan:** Session 5 creates `Pages/Index.razor` (home page with role-based greeting)
   - **Adjustment Needed:** `Index.razor` should redirect CS role to `/customer-search`, not show a dashboard greeting
   - **Impact:** Low — routing change only
   - **Action:** Update Session 5 routing logic to respect role-specific landing pages

2. **Alert Feed Persistence**
   - **Design:** 3-tier alert system (Critical persists until dismissed, Warning auto-resolves, Info 24h TTL)
   - **M32.1 Plan:** Session 8 ("alert acknowledgment workflow") — no TTL specified
   - **Adjustment Needed:** Add TTL configuration to `AlertFeedView` projection (Info alerts expire after 24h)
   - **Impact:** Low — projection design change
   - **Action:** Document alert lifecycle rules in Session 8 implementation

3. **Order Detail Progressive Loading**
   - **Design:** Show order header immediately (< 500ms), lazy-load tabs (shipment, messages, returns) on first click
   - **M32.1 Plan:** Session 7 creates order detail page — no progressive loading mentioned
   - **Adjustment Needed:** Implement tab lazy-loading pattern (MudTabs + on-demand HTTP fetch)
   - **Impact:** Medium — affects Session 7 implementation approach
   - **Action:** Add lazy-loading requirement to Session 7 deliverables

4. **Sound Notifications Default**
   - **Design:** Sound notifications OFF by default, opt-in toggle in user menu
   - **M32.1 Plan:** No mention of sound notifications
   - **Adjustment Needed:** Add sound toggle to user menu component (Session 5)
   - **Impact:** Low — UX polish
   - **Action:** Add to Session 5 navigation implementation (MudMenu dropdown)

5. **Connection Status Indicator**
   - **Design:** Green/yellow/red dot in app bar showing SignalR connection state
   - **M32.1 Plan:** Session 6 creates hub connection — no status indicator mentioned
   - **Adjustment Needed:** Add connection status MudBadge to MainLayout.razor
   - **Impact:** Low — UI component addition
   - **Action:** Add to Session 5 main layout deliverables

---

### 🔴 CONFLICTS — Require Replanning

**None.** All items flagged below are **escalated blockers** from the design document (Stage 4), not conflicts between design and plan.

---

### 🧭 OWNER DECISION REQUIRED

**Design Document Escalations (E1-E4):**

1. **E1: 5 Notification Handlers Missing SignalR Return Types**
   - **Context:** `LowStockDetectedHandler`, `ShipmentDeliveryFailedHandler`, `ReturnExpiredHandler`, `PaymentCapturedHandler`, `OrderCancelledHandler` update Marten projections but don't return `IBackofficeWebSocketMessage`
   - **Impact:** Blocks real-time alert feed for WarehouseClerk and OperationsManager
   - **Decision Needed:** Close before M32.1 Session 4 (frontend build start)
   - **Tradeoffs:** None — this is a clear implementation gap
   - **Recommendation:** Close immediately (1-2 hour fix)

2. **E2: BackofficeHub Role Group Assignment No-Op**
   - **Context:** `BackofficeHub.OnConnectedAsync()` connects but doesn't assign users to `role:{roleName}` groups
   - **Impact:** Blocks all role-scoped SignalR message delivery
   - **Decision Needed:** Close before M32.1 Session 4
   - **Tradeoffs:** None — this is a clear implementation gap
   - **Recommendation:** Close immediately (1 hour fix)

3. **E3: JWT Audience Mismatch**
   - **Context:** BackofficeIdentity issues tokens with self-referential audience; Backoffice.Api needs tokens with its own audience
   - **Impact:** Blocks Backoffice.Web → Backoffice.Api authentication
   - **Decision Needed:** Close before M32.1 Session 4
   - **Tradeoffs:** None — this is a clear implementation gap
   - **Recommendation:** Close immediately (consult ADR 0032, 1-2 hour fix)

4. **E4: Phase 2 Placeholder UI**
   - **Context:** PO recommends disabled/placeholder buttons for Phase 2 features (e.g., grayed "Cancel Order" with "Coming in Phase 2" tooltip); UXE neutral; PSA flags scope creep risk
   - **Impact:** Cosmetic only — does not block functionality
   - **Decision Needed:** PO to decide: placeholders vs. clean omission
   - **Tradeoffs:**
     - **Pro placeholders:** Users see what's coming, reduces support questions
     - **Con placeholders:** Each placeholder is a component to maintain, temptation to add "preview" logic
   - **Recommendation:** Clean omission (no placeholders). Add features when ready to ship, not before.

**Open Questions Document Escalations (Q-9, Q-12):**

These are **not design conflicts** — they existed before the design document was written.

5. **Q-9: Inventory BC HTTP Layer Approach**
   - **Context:** Inventory BC is entirely message-driven (zero HTTP endpoints). Should it expose HTTP endpoints, or should Backoffice BFF build projections from events?
   - **Impact:** Blocks Phase 0.5 (M32.1 Session 3)
   - **Decision Needed:** Option A (HTTP endpoints) vs. Option B (BFF projections) vs. Option C (hybrid)
   - **Tradeoffs:**
     - **Option A:** Consistency (all BCs have HTTP), clarity, but requires Inventory BC refactoring
     - **Option B:** No Inventory BC changes, but BFF becomes tightly coupled to Inventory events
     - **Option C:** BFF projections for dashboards (real-time), HTTP for writes (on-demand)
   - **Recommendation:** Option A — consistency matters. Every other BC has HTTP endpoints. Schedule Inventory BC HTTP layer work alongside M32.1 Session 3.

6. **Q-12: Correspondence Manual Retry**
   - **Context:** If CS sees a failed email, can they trigger manual retry? No "resend" endpoint exists today.
   - **Impact:** Blocks decision on Phase 1 Correspondence scope (read-only vs. interactive)
   - **Decision Needed:** Read-only (Option A) vs. add resend endpoint (Option B/C)
   - **Tradeoffs:**
     - **Option A:** No backend work, CS tells customer to check spam (acceptable workaround)
     - **Option B:** Cleaner (HTTP endpoint), more backend work
   - **Recommendation:** Option A for Phase 1, defer manual retry to Phase 2 if needed

---

## Step 4 — Deprecation Check

### Event Modeling Outputs

**Question:** Are any Event Modeling outputs (event maps, slices, scenarios, swim lanes) now deprecated or materially incorrect in light of the design document?

**Answer:** ❌ **NO DEPRECATION**

The revised event model (`docs/planning/backoffice-event-modeling-revised.md`) remains valid. The design document provides **UI/UX details for the same backend contracts**.

**Evidence:**

1. **Commands/Queries Match:**
   - Event model: `GetCustomerServiceView`, `GetOrderDetails`, `CancelOrder`, `ApproveReturn`, `DenyReturn`
   - Design wireframes: Same operations shown in Sketches 1-2

2. **Integration Messages Match:**
   - Event model: `OrderPlaced`, `PaymentCaptured`, `LowStockDetected`, `ShipmentDeliveryFailed` → SignalR push
   - Design Decision 2: Identical messages listed in SignalR table

3. **Projections Match:**
   - Event model: `AdminDailyMetrics`, `AlertFeedView` (BFF-owned Marten projections)
   - Design Sketch 3: Dashboard KPIs and alert feed sourced from these projections

4. **Roles Match:**
   - Event model: 7 roles (CopyWriter, PricingManager, WarehouseClerk, CustomerService, OperationsManager, Executive, SystemAdmin)
   - Design Decision 4: Same 7 roles in navigation visibility matrix

**What the Design Document Adds (Not Conflicts):**

- Visual theme and color palette (missing from event model)
- Navigation tree structure and layout (missing from event model)
- Wireframe sketches showing component composition (missing from event model)
- Performance targets (missing from event model)
- Alert notification tiers and lifecycle (missing from event model)
- Progressive loading patterns (missing from event model)

**Conclusion:** The design document is a **UI/UX layer on top of the event model**, not a replacement or contradiction.

---

### Feature Specifications (Gherkin)

**Status:** No `.feature` files exist yet for Backoffice. The design document provides wireframes that **should inform** Gherkin scenario writing (M32.1 Session 13-15).

**Recommendation:** Write Gherkin scenarios during E2E test planning (Sessions 13-14) using design wireframes as acceptance criteria.

---

## Step 5 — Summary and Recommended Actions

### What Is Aligned (Proceed As-Is)

- **Technology stack:** Blazor WASM, MudBlazor, JWT, SignalR — all decisions match
- **Architecture:** BFF pattern, role-based auth, SignalR real-time — no conflicts
- **Scope:** Read-only dashboards (Phase 1 complete), write operations (Phase 2), E2E tests — matches M32.1 plan
- **Event model validity:** Revised event model remains authoritative for backend contracts

**Summary:** The design document validates and extends M32.1 planning without introducing conflicts. Proceed to Session 1 (gap closure + ADRs).

---

### What Needs Adjustment (Clear Actions)

1. **CS landing page routing** (Session 5)
   - Action: Redirect CS role to `/customer-search`, not dashboard
   - Impact: Low — routing logic change

2. **Alert TTL configuration** (Session 8)
   - Action: Add 24-hour TTL for Info alerts in `AlertFeedView` projection
   - Impact: Low — projection design

3. **Order detail progressive loading** (Session 7)
   - Action: Implement tab lazy-loading (MudTabs + on-demand fetch)
   - Impact: Medium — implementation approach change

4. **Sound notifications toggle** (Session 5)
   - Action: Add opt-in sound toggle to user menu (default OFF)
   - Impact: Low — UI component addition

5. **Connection status indicator** (Session 5)
   - Action: Add SignalR connection status MudBadge to app bar
   - Impact: Low — UI component addition

**Summary:** 5 minor adjustments, all low-to-medium impact. No replanning required.

---

### What Requires Replanning

**None.** No fundamental conflicts exist between design document and M32.1 plan.

---

### What Requires Owner Decision (Blockers)

**Priority 1 (MUST CLOSE BEFORE SESSION 4):**

1. **E1: Add SignalR return types to 5 notification handlers**
   - Blocking: Real-time alerts for WH and Ops roles
   - Estimated: 1-2 hours
   - Action: Modify handlers to return `IBackofficeWebSocketMessage` events

2. **E2: Implement BackofficeHub role group assignment**
   - Blocking: All role-scoped SignalR delivery
   - Estimated: 1 hour
   - Action: Add `await Groups.AddToGroupAsync(connectionId, $"role:{roleName}")` in `OnConnectedAsync()`

3. **E3: Fix JWT audience configuration**
   - Blocking: Backoffice.Web → Backoffice.Api authentication
   - Estimated: 1-2 hours
   - Action: Review ADR 0032, configure BackofficeIdentity to issue tokens with Backoffice.Api audience

**Priority 2 (RESOLVE DURING SESSION 1-3):**

4. **Q-9: Inventory BC HTTP endpoints approach**
   - Blocking: Phase 0.5 (M32.1 Session 3)
   - Recommended: Option A (add HTTP endpoints to Inventory.Api)
   - Rationale: Consistency with all other BCs

5. **Q-12: Correspondence manual retry capability**
   - Blocking: Phase 1 Correspondence scope decision
   - Recommended: Option A (read-only for Phase 1)
   - Rationale: Manual retry is nice-to-have, not day-one critical

**Priority 3 (LOW — COSMETIC):**

6. **E4: Phase 2 placeholder UI**
   - Not blocking: Cosmetic preference
   - Recommended: Clean omission (no placeholders)
   - Rationale: Avoid scope creep and maintenance burden

---

## Conclusion

**Final Assessment:** The Backoffice Frontend Design document is **production-ready guidance** that strengthens the M32.1 plan without contradicting it. The design provides critical UI/UX detail missing from the milestone plan while respecting all architectural decisions already made.

**Next Steps:**

1. **Close E1-E3 blockers immediately** (4-5 hours total) before Session 4
2. **Proceed to M32.1 Session 1** (gap closure + ADRs) as planned
3. **Incorporate design adjustments** during Sessions 5-8 (frontend build)
4. **Use design wireframes as acceptance criteria** for E2E tests (Sessions 13-15)

**No replanning required.** The milestone structure remains valid.

---

*Analysis Date: 2026-03-17*
*Analyst: Senior Technical Analyst*
*Status: Ready for Owner Review*
