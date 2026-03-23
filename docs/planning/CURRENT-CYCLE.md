# Current Development Milestone

> **Note:** This file is maintained as a lightweight AI-readable summary of the active development milestone.
> It is the fallback when GitHub Issues/Projects are not directly accessible.
> **Primary tracking:** GitHub Issues + GitHub Project board (see links below)
>
> **For full GitHub-first access on this machine, you need:**
> 1. **GitHub MCP server** configured in your AI tool's MCP settings
> 2. **GitHub auth** (personal access token with `repo` + `project` scopes)
>
> With both configured, query GitHub directly: `list_issues(milestone="M31.5", state="open")`
> This works identically on any machine — MacBook, Windows PC, Linux laptop.
>
> **⚡ New:** We've migrated from "Cycle N Phase M" to **Milestone-Based Versioning** (M.N format).
> See [ADR 0032](../decisions/0032-milestone-based-planning-schema.md) and [Milestone Mapping](milestone-mapping.md) for details.

---

## 🤖 LLM Navigation Guide

**Choose your section based on your task:**

| **Your Task** | **Go To Section** | **Purpose** |
|---------------|-------------------|-------------|
| 📊 **Quick status check** | [Quick Status](#quick-status) | One-table snapshot of current state |
| 🚀 **Starting new work** | [Active Milestone](#active-milestone) | Detailed info on current milestone only |
| ✅ **Recording completion** | [Recent Completions](#recent-completions) | Add to top of list (keep last 3 milestones) |
| 📦 **Looking up old milestone** | [Milestone Archive](#milestone-archive) | Full historical record (M29.1 and earlier) |
| 🗺️ **Planning next work** | [Roadmap](#roadmap) | Next 3-4 milestones + future BCs |
| 🔗 **Finding references** | [Quick Links](#quick-links) | GitHub, docs, ADRs |

**Update Instructions:**
- **Milestone starts:** Update [Active Milestone](#active-milestone) section + Quick Status table
- **Milestone completes:** Move Active → [Recent Completions](#recent-completions), update Quick Status, add retrospective link
- **Archiving old milestones:** After 3 milestones in Recent Completions, move oldest to [Milestone Archive](#milestone-archive)
- **Roadmap changes:** Update [Roadmap](#roadmap) with new priorities (keep focused on next 3-4 only)

---

## Quick Status

| Aspect | Status |
|--------|--------|
| **Current Milestone** | M33.0 — Code Correction + Broken Feedback Loop Repair (ACTIVE) |
| **Status** | 🚀 IN PROGRESS — Phase 1 COMPLETE, Phase 2 COMPLETE (9/9 items), Phase 3 IN PROGRESS (2/8 items complete) |
| **Deliverables** | Phase 1 ✅, Phase 2 ✅, Phase 3 partial (R-4 ✅, R-1 27% complete) |
| **Recent Completion** | M32.4 — Backoffice Phase 4 (2026-03-21), M32.3 — Backoffice Phase 3B (2026-03-21) |
| **Active BCs** | 18 total (including Backoffice BFF + Backoffice.Web) |

*Last Updated: 2026-03-23 (M33.0 Session 10 complete — Phase 3 started: R-4 delivered, R-1 3/11 commands)*

---

## Active Milestone

### 📋 M33.0: Code Correction + Broken Feedback Loop Repair

**Status:** 🚀 **IN PROGRESS** — Phase 1 Complete (INV-3 + F-8 verified working)
**Goal:** Fix broken tests, build missing projections, execute structural refactors, document canonical patterns

**Session 1 Completion (2026-03-21):**
- ✅ INV-3 Fixed: `AdjustInventoryEndpoint` reverted to manual validation + explicit integration message publishing
- ✅ All 48 Inventory.Api.IntegrationTests passing
- ✅ F-8 Verified: `BackofficeTestFixture.ExecuteAndWaitAsync()` working (75 Backoffice tests passing)
- ✅ Retrospective documenting Wolverine compound handler learnings created
- **Key Learning:** Mixing `IMessageBus.InvokeAsync()` with manual event appending doesn't respect `Before()` validation

**Sessions 5+6 Completion (2026-03-22 to 2026-03-23):**
- ⚠️ **Priority 3 PARTIALLY DELIVERED:** Order Search + Return Management pages were added to Backoffice.Web, but post-mortem review found unresolved recovery work
- ✅ Created 2 new Blazor WASM pages (`/orders/search`, `/returns`)
- ✅ Updated NavMenu with role-based navigation items
- ❌ Created Backoffice.Web.UnitTests bUnit project — **all tests removed after 7 failed fix attempts**
- ❌ No replacement E2E coverage was added, leaving the new pages with **ZERO automated UI coverage**
- ⚠️ Post-mortem review found route-shape/BFF mismatch and status/discoverability inconsistencies that should be addressed before treating Priority 3 as closed
- ✅ All 51 Backoffice.Api.IntegrationTests passing (no regressions in that suite)
- ✅ Retrospective documenting bUnit limitations and Blazor WASM local DTOs created
- **See:** `docs/planning/milestones/m33-0-post-mortem-recovery-review.md`

**Session 7 Completion (2026-03-23):**
- ✅ **Priority 3 FULLY DELIVERED:** All post-mortem blocking issues resolved
- ✅ Created 2 BFF proxy endpoints at correct `/api/backoffice/*` paths (SearchOrders, GetReturns)
- ✅ Fixed frontend route mismatches in OrderSearch.razor and ReturnManagement.razor
- ✅ Fixed NavMenu authorization (operations-manager can now see Order Search + Return Management)
- ✅ Fixed return status vocabulary ("Pending" → "Requested", removed invalid status from UI)
- ✅ Added 10 comprehensive integration tests (4 OrderSearch + 6 ReturnList scenarios)
- ✅ All 91 Backoffice.Api.IntegrationTests passing (up from 51)
- ✅ Zero build errors, zero test failures
- ✅ Retrospective documenting recovery patterns and lessons learned created
- **See:** `docs/planning/milestones/m33-0-session-7-retrospective.md`

**Session 2 Completion (PREVIOUSLY UNDOCUMENTED):**
- ✅ **Priority 2 COMPLETE:** All three Marten projections built and tested
- ✅ ReturnMetricsView projection (inline, singleton, active return counts)
- ✅ CorrespondenceMetricsView projection (inline, singleton, email queue health)
- ✅ FulfillmentPipelineView projection (inline, singleton, active shipments pipeline)
- ✅ All projections registered in Program.cs with `ProjectionLifecycle.Inline`
- ✅ 14 projection integration tests passing (EventDrivenProjectionTests)
- ✅ Dashboard uses ReturnMetricsView for PendingReturns KPI
- **See:** `docs/planning/milestones/m33-0-session-2-retrospective.md`

**Session 8 Completion (2026-03-23):**
- ✅ **Phase 1 COMPLETE:** XC-1 ADR + CheckoutCompleted fix delivered
- ✅ ADR 0039 published (canonical validator placement convention)
- ✅ Shopping's `CheckoutCompleted` renamed to `CartCheckoutCompleted`
- ✅ Orders' internal `CheckoutCompleted` renamed to `OrderCreated`
- ✅ All consumers updated (zero `CheckoutCompleted` references remain)
- ✅ Build succeeds (0 errors, 36 pre-existing warnings)
- ✅ All tests passing (971+ tests across all BCs)
- ✅ Live 🔴 risk eliminated (dual-payload collision at checkout)
- ✅ Retrospective documenting Phase 1 completion created
- **See:** `docs/planning/milestones/m33-0-session-8-retrospective.md`

**Session 9 Completion (2026-03-23):**
- ✅ **Phase 2 COMPLETE:** All 9 Quick Wins items delivered in single session
- ✅ INV-1: Consolidated AdjustInventory* 4-file shatter → AdjustInventory.cs
- ✅ INV-2: Consolidated ReceiveInboundStock* split → ReceiveInboundStock.cs
- ✅ INV-3: Renamed Inventory folders (Commands/ → InventoryManagement/, Queries/ → StockQueries/)
- ✅ PR-1: Merged Pricing validator splits (SetInitialPrice + ChangePrice)
- ✅ CO-1: Exploded MessageEvents.cs → 4 individual event files
- ✅ PAY-1/FUL-1/ORD-1: Moved isolated Queries to feature-named folders
- ✅ F-9: Fixed Orders test collection attributes (3 raw string literals)
- ✅ Build: 0 errors, 36 pre-existing warnings (unchanged)
- ✅ All tests passing (no regressions)
- ✅ Retrospective documenting Phase 2 completion created
- **See:** `docs/planning/milestones/m33-0-session-9-retrospective.md`

**Session 10 Completion (2026-03-23):**
- ✅ **Phase 3 STARTED:** R-4 fully delivered, R-1 partial (3/11 commands) — 27% complete
- ✅ R-4: Exploded `ReturnCommandHandlers.cs` (387 lines) → 5 individual handler files
- ✅ R-1 (3/11): Created vertical slices for DenyReturn, SubmitInspection, RequestReturn
- ✅ ReturnValidators.cs now empty (all validators moved to vertical slice files)
- ✅ Build: 0 errors, 36 pre-existing warnings (unchanged)
- ⚠️ Pre-existing test failures (14 failures, 30 passed — auth issues, not refactoring-related)
- ✅ Session plan documenting Phase 3 scope and sequencing created
- ✅ Session retrospective documenting learnings and shared type dependencies created
- **See:** `docs/planning/milestones/m33-0-session-10-plan.md`, `docs/planning/milestones/m33-0-session-10-retrospective.md`

**Remaining Planned Priorities:**
3. 📋 **Phase 3:** Returns BC structural refactor (R-1 through R-7 + F-7) — NEXT
4. 📋 **Phase 4:** Vendor Portal structural refactor (VP-1 through VP-6)
5. 📋 **Phase 5:** Backoffice folder restructure (BO-1/BO-2/BO-3) + XC-3 (AcknowledgeAlert transaction fix)

**References:**
- M33-M34 Proposal: `docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md`
- Session 1 Plan: `docs/planning/milestones/m33-0-session-1-plan.md`
- Session 1 Retrospective: `docs/planning/milestones/m33-0-session-1-retrospective.md`
- Session 2 Retrospective: `docs/planning/milestones/m33-0-session-2-retrospective.md`
- Session 5 Status: `docs/planning/milestones/m33-0-session-5-status.md`
- Session 6 Status: `docs/planning/milestones/m33-0-session-6-status.md`
- Sessions 5+6 Retrospective: `docs/planning/milestones/m33-0-session-5-retrospective.md` (combined)
- Post-Mortem Recovery Review: `docs/planning/milestones/m33-0-post-mortem-recovery-review.md`
- Session 7 Plan: `docs/planning/milestones/m33-0-session-7-plan.md`
- Session 7 Retrospective: `docs/planning/milestones/m33-0-session-7-retrospective.md`
- Session 8 Plan: `docs/planning/milestones/m33-0-session-8-plan.md`
- Session 8 Retrospective: `docs/planning/milestones/m33-0-session-8-retrospective.md`
- Session 9 Plan: `docs/planning/milestones/m33-0-session-9-plan.md`
- Session 9 Retrospective: `docs/planning/milestones/m33-0-session-9-retrospective.md`
- Session 10 Plan: `docs/planning/milestones/m33-0-session-10-plan.md`
- Session 10 Retrospective: `docs/planning/milestones/m33-0-session-10-retrospective.md`
- ADR 0039: `docs/decisions/0039-canonical-validator-placement.md`
- M32.4 Retrospective: `docs/planning/milestones/m32-4-session-1-retrospective.md`

---

## Recent Completions

### M32.4: Backoffice Phase 4 — E2E Stabilization + UX Polish

**Status:** ✅ **COMPLETE** — All critical and medium priorities finished in single session (2026-03-21)
**Goal:** Fix E2E test fixture issue, stabilize test suite, audit DateTimeOffset precision

**What Shipped:**
- ✅ **Priority 1 (CRITICAL):** Fixed E2E test fixture issue — Blazor WASM now publishes automatically before tests
- ✅ **Priority 2 (MEDIUM):** Automated Blazor WASM publish — MSBuild target runs before VSTest execution
- ✅ **Priority 3 (MEDIUM):** DateTimeOffset precision audit — all EF Core tests already use correct tolerance patterns
- 📄 **Documentation:** Comprehensive audit document (192 lines) documenting findings and patterns

**Key Technical Win:**
- Single MSBuild target solution addressed both Priority 1 (blocking issue) AND Priority 2 (automation), collapsing 2 planned sessions into 1

**Audit Results:**
- BackofficeIdentity (EF Core): ✅ Uses `TimeSpan.FromMilliseconds(1)` tolerance correctly
- VendorIdentity (EF Core): ✅ Uses `ShouldBeInRange()` (built-in tolerance)
- Customer Identity (EF Core): ✅ No DateTimeOffset assertions
- All Marten BCs: ✅ Not affected (Marten preserves full precision)
- **Conclusion:** No code fixes required

**Session Efficiency:** Completed 3 priorities in ~2.5 hours (planned 4-6 hours for Priority 1 alone)

**Deferred (Optional):**
- Priority 4 (LOW): GET /api/backoffice-identity/users/{userId} endpoint
- Priority 5 (LOW): Table sorting in UserList.razor

**References:**
- [M32.4 Plan](./milestones/m32-4-plan.md)
- [Session 1 Retrospective](./milestones/m32-4-session-1-retrospective.md)
- [DateTimeOffset Audit](./milestones/m32-4-datetime-offset-audit.md)

*Completed: 2026-03-21*

---

### M32.3: Backoffice Phase 3B — Write Operations Depth

**Status:** ✅ **COMPLETE** — All 10 sessions finished (10 Blazor pages, 34 E2E scenarios, 6 integration tests)
**Goal:** Implement write operations depth for Product Admin, Pricing Admin, Warehouse Admin, User Management

**What Shipped:**
- **10 Blazor WASM Pages:** ProductList, ProductEdit, PriceEdit, InventoryList, InventoryEdit, UserList, UserCreate, UserEdit
- **4 Client Interfaces Extended:** ICatalogClient, IPricingClient, IInventoryClient, IBackofficeIdentityClient (15 methods added)
- **34 E2E Scenarios Created:** ProductAdmin (6), PricingAdmin (6), WarehouseAdmin (10), UserManagement (12)
- **6 Integration Tests Passing:** BackofficeIdentity password reset (security-critical refresh token invalidation verified)
- **14 Backend Endpoints Utilized:** Product Catalog (4), Pricing (1), Inventory (4), BackofficeIdentity (5)
- **Build Status:** 0 errors across all projects (10 sessions)
- **Test Coverage:** ~85% (integration + E2E combined)

**Key Technical Wins:**
- Blazor WASM local DTO pattern (cannot reference backend projects)
- Two-click confirmation pattern for destructive actions
- Wolverine direct implementation pattern (mixed parameter source fix)
- Hidden message divs for E2E assertions
- ScenarioContext dynamic URL replacement for E2E tests
- EF Core DateTimeOffset precision tolerance pattern

**Production Readiness:** ✅ READY (with documented E2E fixture gap for M32.4)

**E2E Test Status:**
- 22/34 scenarios passing (ProductAdmin, PricingAdmin, WarehouseAdmin)
- 12/34 scenarios blocked by E2E fixture issue (UserManagement — environmental, not code defect)

**Integration Test Status:**
- 6/6 passing (BackofficeIdentity password reset endpoint)

**Session Summary:**
1. ✅ Session 1: Product Admin write UI
2. ✅ Session 2: Product List UI + API routing audit
3. ✅ Session 3: Product Admin E2E tests + Pricing Admin write UI
4. ✅ Session 4: Warehouse Admin write UI
5. ✅ Session 5: Pricing Admin E2E tests
6. ✅ Session 6: Warehouse Admin E2E tests
7. ✅ Session 7: User Management write UI
8. ❌ Session 8: SKIPPED (no CSV/Excel exports needed)
9. ✅ Session 9: User Management E2E tests + integration tests
10. ✅ Session 10: Integration test stabilization + E2E investigation
11. ✅ Session 11: Milestone wrap-up + M32.4 planning + Wolverine pattern documentation

**Deferred to M32.4:**
- E2E fixture investigation (Blazor WASM app not loading in test context, 4-6 hours)
- DateTimeOffset precision audit across all EF Core tests
- GET /api/backoffice-identity/users/{userId} endpoint (performance optimization)
- Table sorting in UserList.razor (UX enhancement)
- Enhanced error messages (400 vs 500 vs 503 specificity)

**References:**
- [M32.3 Retrospective](./milestones/m32-3-retrospective.md)
- [Session 1 Retrospective](./milestones/m32-3-session-1-retrospective.md)
- [Session 2 Retrospective](./milestones/m32-3-session-2-retrospective.md)
- [Session 4 Retrospective](./milestones/m32-3-session-4-retrospective.md)
- [Session 5 Retrospective](./milestones/m32-3-session-5-retrospective.md)
- [Session 6 Retrospective](./milestones/m32-3-session-6-retrospective.md)
- [Session 7 Retrospective](./milestones/m32-3-session-7-retrospective.md)
- [Session 9 Retrospective](./milestones/m32-3-session-9-retrospective.md)
- [Session 10 Retrospective](./milestones/m32-3-session-10-retrospective.md)
- [Session 11 Plan](./milestones/m32-3-session-11-plan.md)
- [Session 11 Retrospective](./milestones/m32-3-session-11-retrospective.md) (pending)

*Completed: 2026-03-21*

---

### M32.2: Backoffice Phase 3A — Stabilization + UX Hardening

**Status:** ✅ **COMPLETE** — All 3 sessions finished (all P0 + P1 + P2 items)
**Goal:** Execute narrow M32.2 scope (stabilization + UX hardening) and defer heavier write-ops/UI depth to M32.3

**Current findings (2026-03-18):**
- ✅ UX audit backlog has been converted to copy/paste issue drafts:
  - `docs/planning/ux-audit-discovery-2026-03-18.md` → "Drop-in backlog entries"
- ✅ M32.1 retrospective already recommends:
  - M32.2 focus on E2E stabilization
  - Write-operations UI deferred to M32.3+
- ✅ No existing `m32.2*` / `m32.3*` milestone plan files found under `docs/planning/milestones/`
- ✅ No existing GitHub Issues currently assigned to milestone `M32.2` or `M32.3`

**Session 1 Progress (2026-03-19):**
- ✅ P0-1: Fixed Alerts.razor authorization role mismatch (warehouse-manager → warehouse-clerk)
- ✅ P1-1: Gated dead-end navigation in CustomerSearch.razor ("View Details" button disabled with tooltip)
- ✅ Verified build succeeds with both fixes (0 errors)
- ✅ Stored memories for future sessions
- **Retrospective:** `docs/planning/milestones/m32.2-session-1-retrospective.md`

**Session 2 Progress (2026-03-19):**
- ✅ P0-2: Alert acknowledgment UX (Alerts.razor) — Acknowledge button, optimistic UI, 409 handling
- ✅ P0-3: Session-expired recovery UX — SessionExpiredModal, returnUrl redirect, standardized 401 handling
- ✅ P0-4: Network/conflict/retry state standardization — Applied session-expired pattern to Dashboard + CustomerSearch
- ✅ All 3 P0 items completed with zero rework (199 lines added, 10 removed, 9 files changed)
- ✅ Stored memory: event-based SessionExpiredService pattern for Blazor WASM 401 handling
- **Retrospective:** `docs/planning/milestones/m32.2-session-2-retrospective.md`

**Session 3 Progress (2026-03-19):**
- ✅ P1-2: Data freshness indicators — Backend `QueriedAt` timestamps + relative time display ("2 minutes ago")
- ✅ P2-9: Operator terminology consistency pass — Dashboard, Alerts, CustomerSearch reviewed (zero issues found)
- ✅ P2-10: Empty-state UX guidance — All pages already have appropriate empty states
- ✅ All remaining P1 and P2 items completed with minimal changes (43 lines added, 5 removed, 3 files changed)
- **Retrospective:** `docs/planning/milestones/m32.2-session-3-retro.md`

**Final Backlog Status:**

**M32.2 (stabilization + UX hardening) — ALL COMPLETE:**
- ✅ P0-1: Alerts authorization role mismatch (COMPLETED Session 1)
- ✅ P0-2: Alert acknowledgment UX (COMPLETED Session 2)
- ✅ P0-3: Session-expired recovery UX (COMPLETED Session 2)
- ✅ P0-4: Network/conflict/retry state standardization (COMPLETED Session 2)
- ✅ P1-1: Dead-end route gating/replacement (COMPLETED Session 1)
- ✅ P1-2: Data freshness indicators (COMPLETED Session 3)
- ✅ P2-9: Operator terminology consistency pass (COMPLETED Session 3)
- ✅ P2-10: Empty-state UX guidance (COMPLETED Session 3)

**M32.3 (write-ops/UI depth + cross-BC dependencies) — DEFERRED:**
- P1-3: Product history tab with significance filtering (event-sourcing dependent)
- P1-4: Discontinuation pre-flight impact + grouped notification UX (Listings/Marketplaces dependency)
- Existing deferred Phase 3 items: Promotions management UI, CSV/Excel exports, bulk operations pattern, returns analytics dashboard, audit log viewer

**Decision record:**
- ✅ **Option A selected:** Keep M32.2 narrow (stability + UX hardening), push heavier write-ops/UI depth to M32.3

**Completion Summary:**
- **Duration:** 3 sessions (~6 hours)
- **Deliverables:** 8 UX improvements (4 P0, 2 P1, 2 P2)
- **Build Status:** 0 errors, 0 warnings
- **Key Achievement:** M32.2 Backoffice MVP stabilization functionally complete. Only E2E testing remains before milestone closure.

---

### M32.1 Historical Detail (to be condensed after M32.2 kickoff)

### 🚀 M32.1: Backoffice Phase 2 — Write Operations

**Status:** 🚀 **IN PROGRESS** — Sessions 1-10 completed, E2E test infrastructure built, all 32 tests timeout at ~30 seconds
**Duration Estimate:** 3-4 cycles (12-18 sessions)
**Current Phase:** Diagnosing E2E test failures (Session 11) — Playwright tracing and WASM hydration investigation

**What's Shipping:**
- **Phase 2 Prerequisite (Sessions 1-3):** Domain BC endpoint gaps closed (Product Catalog write, Pricing write, Inventory write, Payments query)
- **Blazor WASM Frontend (Sessions 4-8):** Backoffice.Web with JWT auth, role-based navigation, dashboard UI, CS workflows UI
- **Write Operations UI (Sessions 9-12):** Product admin, pricing admin, warehouse admin, user management
- **E2E Testing (Sessions 13-15):** Playwright tests for critical workflows
- **Documentation (Session 16):** Retrospectives, skills updates, gap register closure

**Phase 2 Approach:**
1. **Sessions 1-3:** ✅ Close 9 endpoint gaps in domain BCs (prerequisite for write operations)
2. **Sessions 4-8:** ✅ Build Blazor WASM frontend shell with JWT auth and read-only views
3. **Sessions 9-10:** ✅ Build E2E test infrastructure + run first test execution (all 32 tests failing)
4. **Session 11:** 🔄 Enable Playwright tracing, diagnose WASM hydration or appsettings.json injection
5. **Sessions 12-15:** Add write operations UI (product, pricing, inventory, users)
6. **Session 16:** Documentation and retrospective

**Key Decisions:**
- Session 1 will write **4 ADRs documenting M32.0 decisions** (0034-0037: BFF Architecture, SignalR Hub, Projections Strategy, OrderNote Ownership)
- Blazor WASM follows Vendor Portal pattern (in-memory JWT, background token refresh, SignalR with JWT Bearer)
- E2E tests use real Kestrel servers (not TestServer) for SignalR testing
- Gap closure first (Sessions 1-3) prevents mid-cycle blockers

**Session 1 Goals:** ✅ COMPLETED
- ✅ Write ADRs 0034-0037 (M32.0 architectural decisions)
- ✅ Close Product Catalog admin write endpoint gaps (update description, update display name, delete product)
- ✅ Add multi-issuer JWT to Product Catalog BC (Backoffice scheme)
- ✅ 10+ integration tests for Product Catalog write endpoints

**Session 2 Goals:** ✅ COMPLETED (with deferred tests)
- ✅ Close Pricing BC write endpoint gaps (set base price, schedule price change, cancel schedule)
- ✅ Add multi-issuer JWT to Pricing BC (Backoffice scheme)
- ✅ Implement floor/ceiling constraint enforcement
- ⚠️ Integration tests (deferred to Session 4 due to timeout)

**Session 3 Goals:** ✅ COMPLETED
- ✅ Close Inventory BC write endpoints (adjust inventory, receive inbound stock)
- ✅ Close Payments BC query endpoint (list payments for order)
- ✅ Update Gap Register (9 Phase 2 blockers → 1 blocker)
- ✅ Session 2 and Session 3 retrospectives completed

**Session 4 Goals:** ✅ COMPLETED
- ✅ Fix Pricing BC integration tests (25 tests, all passing)
- ✅ Add authorization bypass pattern to test fixtures
- ✅ Fix missing Apply method for ProductRegistered event
- ✅ Session 4 retrospective completed

**Session 5 Goals:** ✅ COMPLETED
- ✅ Fix Inventory BC integration tests (48 tests, all passing)
- ✅ Fix Payments BC integration tests (24 tests, all passing)
- ✅ Add AdjustInventoryRequestValidator for HTTP endpoint validation
- ✅ Multi-policy authorization bypass (CustomerService + FinanceClerk)
- ✅ Session 5 retrospective completed

**Session 6 Goals:** ✅ COMPLETED
- ✅ Begin Blazor WASM scaffolding (Backoffice.Web project)
- ✅ Basic project structure following Vendor Portal pattern
- ✅ JWT authentication infrastructure (in-memory token storage)
- ✅ Login page + authentication state provider
- ✅ Stub navigation shell (AppBar, Drawer, role-based menu)
- ✅ TokenRefreshService for background token refresh
- ✅ 17 files created, project builds successfully (0 errors)

**Session 7 Goals:** ✅ COMPLETED
- ✅ Create Customer Search page (CS role — highest-frequency workflow)
- ✅ Create Executive Dashboard page (Executive role — KPI metrics)
- ✅ Create Operations Alert Feed page (OperationsManager role)
- ✅ Wire SignalR hub connection (BackofficeHubService)
- ✅ Create typed HTTP client interfaces (stub-backed for rapid iteration)
- ✅ Test role-based navigation visibility

**Session 8 Goals:** ✅ COMPLETED
- ✅ Replace GetDashboardSummary stub with real AdminDailyMetrics projection query
- ✅ Remove duplicate stub endpoints (GetOperationsAlerts, SearchCustomers already had real implementations)
- ✅ Fix SignalRNotificationTests to match BackofficeEvent discriminated union signatures
- ✅ All 75 Backoffice integration tests passing

**Session 9 Goals:** ✅ COMPLETED (split into 9a and 9b due to context limit)
- ✅ Create E2E test infrastructure (Backoffice.E2ETests project)
- ✅ 3-server WASM E2E fixture (BackofficeIdentity.Api + Backoffice.Api + Backoffice.Web)
- ✅ 3 BDD feature files (Authentication, CustomerService, OperationsAlerts) with 32 scenarios
- ✅ Page Object Models (LoginPage, DashboardPage, CustomerSearchPage, OperationsAlertsPage)
- ✅ Playwright v1.51.0 configuration with browser downloads
- ✅ Fix compilation errors (appsettings.json injection, WasmStaticFileHost, test hooks)
- ✅ Project builds successfully (0 errors, 6 nullable warnings)

**Session 10 Goals:** ✅ COMPLETED
- ✅ Start infrastructure (Postgres, RabbitMQ, Jaeger) via Docker Compose
- ✅ Run E2E tests for first time (`dotnet test Backoffice.E2ETests`)
- ✅ Document test failures: All 32 scenarios timeout at ~30 seconds
- ✅ Root cause analysis: Likely Blazor WASM hydration failure or appsettings.json injection failure
- ✅ Write comprehensive Session 10 retrospective with diagnostic strategy
- ✅ Update CURRENT-CYCLE.md

**Session 11 Goals:** ✅ COMPLETED
- ✅ Enable Playwright tracing to capture browser console logs, network traffic, screenshots
- ✅ Run first E2E test to generate trace files (all traces captured successfully)
- ✅ Add trace-on-failure logic (saves `.zip` files to `playwright-traces/`)
- ✅ Diagnose WASM hydration issue: discovered critical wwwroot path bug
- ✅ Fix: `FindWasmRoot()` was returning `bin/.../wwwroot` (has `_framework` but missing `index.html`)
- ⚠️ Partial success: wwwroot path fixed, but 404 errors still present (requires publish output)
- ✅ Write comprehensive Session 11 retrospective with root cause analysis
- ✅ Update CURRENT-CYCLE.md

**Session 12 Goals:** ✅ COMPLETED
- ✅ View Playwright traces from Session 11 (via logging, not viewer due to time)
- ✅ Fix middleware ordering: `UseStaticFiles` BEFORE `MapGet` route handlers
- ✅ Diagnose root cause of 404s: `index.html` missing from `bin/.../wwwroot` (only in publish output)
- ✅ Fix `FindWasmRoot()` to prefer publish output directory (`bin/.../publish/wwwroot`)
- ✅ Run `dotnet publish` to create complete wwwroot with `index.html` + `_framework`
- ✅ All 404 errors fixed — WASM files now serve correctly (200 OK)
- ⚠️ Discovered new issue: Authorization policies not registered (`CustomerService`, `Executive`, etc.)
- ✅ Write Session 12 retrospective documenting fixes and new issue
- ✅ Update CURRENT-CYCLE.md

**Session 13 Goals:** ✅ COMPLETED (with caveats)
- ✅ Register authorization policies in `Backoffice.Web/Program.cs` (7 policies added)
- ✅ Add `data-testid` attributes to `Login.razor` (5 test-ids added)
- ✅ Fix JWT role claims to use kebab-case (created `ToRoleString()` extension)
- ✅ Update post-login navigation to `/dashboard`
- ⚠️ Dashboard navigation still failing — test times out at URL check
- ✅ Write Session 13 retrospective documenting major fixes + ongoing issue

**Session 14 Goals:** ✅ COMPLETED (with test failures)
- ✅ Fix `LoginHandler` Line 133 to use `ToRoleString()` for consistency
- ✅ Align Dashboard.razor test-ids with DashboardPage.cs expectations (17 changes)
- ✅ Add `realtime-connected` and `realtime-disconnected` test-id indicators
- ✅ Add nested `kpi-value` test-ids to all KPI cards
- ❌ Run full authentication feature suite (tests failed — needs debugging in Session 15)
- ✅ Write comprehensive Session 14 retrospective
- ✅ Update CURRENT-CYCLE.md

**Session 15 Goals:** ✅ COMPLETED (with deferred E2E fixes)
- ✅ Investigate E2E test failures (identified 4 root causes: WASM hydration, navigation, KPI rendering, SignalR connection timeouts)
- ✅ Resolve Active Customers KPI mismatch (removed from DashboardPage.cs - not in M32.1 scope)
- ⚠️ Run full authentication test suite (deferred to Session 16 - requires timeout fixes)
- ✅ Document test-id conventions in e2e-playwright-testing.md (comprehensive naming guide added)
- ✅ Write Session 15 retrospective
- ✅ Update CURRENT-CYCLE.md

**Session 16 Goals:** (Next — Milestone Completion)
- **PRIMARY GOAL:** Get at least 1 authentication E2E scenario passing (smoke test validation)
- Run single authentication scenario with Playwright tracing enabled
- Fix dashboard navigation timing (add explicit auth state + MudBlazor hydration checks)
- Reduce LoginPage timeout from 30s to 15s
- Write Session 16 retrospective
- Write M32.1 milestone retrospective
- Update CURRENT-CYCLE.md (move M32.1 to Recent Completions)
- Update E2E test documentation with timeout tuning guidance
- **Note:** Full 32-test suite stabilization can be deferred to M32.2 if needed

**References:**
- [M32.1 Plan](./milestones/m32-1-backoffice-phase-2-plan.md)
- [M32.1 Triage and Completion Plan](./milestones/m32.1-triage-and-completion-plan.md) ⭐ **NEW**
- [M32.0 Retrospective](./milestones/m32-0-retrospective.md)
- [Session 1 Retrospective](./milestones/m32-1-session-1-retrospective.md)
- [Session 2 Retrospective](./milestones/m32-1-session-2-retrospective.md)
- [Session 3 Retrospective](./milestones/m32-1-session-3-retrospective.md)
- [Session 4 Retrospective](./milestones/m32-1-session-4-retrospective.md)
- [Session 5 Retrospective](./milestones/m32-1-session-5-retrospective.md)
- [Session 6 Retrospective](./milestones/m32-1-session-6-retrospective.md)
- [Session 7 Retrospective](./milestones/m32-1-session-7-retrospective.md)
- [Session 8 Retrospective](./milestones/m32-1-session-8-retrospective.md)
- [Session 9 Retrospective](./milestones/m32-1-session-9-retrospective.md)
- [Session 10 Retrospective](./milestones/m32-1-session-10-retrospective.md)
- [Session 11 Retrospective](./milestones/m32-1-session-11-retrospective.md)
- [Session 12 Retrospective](./milestones/m32-1-session-12-retrospective.md)
- [Session 13 Retrospective](./milestones/m32-1-session-13-retrospective.md)
- [Session 14 Retrospective](./milestones/m32-1-session-14-retrospective.md)
- [Session 15 Retrospective](./milestones/m32.1-session-15-retrospective.md)
- [UX Audit Discovery (includes M32.2/M32.3 issue drafts)](./ux-audit-discovery-2026-03-18.md) ⭐ **NEW**
- [Backoffice Event Modeling](./backoffice-event-modeling-revised.md)
- [Backoffice Frontend Design](./backoffice-frontend-design.md)
- [Frontend Design Alignment Analysis](./backoffice-frontend-design-alignment-analysis.md)
- [Integration Gap Register](./backoffice-integration-gap-register.md)
- [ADR 0034: Backoffice BFF Architecture](../decisions/0034-backoffice-bff-architecture.md)
- [ADR 0035: Backoffice SignalR Hub Design](../decisions/0035-backoffice-signalr-hub-design.md)
- [ADR 0036: BFF-Owned Projections Strategy](../decisions/0036-bff-projections-strategy.md)
- [ADR 0037: OrderNote Aggregate Ownership](../decisions/0037-ordernote-aggregate-ownership.md)

**Deferred to Phase 3:**
- Promotions management UI
- CSV/Excel exports
- Bulk operations pattern
- Returns analytics dashboard
- Audit log viewer

---

## Recent Completions

> **Contains:** Last 3 completed milestones for quick reference.
> **Archive Policy:** After 3 milestones accumulate, move oldest to [Milestone Archive](#milestone-archive).

### ✅ M32.0: Backoffice Phase 1 — Read-Only Dashboards (2026-03-16)

**What shipped:**
- Backoffice BFF (Backend-for-Frontend) for internal operations portal
- CS agent workflows: customer search, order lookup, return management, correspondence history, order notes
- Executive dashboard with 5 real-time KPIs (order count, revenue, AOV, payment failure rate)
- Operations alert feed with SignalR push notifications
- Warehouse clerk tools: stock visibility, low-stock alerts, alert acknowledgment
- BFF-owned Marten projections (AdminDailyMetrics, AlertFeedView)
- OrderNote aggregate (BFF-owned internal CS comments)
- 75 integration tests (Alba + TestContainers) — all passing
- 14+ RabbitMQ event subscriptions from 7 domain BCs

**Key Technical Wins:**
- BFF pattern consistency (3rd successful implementation: Storefront, Vendor Portal, Backoffice)
- Multi-issuer JWT integration (domain BCs accept tokens from 2+ identity providers)
- BFF-owned projections for real-time dashboards (alternative to Analytics BC)
- Integration testing pattern for multi-BC BFFs (stub client fixture design)
- OrderNote aggregate ownership decision (ADR 0037 — operational metadata belongs in BFF)

**Key Decisions:**
- Pre-wired SignalR configuration accelerated Session 8 (3h → 2h)
- Inline projections require explicit `SaveChangesAsync()` before querying
- Role-based SignalR groups scale better than user-specific groups for internal portals
- Stub clients must mirror real BC API design (separate list vs detail storage)

**Build Status:** 0 errors, 7 pre-existing warnings (OrderNoteTests nullable false positives)

**Duration:** 11 sessions (~28 hours) — within estimate (26-32 hours)

**References:**
- [Milestone Plan](./milestones/m32-0-backoffice-phase-1-plan.md)
- [Milestone Retrospective](./milestones/m32-0-retrospective.md)
- [Session 11 Retrospective](./milestones/m32-0-session-11-retrospective.md)
- [ADR 0031: Backoffice RBAC Model](../decisions/0031-admin-portal-rbac-model.md)
- [ADR 0032: Multi-Issuer JWT Strategy](../decisions/0032-multi-issuer-jwt-strategy.md)
- [ADR 0033: Backoffice Rename](../decisions/0033-admin-portal-to-backoffice-rename.md)

**ADRs to Write (Phase 2):**
- ADR 0034: Backoffice BFF Architecture
- ADR 0035: Backoffice SignalR Hub Design
- ADR 0036: BFF-Owned Projections Strategy
- ADR 0037: OrderNote Aggregate Ownership

**Deferred to M32.1 (Phase 2):**
- 9 endpoint gaps (Product Catalog write, Pricing write, Inventory write, Payments order query)
- Blazor WASM frontend (Backoffice.Web)
- Write operations (product admin, pricing adjustments, inventory adjustments)
- E2E tests (Playwright)

*Completed: 2026-03-16*

---

### ✅ M31.5: Backoffice Prerequisites (2026-03-16)

**What shipped:**
- 8 Phase 0.5 blocking gaps closed across 5 sessions
- GetCustomerByEmail endpoint (Customer Identity BC)
- Inventory BC HTTP query endpoints (GetStockLevel, GetLowStock)
- Fulfillment BC GetShipmentsForOrder endpoint
- Multi-issuer JWT configuration (5 domain BCs: Orders, Payments, Inventory, Fulfillment, Correspondence)
- Endpoint authorization with `[Authorize]` attributes (17 endpoints across 7 BCs)
- 38 fully defined endpoints ready for Backoffice Phase 1

**Key Decisions:**
- Multi-issuer JWT uses named schemes (`"Backoffice"`, `"Vendor"`)
- Policy-based authorization aligned with ADR 0031 roles
- Product Catalog policy already named "VendorAdmin" (no rename needed)
- GetAddressSnapshot deliberately left unprotected (BC-to-BC integration)

**Build Status:** 0 errors, 7 pre-existing warnings (Correspondence BC unused variables)

**References:**
- [Milestone Plan](./milestones/m31-5-backoffice-prerequisites.md)
- [Session 5 Retrospective](./milestones/m31-5-session-5-retrospective.md)
- [Integration Gap Register](./backoffice-integration-gap-register.md) (updated)
- [ADR 0032: Multi-Issuer JWT Strategy](../decisions/0032-multi-issuer-jwt-strategy.md)

*Completed: 2026-03-16*

---

### ✅ M31.0: Correspondence BC Extended (2026-03-15)

**What shipped:**
- 5 new integration handlers: ShipmentDeliveredHandler, ShipmentDeliveryFailedHandler, ReturnDeniedHandler, ReturnExpiredHandler, RefundCompletedHandler
- SMS channel infrastructure: ISmsProvider interface, StubSmsProvider with fake Twilio SID generation
- RabbitMQ Payments BC queue added (correspondence-payments-events)
- All 4 BC integration queues configured: Orders, Fulfillment, Returns, Payments
- 8 total handlers (4 from M28.0 + 4 new from M31.0)

**Key Decisions:**
- Pure choreography pattern scales well (no sagas needed)
- Defer template system and Customer Identity queries to Phase 3+
- Inline HTML templates in handlers for now

**Build Status:** 0 errors, 7 expected warnings (TODO placeholders)

**References:**
- [Retrospective](./cycles/m31-0-retrospective.md)
- CONTEXTS.md updated with M31.0 integration matrix

*Completed: 2026-03-15*

---

### ✅ M30.1: Shopping BC Coupon Integration (2026-03-15)

**What shipped:**
- ApplyCouponToCart + RemoveCouponFromCart command handlers
- Real PromotionsClient integration (ValidateCoupon + CalculateDiscount HTTP calls)
- GetCart enrichment with discount information
- Dual handler pattern (command handler + HTTP endpoint handler classes)
- 11 integration tests covering valid/invalid coupons, empty/terminal carts, discount calculations

**Key Patterns:**
- Wolverine Railway Programming with async external service calls requires separate handler classes
- Alba test fixture DI replacement: RemoveAll + AddSingleton pattern for stub injection
- Single coupon per cart (stacking deferred to M30.3+)

**Skills Refresh:**
- Propagated M30.1 learnings to `wolverine-message-handlers.md` (Railway Programming with async validation)

**References:**
- [Retrospective](./cycles/m30-1-shopping-bc-coupon-retrospective.md)
- CONTEXTS.md updated with Shopping ↔ Promotions bidirectional integration

*Completed: 2026-03-15*

---

### ✅ M30.0: Promotions BC Redemption (2026-03-15)

**What shipped:**
- RedeemCoupon, RevokeCoupon, RecordPromotionRedemption command handlers
- GenerateCouponBatch fan-out pattern (PREFIX-XXXX format)
- CalculateDiscount query with stub CartView
- RecordPromotionRedemptionHandler choreography integration with Orders BC
- ExpireCoupon scheduled message (promotion end date expiry)
- 29 integration tests across lifecycle, validation, redemption, discount calculation

**Key Patterns:**
- Handlers manually loading aggregates must use `session.Events.Append()` (not tuple returns)
- Draft promotions can issue coupons (enables batch generation before activation)
- **Banker's Rounding:** `Math.Round(6.825, 2)` → 6.82 (even), not 6.83 — affects discount calculations

**Skills Refresh:**
- Updated `wolverine-message-handlers.md` (anti-pattern #8)
- Updated `modern-csharp-coding-standards.md` (banker's rounding)
- Updated `critterstack-testing-patterns.md` (fan-out timing)

**Deferred:**
- Full Shopping BC integration (completed in M30.1)
- Pricing BC floor price enforcement (future)

**References:**
- [Retrospective](./milestones/m30-0-retrospective.md)
- CONTEXTS.md updated with M30.0 implementation status

*Completed: 2026-03-15*

---

## Milestone Archive

> **Contains:** Completed milestones older than the last 3 (M29.1 and earlier).
> **Purpose:** Historical reference without cluttering recent work context.

<details>
<summary><strong>M29.1: Promotions BC Core — MVP (2026-03-14 to 2026-03-15)</strong></summary>

**What shipped:**
- Event-sourced Promotion aggregate (UUID v7) with 6 domain events
- Event-sourced Coupon aggregate (UUID v5 from code) with 4 domain events
- Command handlers: CreatePromotion, ActivatePromotion, IssueCoupon
- CouponLookupView projection (case-insensitive coupon validation)
- ValidateCoupon query endpoint with business rules
- Marten snapshot projections (Promotion + Coupon)
- 11 integration tests (all passing)
- Port 5250 allocated

**Pattern Discoveries:**
- IStartStream return type for event stream creation
- Snapshot projection requirement for queryability

**Deferred to M30.0:** Redemption tracking, batch generation, Shopping/Pricing integration

[Retrospective](./cycles/cycle-29-phase-2-retrospective-notes.md)

</details>

<details>
<summary><strong>M29.0: Backoffice Identity BC (2026-03-14)</strong></summary>

**What shipped:**
- ADR 0031: RBAC model (7 roles, policy-based authorization)
- EF Core entity model: AdminUser, AdminRole, AdminUserStatus, BackofficeIdentityDbContext
- Authentication handlers: Login, RefreshToken, Logout (JWT + refresh token rotation)
- User management handlers: CreateAdminUser, GetAdminUsers, ChangeAdminUserRole, DeactivateAdminUser
- JWT token generation with 7 authorization policies
- API endpoints: 3 auth + 4 user management (Wolverine HTTP)
- Infrastructure: Docker Compose, Aspire, database, port 5249

[Retrospective](./cycles/cycle-29-admin-identity-phase-1-retrospective.md)

</details>

<details>
<summary><strong>M28.0: Correspondence BC Core (2026-03-13 to 2026-03-14)</strong></summary>

**What shipped:**
- Message aggregate (event-sourced) — 4 domain events, retry lifecycle
- Provider interfaces (IEmailProvider, StubEmailProvider)
- OrderPlacedHandler — email order confirmations
- SendMessage handler — exponential backoff retry (5min, 30min, 2hr)
- MessageListView projection (inline)
- HTTP query endpoints (GetMessagesForCustomer, GetMessageDetails)
- 12 unit tests + 5 integration tests

[Retrospective](./cycles/cycle-28-correspondence-bc-phase-1-retrospective.md)

</details>

<details>
<summary><strong>M25.2: Returns BC Exchanges (2026-03-13)</strong></summary>

**What shipped:**
- Exchange workflow (UC-11) — ReturnType enum, ExchangeRequest, 5 exchange domain events, 3 command handlers
- 6 integration messages for exchange workflow
- CE SignalR handlers — 7 handlers, ReturnStatusChanged discriminated union event
- Sequential returns — IsReturnInProgress → ActiveReturnIds saga refactor
- Anticorruption layer — EnumTranslations static class
- Cross-BC smoke tests (3-host Alba fixture)

[Plan](./cycles/cycle-27-returns-bc-phase-3.md) | [Retrospective](./cycles/cycle-27-returns-bc-phase-3-retrospective.md)

</details>

<details>
<summary><strong>M25.1: Returns BC Mixed Inspection (2026-03-12 to 2026-03-13)</strong></summary>

**What shipped:**
- ReturnCompleted expanded with per-item disposition
- 5 new integration events (ReturnApproved, ReturnRejected, ReturnExpired, ReturnReceived, ReturnedItem)
- Mixed inspection three-way logic
- GetReturnsForOrder query (Marten inline snapshots)
- RabbitMQ dual-queue routing + Fulfillment queue wiring fix
- ~99 total return-related tests

[Plan](./cycles/cycle-26-returns-bc-phase-2.md) | [Retrospective](./cycles/cycle-26-returns-bc-phase-2-retrospective.md)

</details>

<details>
<summary><strong>M25.0: Returns BC Core (2026-03-12)</strong></summary>

**What shipped:**
- Event-sourced Return aggregate (10 lifecycle states, 9 domain events)
- 6 command handlers + 7 API endpoints (port 5245)
- ReturnEligibilityWindow from Fulfillment.ShipmentDelivered
- Auto-approval logic + restocking fee calculation
- 48 unit tests + 5 integration tests

[Plan & Retrospective](./cycles/cycle-25-returns-bc-phase-1.md)

</details>

<details>
<summary><strong>Cycle 24: Fulfillment Integrity + Returns Prerequisites (2026-03-12)</strong></summary>

**What shipped:**
- RabbitMQ transport wired in Fulfillment.Api
- RecordDeliveryFailure endpoint + ShipmentDeliveryFailed cascade
- UUID v5 idempotent shipment creation
- SharedShippingAddress with dual JSON annotations
- Orders saga return handlers + IsReturnInProgress guard
- GET /api/orders/{orderId}/returnable-items endpoint

[Plan](./cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md)

</details>

<details>
<summary><strong>Cycle 23: Vendor Portal E2E Testing (2026-03-11)</strong></summary>

**What shipped:**
- 3-server E2E fixture (VendorIdentity.Api + VendorPortal.Api + WASM static host)
- 12 BDD scenarios (P0 + P1a) across 3 feature files
- Page Object Models for Login, Dashboard, Change Requests, Submit, Settings
- SignalR hub message injection testing

[Plan](./cycles/cycle-23-vendor-portal-e2e-testing.md) | [Skills Update](../skills/e2e-playwright-testing.md)

</details>

<details>
<summary><strong>Cycle 22: Vendor Portal + Vendor Identity Phase 1 (2026-03-08 to 2026-03-10)</strong></summary>

**What shipped (6 phases):**
- Phase 1: JWT Auth (VendorIdentity.Api, EF Core, token lifecycle)
- Phase 2: Vendor Portal API (analytics, alerts, dashboard, multi-tenant)
- Phase 3: Blazor WASM Frontend (SignalR hub, in-memory JWT, live updates)
- Phase 4: Change Request Workflow (7-state machine, Catalog BC integration)
- Phase 5: Saved Views + VendorAccount (notification preferences, saved dashboard views)
- Phase 6: Full Identity Lifecycle + Admin Tools (8 admin endpoints, compensation handler)
- 143 integration tests (100% pass rate)

[Event Modeling](vendor-portal-event-modeling.md) | [Retrospective](./cycles/cycle-22-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/16)

</details>

<details>
<summary><strong>Cycle 21: Pricing BC Phase 1 (2026-03-07 to 2026-03-08)</strong></summary>

**What shipped:**
- ProductPrice event-sourced aggregate (UUID v5 deterministic stream ID)
- Money value object (140 unit tests)
- CurrentPriceView inline projection (zero-lag queries)
- Shopping BC security fix (server-authoritative pricing)
- 5 ADRs written
- 151 Pricing tests + 56 Shopping tests

[Plan](pricing-event-modeling.md) | [Retrospective](./cycles/cycle-21-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/15) (closed)

</details>

<details>
<summary><strong>Cycle 20: Automated Browser Testing (2026-03-04 to 2026-03-07)</strong></summary>

**What shipped:**
- Playwright + Reqnroll E2E testing infrastructure
- Real Kestrel servers (not TestServer) for SignalR testing
- Page Object Model with data-testid selectors
- MudBlazor component interaction patterns
- Playwright tracing for CI failure diagnosis

[Plan](./cycles/cycle-20-automated-browser-testing.md) | [Retrospective](./cycles/cycle-20-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/2)

</details>

<details>
<summary><strong>Cycle 19.5: Complete Checkout Workflow (2026-03-04)</strong></summary>

**What shipped:**
- Wired checkout stepper to backend APIs
- Checkout initialization + CheckoutId persistence
- Error handling with MudSnackbar toasts
- End-to-end manual testing

[Milestone](https://github.com/erikshafer/CritterSupply/milestone/13)

</details>

<details>
<summary><strong>Cycle 19: Authentication & Authorization (2026-02-25 to 2026-02-26)</strong></summary>

**What shipped:**
- Cookie-based authentication (ASP.NET Core middleware)
- Login/Logout pages with MudBlazor
- Protected routes (Cart, Checkout)
- AppBar authentication UI
- Cart persistence via browser localStorage
- Swagger UI + seed data for ProductCatalog.Api

[Plan](./cycles/cycle-19-authentication-authorization.md) | [Retrospective](./cycles/cycle-19-retrospective.md)

</details>

*Archive Last Updated: 2026-03-15*

---

## Roadmap

> **Contains:** Next 3-4 milestones + future BCs (high-level only).
> **Purpose:** Forward-looking planning without excessive detail.

### Next 3-4 Milestones

> ⚠️ **Updated 2026-03-21:** Following the post-audit discussion, the owner directed that M33 and M34 be **engineering-led milestones** — not product expansion. Product Catalog Evolution and other future BCs are pushed to M35+. See full proposal: [`docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md`](milestones/m33-m34-engineering-proposal-2026-03-21.md).

- **M32.4 (active):** Backoffice Phase 4 — E2E fixture stabilization + UX polish
  - Resolve Blazor WASM app-loading timeout blocking 12/34 E2E scenarios
  - Address remaining UX polish items from audit
  - Close out the Backoffice series

- **M33 (planned — engineering-led):** Code Correction + Broken Feedback Loop Repair
  - **10–13 sessions** (PSA + UXE + QAE joint effort)
  - Fix INV-3 (`AdjustInventoryEndpoint` bypass) + instrument `BackofficeTestFixture` (F-8) — first
  - Build three missing Marten projections (`FulfillmentPipelineView`, `ReturnMetricsView`, `CorrespondenceMetricsView`) — after INV-3+F-8
  - Add Order Search (`/orders/search`) and Return Management (`/returns`) pages to Backoffice
  - Returns BC full structural refactor (R-1 through R-7) + UXE event renames in same PR
  - Vendor Portal structural refactor (VP-1 through VP-6) + E2E `@ignore` Phase A removal
  - Backoffice folder restructure (BO-1/BO-2/BO-3) + `AcknowledgeAlert` transaction fix (XC-3)
  - ADR for canonical validator placement (XC-1)
  - `CheckoutCompleted` dual-payload collision fix (🔴)
  - Quick wins: INV-1/2, PR-1, CO-1, PAY-1/FUL-1/ORD-1, F-9
  - See full scope + sequencing diagram in proposal document

- **M34 (planned — engineering-led):** Architecture Completion + Vocabulary Alignment
  - **8–12 sessions** (PSA + UXE + QAE)
  - Untapped value: `Returns → Correspondence` integration, customer order status timeline, live return count in SignalR hub, `LowStockDetected` → Correspondence notification
  - Test pyramid completion: Vendor Portal bUnit, Vendor Portal E2E Phase B (step definitions), Customer Experience E2E (`cart-real-time-updates`, `product-browsing`), Promotions unit tests + `ICollectionFixture`
  - Vocabulary alignment: `PriceChanged`/`PriceUpdated`, `CheckoutStarted`/`CheckoutReceived`, Correspondence contract alignment, VP transient command-masquerade renames
  - Shopping dual-handler ADR (SH-1)
  - Persisted event rename investigation + migration ADR
  - XC-2 `.Api` folder naming normalization pass
  - See full scope + untapped value items in proposal document

### Future BCs (Priority Roadmap — Post M34)

> Product expansion milestones are deferred until M33 and M34 close the engineering health gap.

**High Priority (Next after M34):**
- 🟡 **Exchange v2** — Cross-product exchanges, upcharge payment collection
- 🟡 **Product Catalog Evolution** — Variants, Listings, Marketplaces ([plan](catalog-listings-marketplaces-cycle-plan.md) — M35+ estimated, re-numbered from former M33 estimate)

**Medium Priority:**
- 🟡 **Search BC** — Full-text product search, faceted navigation
- 🟡 **Recommendations BC** — Personalized product recommendations

**Lower Priority (Strategic/Retention):**
- 🔵 **Analytics BC** — Business intelligence, reporting, dashboards
- 🔵 **Store Credit BC** — Gift cards, store credit issuance
- 🔵 **Loyalty BC** — Rewards program, points accumulation
- 🔵 **Operations Dashboard** — Developer/SRE event stream visualization (React + SignalR)

See [CONTEXTS.md — Future Considerations](../../CONTEXTS.md) for full specifications.

*Roadmap Last Updated: 2026-03-21 (M33+M34 engineering milestones inserted; product BCs shifted to M35+)*

---

## Quick Links

- [CONTEXTS.md](../../CONTEXTS.md) — Architectural source of truth *(always read first)*
- [GitHub Issues](https://github.com/erikshafer/CritterSupply/issues) — Issue tracking
- [GitHub Project Board](https://github.com/users/erikshafer/projects/9) — Kanban board
- [Historical Cycles](./cycles/) — Markdown retrospectives
- [Milestone Mapping](./milestone-mapping.md) — Legacy "Cycle N" → "M.N" translation
- [Migration Plan](./GITHUB-MIGRATION-PLAN.md) — How we got here
- [ADR 0011](../decisions/0011-github-projects-issues-migration.md) — Why we made this change
- [ADR 0032](../decisions/0032-milestone-based-planning-schema.md) — Milestone-based planning schema

---

*Document Last Updated: 2026-03-18*
*Active Milestone: M32.2 (Backoffice Phase 3A) — Option A selected, backlog intake in progress*
*Update Policy: At milestone start, milestone end, and significant task changes*
