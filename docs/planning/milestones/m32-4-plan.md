# M32.4 Milestone Plan: E2E Stabilization + UX Polish

**Milestone:** M32.4 — Backoffice Phase 4: E2E Test Stabilization + UX Polish
**Start Date:** TBD (after M32.3 completion on 2026-03-21)
**Duration Estimate:** 3-5 sessions (~8-12 hours)
**Status:** 📋 PLANNED

---

## Executive Summary

M32.4 focuses on resolving the E2E test fixture issue discovered in M32.3 Session 10 and applying targeted UX polish to the Backoffice portal. This milestone closes out the M32 series (Backoffice implementation) before transitioning to M33 (Code Correction + Broken Feedback Loop Repair).

**Primary Goal:** Fix the Blazor WASM E2E fixture issue blocking 12/34 UserManagement scenarios and complete high-value quick wins.

**Secondary Goal:** Document patterns, audit DateTimeOffset tests, and prepare the codebase for M33's structural refactoring work.

**Key Constraint:** This is NOT a product expansion milestone. All work items must be focused on stabilization, polish, or technical debt reduction. New features are deferred to M33+ per owner direction.

---

## M32.4 Scope

### In Scope (Confirmed)

**Phase 1 — E2E Fixture Investigation (CRITICAL):**
- ✅ Fix Blazor WASM app loading issue in E2E tests
- ✅ Verify all 12 UserManagement scenarios pass
- ✅ Run regression tests (ProductAdmin, PricingAdmin, WarehouseAdmin)
- ✅ Document fix in skill files

**Phase 2 — Quick Wins (MEDIUM):**
- ✅ Automate Blazor WASM publish in E2E tests (MSBuild BeforeTargets)
- ✅ Audit EF Core DateTimeOffset tests (tolerance assertions)
- ✅ Document Wolverine mixed parameter pattern (COMPLETED in M32.3 Session 11)

**Phase 3 — Optional Enhancements (LOW):**
- 📋 GET /api/backoffice-identity/users/{userId} endpoint (performance optimization)
- 📋 Table sorting in UserList.razor (UX enhancement)

### Out of Scope (Deferred)

**Deferred to M33 (per Audit Top 10):**
- INV-3: Fix `AdjustInventoryEndpoint` to dispatch via Wolverine bus (🔴 CRITICAL — audit #1)
- F-8: Add `ExecuteAndWaitAsync()` + `TrackedHttpCall()` to `BackofficeTestFixture` (🟡 MEDIUM)
- Missing Marten projections: `ReturnMetricsView`, `FulfillmentPipelineView`, `CorrespondenceMetricsView`
- Order Search page at `/orders/search` (requires projections)
- Return Management page at `/returns` (requires projections)

**Rationale:** M32.4 is E2E stabilization only. The audit findings (INV-3, F-8, projections, pages) form a sequenced dependency chain that belongs in M33's "Code Correction + Broken Feedback Loop Repair" theme. Attempting to fix INV-3 in M32.4 would blur the milestone boundary and risk incomplete delivery.

---

## Session Breakdown

### Session 1: E2E Fixture Investigation (HIGH, 4-6 hours)

**Goal:** Fix Blazor WASM app loading issue blocking 12 UserManagement E2E scenarios.

**Investigation Steps:**
1. Read `E2ETestFixture.cs` lines 480-630 (WasmStaticFileHost, Kestrel config)
2. Compare to working VendorPortal E2E tests (if any passing)
3. Enable Playwright tracing (`--trace on`) for detailed diagnostics
4. Check SignalR hub connection (JWT token provider, antiforgery configuration)
5. Verify MudBlazor initialization in E2E context
6. Test wwwroot path resolution (build vs publish output)

**Root Cause Hypotheses (from Session 10):**
- **H1:** Blazor WASM hydration failure (most likely)
- **H2:** SignalR hub connection failing (JWT auth issue)
- **H3:** MudBlazor initialization issue in E2E context
- **H4:** TestServer/Kestrel configuration issue in E2ETestFixture

**Success Criteria:**
- ✅ Root cause identified and documented
- ✅ Fix applied and verified
- ✅ All 12 UserManagement scenarios passing
- ✅ No regressions in ProductAdmin (6), PricingAdmin (6), WarehouseAdmin (10) scenarios
- ✅ Full E2E suite: 34/34 scenarios passing

**Expected Outcome:** E2E test fixture is stable and reliable for future development.

**Deferred if Time Exceeds 6 Hours:** Document findings and create GitHub Issue for future session.

---

### Session 2: E2E Automation + DateTimeOffset Audit (MEDIUM, 2-3 hours)

**Goal:** Automate WASM publish step and audit DateTimeOffset precision across EF Core tests.

**Part 1: Automate Blazor WASM Publish (1 hour)**

**Problem:** Developers must manually run `dotnet publish` before E2E tests.

**Solution:** Add MSBuild BeforeTargets to E2ETests project:

```xml
<!-- tests/Backoffice/Backoffice.E2ETests/Backoffice.E2ETests.csproj -->
<Target Name="PublishBlazorWasmForE2E" BeforeTargets="VSTest">
  <MSBuild Projects="../../../src/Backoffice/Backoffice.Web/Backoffice.Web.csproj"
           Targets="Publish"
           Properties="Configuration=$(Configuration);PublishDir=$(MSBuildThisFileDirectory)../../../src/Backoffice/Backoffice.Web/bin/$(Configuration)/net10.0/publish/" />
</Target>
```

**Success Criteria:**
- ✅ E2E tests run without manual `dotnet publish` step
- ✅ CI/CD pipeline updated (if needed)
- ✅ README.md updated with new workflow

**Part 2: DateTimeOffset Precision Audit (1-2 hours)**

**Problem:** EF Core Postgres round-trip loses microsecond precision on DateTimeOffset fields.

**Scope:** Search for all `.ShouldBe(` assertions on `DateTimeOffset` or `DateTimeOffset?` in EF Core-backed tests.

**Fix Pattern:**

```csharp
// ❌ Flaky - may fail due to microsecond precision loss
updatedUser.CreatedAt.ShouldBe(expectedCreatedAt);

// ✅ Stable - 1ms tolerance
updatedUser.CreatedAt.ShouldBe(expectedCreatedAt, TimeSpan.FromMilliseconds(1));

// For nullable DateTimeOffset:
updatedUser.LastLoginAt.ShouldNotBeNull();
updatedUser.LastLoginAt.Value.ShouldBe(expectedLastLoginAt, TimeSpan.FromMilliseconds(1));
```

**BCs to Audit:**
- ✅ BackofficeIdentity (DONE in Session 10)
- 📋 Customer Identity (EF Core-backed)
- 📋 Vendor Identity (EF Core-backed)
- 📋 Any other EF Core tests with DateTimeOffset fields

**Success Criteria:**
- ✅ All EF Core DateTimeOffset assertions use tolerance
- ✅ Pattern documented in `critterstack-testing-patterns.md`
- ✅ No flaky test failures due to precision loss

---

### Session 3: Optional Enhancements (LOW, 2-4 hours)

**Goal:** Add optional enhancements if time permits.

**Option 1: GET /api/backoffice-identity/users/{userId} Endpoint (2 hours)**

**Current State:** UserList.razor fetches all users, filters client-side for detail view.

**Enhancement:** Add dedicated endpoint for single-user detail queries.

**Value:** Performance optimization for large user lists.

**Implementation:**
1. Add `GetBackofficeUserById.cs` query handler
2. Add endpoint to BackofficeIdentity.Api
3. Update `UserEdit.razor` to call dedicated endpoint
4. Add integration test

**Success Criteria:**
- ✅ Endpoint implemented and tested
- ✅ UserEdit.razor uses dedicated endpoint
- ✅ Performance improvement measurable (if user list > 100 users)

**Option 2: Table Sorting in UserList.razor (1-2 hours)**

**Current State:** UserList.razor has no column sorting.

**Enhancement:** Add MudTable column sorting (email, role, status, created date).

**Value:** UX enhancement for CS agents managing large user lists.

**Implementation:**
1. Add `@onclick` to MudTable column headers
2. Implement client-side sorting (users already loaded)
3. Update E2E tests if needed

**Success Criteria:**
- ✅ All columns sortable (ascending/descending)
- ✅ Sort state persists during session (optional)

---

## Exit Criteria

### Must-Have (Blocking M32.4 Completion)

- ✅ E2E test fixture issue resolved
- ✅ All 34 E2E scenarios passing (UserManagement + ProductAdmin + PricingAdmin + WarehouseAdmin)
- ✅ Blazor WASM publish automated in E2E tests
- ✅ DateTimeOffset precision audit complete
- ✅ Build: 0 errors across all projects
- ✅ M32.4 retrospective written
- ✅ CURRENT-CYCLE.md updated

### Should-Have (Non-Blocking)

- ✅ Wolverine mixed parameter pattern documented (DONE in Session 11)
- ✅ E2E fixture fix documented in `e2e-playwright-testing.md`
- ✅ DateTimeOffset pattern documented in `critterstack-testing-patterns.md`

### Nice-to-Have (Future Milestones)

- 📋 GET /api/backoffice-identity/users/{userId} endpoint (can defer to M33+)
- 📋 Table sorting in UserList.razor (can defer to M33+)

---

## Dependencies

### From M32.3

- ✅ All 10 Blazor pages implemented
- ✅ All 6 BackofficeIdentity integration tests passing
- ✅ 22/34 E2E scenarios passing (ProductAdmin, PricingAdmin, WarehouseAdmin)
- ✅ Build: 0 errors

### For M33 (Next Milestone)

- ✅ E2E test fixture stable (M32.4 deliverable)
- ✅ DateTimeOffset precision pattern established (M32.4 deliverable)
- ⚠️ INV-3 fix (deferred from M32.4 to M33 Phase 1)
- ⚠️ F-8 fixture instrumentation (deferred from M32.4 to M33 Phase 1)

---

## Risks & Mitigation

### R1: E2E Fixture Fix May Be Complex (HIGH)

**Risk:** Root cause may require significant Playwright/Kestrel configuration changes.

**Mitigation:**
- Time-box investigation to 6 hours in Session 1
- Enable Playwright tracing for detailed diagnostics
- Compare to working Vendor Portal E2E tests
- Consult Playwright documentation for Blazor WASM patterns

**Fallback:** Document findings, create GitHub Issue, defer to dedicated follow-up milestone.

---

### R2: DateTimeOffset Audit May Find Widespread Issues (MEDIUM)

**Risk:** Many EF Core tests may have precision issues, requiring extensive fixes.

**Mitigation:**
- Use grep/search to identify all assertions first
- Batch fix in single PR (not per-BC)
- Verify fix pattern with BackofficeIdentity tests (already fixed in Session 10)

**Fallback:** Fix only critical tests in M32.4, defer remainder to M33.

---

### R3: Optional Enhancements May Slip Schedule (LOW)

**Risk:** Session 3 optional enhancements may not fit in 3-5 session estimate.

**Mitigation:**
- Clearly mark as "nice-to-have" in exit criteria
- Prioritize E2E fixture fix and DateTimeOffset audit
- Defer optional work to M33+ if time is tight

**Fallback:** Close M32.4 after Session 2, move optional work to M33 backlog.

---

## Success Metrics

### Test Coverage

**Before M32.4:**
- E2E: 22/34 scenarios passing (65%)
- Integration: 6/6 passing (100%)

**After M32.4 (Target):**
- E2E: 34/34 scenarios passing (100%) ✅
- Integration: 6/6 passing (100%) ✅

### Build Quality

**Before M32.4:**
- Errors: 0
- Warnings: 35 (pre-existing)

**After M32.4 (Target):**
- Errors: 0 ✅
- Warnings: 35 (no new warnings)

### Milestone Velocity

**Target:** 3-5 sessions (~8-12 hours)
**Budget:** 10 hours max (2 hours/session average)

---

## References

**From M32.3:**
- [Session 10 Retrospective](./m32-3-session-10-retrospective.md) — E2E fixture issue details
- [Session 10 Plan](./m32-3-session-10-plan.md) — Investigation steps
- [M32.3 Retrospective](./m32-3-retrospective.md) — Milestone summary

**From Audit:**
- [Codebase Audit 2026-03-21](../../audits/CODEBASE-AUDIT-2026-03-21.md) — Full findings
- [Post-Audit Discussion](../../audits/POST-AUDIT-DISCUSSION-2026-03-21.md) — Top 10 priorities
- [M33/M34 Proposal](./m33-m34-engineering-proposal-2026-03-21.md) — Next milestones

**Skills:**
- `docs/skills/e2e-playwright-testing.md` — Playwright patterns
- `docs/skills/critterstack-testing-patterns.md` — Integration test patterns
- `docs/skills/wolverine-message-handlers.md` — Handler patterns (updated in Session 11)

---

## Transition to M33

**After M32.4 Completion:**
1. Close M32 series (M32.0 → M32.1 → M32.2 → M32.3 → M32.4)
2. Begin M33 (Code Correction + Broken Feedback Loop Repair)
3. Start with INV-3 fix + F-8 instrumentation (sequenced dependency)
4. Build missing Marten projections after INV-3 is green

**Key Handoff Items:**
- ✅ E2E test fixture is stable and documented
- ✅ DateTimeOffset precision pattern established
- ✅ All Backoffice pages functional (10 pages)
- ✅ All test suites passing (34 E2E + 6 integration)
- ⚠️ INV-3 and F-8 remain deferred (M33 Phase 1)

---

**Plan Created By:** Claude Sonnet 4.5
**Date:** 2026-03-21
**Milestone:** M32.4 (Backoffice Phase 4: E2E Stabilization + UX Polish)
**Status:** PLANNED (Awaiting M32.3 completion)
