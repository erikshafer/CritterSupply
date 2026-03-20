# M32.3 Session 10 Plan: E2E & Integration Test Stabilization

**Date:** 2026-03-20
**Milestone:** M32.3 — Backoffice Phase 3B: Write Operations Depth
**Session:** 10 of 10 (FINAL SESSION)
**Goal:** Complete testing coverage, fix integration tests, run full E2E suite, and document learnings

---

## Executive Summary

Session 10 is the **final session of M32.3** focused on completing the testing coverage gaps identified in Sessions 7 and 9:

1. **Fix BackofficeIdentity integration tests** (6/6 currently failing with 500 errors)
2. **Run UserManagement E2E tests** (12 scenarios created but not executed)
3. **Run regression tests** (PricingAdmin, WarehouseAdmin, ProductAdmin E2E suites)
4. **Document learnings** and update skill files
5. **Write M32.3 milestone retrospective**

**Success Criteria:**
- ✅ All 6 BackofficeIdentity integration tests passing
- ✅ All 12 UserManagement E2E scenarios passing
- ✅ No regressions in existing E2E test suites (PricingAdmin, WarehouseAdmin, ProductAdmin)
- ✅ Build: 0 errors
- ✅ Test coverage: 80%+ overall
- ✅ M32.3 retrospective written
- ✅ CURRENT-CYCLE.md updated

---

## Session Priorities

### 🚨 Priority 1: Fix BackofficeIdentity Integration Tests (CRITICAL)

**Status:** 🚨 **BLOCKING** — 6/6 tests failing with 500 errors

**Issue:** Handler wiring not resolving command/response parameters in integration tests.

**Error Message:** `Alba.ScenarioAssertionException : Expected status code 200, but was 500`

**Affected Tests:**
1. `ResetPassword_WithValidUserId_UpdatesPasswordHashAndInvalidatesRefreshToken()`
2. `ResetPassword_WithNonExistentUser_Returns404()`
3. `ResetPassword_WithPasswordLessThan8Chars_FailsValidation()`
4. `ResetPassword_WithEmptyPassword_FailsValidation()`
5. `ResetPassword_PreservesOtherUserFields()`
6. `ResetPassword_WithDeactivatedUser_StillWorksButUserStaysDeactivated()`

**Investigation Steps:**
1. Read `ResetBackofficeUserPasswordHandler.cs` to understand command handler implementation
2. Read `BackofficeIdentity.Api/Program.cs` to verify Wolverine handler discovery configuration
3. Check if BackofficeIdentity handler assembly is included in `opts.Discovery.IncludeAssembly()`
4. Compare pattern to working integration tests (Pricing, Payments, Inventory)
5. Verify `ResetBackofficeUserPasswordEndpoint.cs` endpoint signature matches handler output
6. Check if test fixture needs to explicitly include BackofficeIdentity handler assembly

**Endpoint Signature (ResetBackofficeUserPasswordEndpoint.cs line 16):**
```csharp
public static IResult Handle(
    Guid userId,
    string newPassword,
    ResetPasswordResponse? response,  // ← Injected by Wolverine from handler
    ProblemDetails? problem)          // ← Injected by Wolverine from handler
```

**Root Cause Hypothesis:**
- **Wolverine Compound Handler Pattern:** Endpoint expects `response` and `problem` to be injected by Wolverine after command handler executes
- **Handler Discovery:** Command handler may not be discovered in test context
- **Assembly Inclusion:** Handler assembly may not be included in TestServer DI container

**Expected Fix:**
Add handler assembly to Wolverine discovery in `BackofficeIdentityApiFixture.cs`:
```csharp
builder.Host.UseWolverine(opts =>
{
    // Include API assembly (endpoints)
    opts.Discovery.IncludeAssembly(typeof(BackofficeIdentity.Api.Program).Assembly);

    // Include domain assembly (handlers) ← LIKELY MISSING
    opts.Discovery.IncludeAssembly(typeof(BackofficeIdentity.ResetBackofficeUserPassword).Assembly);
});
```

**Success Criteria:**
- ✅ All 6 tests passing
- ✅ Password hash verified as changed
- ✅ Refresh token verified as null (security-critical)

**Estimated Time:** 45-60 minutes

---

### 🎯 Priority 2: Run UserManagement E2E Tests (HIGH)

**Status:** 📋 **NOT EXECUTED** — 12 scenarios created in Session 9 but not run

**Build Status:** ✅ Compiles successfully (0 errors, 13 pre-existing warnings)

**Test Discovery:** ✅ All 12 scenarios recognized by test runner

**Scenarios:**
1. Browse user list (3 users)
2. Search users by email
3. Create new user (happy path)
4. Create user with duplicate email
5. Validation: Password too short
6. Change user role
7. Reset user password (two-click pattern)
8. Password mismatch validation
9. Deactivate user (two-click pattern)
10. Session expired during user creation
11. Non-SystemAdmin blocked from user management
12. Deactivate section hidden for already-deactivated users

**Execution Steps:**
1. Start infrastructure: `docker-compose --profile infrastructure up -d` (if not already running)
2. Run E2E tests: `dotnet test tests/Backoffice/Backoffice.E2ETests --filter "FullyQualifiedName~UserManagement"`
3. Review Playwright traces for any failures
4. Fix failures (expected: high pass rate based on compilation success)

**High Confidence Factors:**
- Step definitions follow proven patterns from Sessions 5-6
- Page Objects match MudBlazor v9 interaction patterns
- Stub client integration verified via compilation
- Two-click confirmation pattern applied to password reset

**Success Criteria:**
- ✅ All 12 scenarios passing
- ✅ No Playwright timeout errors
- ✅ No MudSelect interaction failures

**Estimated Time:** 30-45 minutes (including failure investigation if any)

---

### 🔍 Priority 3: Run Regression Tests (MEDIUM)

**Status:** 📋 **NOT EXECUTED** — Verify no regressions from Session 9 changes

**Test Suites to Run:**
1. **PricingAdmin.feature** (6 scenarios)
   - Session 5 deliverable
   - Verified compilation + step definition binding in Session 9
2. **WarehouseAdmin.feature** (10 scenarios)
   - Session 6 deliverable
   - No changes since Session 6
3. **ProductAdmin.feature** (6 scenarios)
   - Session 3 deliverable
   - No changes since Session 3

**Execution Steps:**
1. Run PricingAdmin: `dotnet test tests/Backoffice/Backoffice.E2ETests --filter "FullyQualifiedName~PricingAdmin"`
2. Run WarehouseAdmin: `dotnet test tests/Backoffice/Backoffice.E2ETests --filter "FullyQualifiedName~WarehouseAdmin"`
3. Run ProductAdmin: `dotnet test tests/Backoffice/Backoffice.E2ETests --filter "FullyQualifiedName~ProductAdmin"`

**Expected Outcome:** ✅ All scenarios passing (no changes since last successful run)

**Success Criteria:**
- ✅ All PricingAdmin scenarios passing (6/6)
- ✅ All WarehouseAdmin scenarios passing (10/10)
- ✅ All ProductAdmin scenarios passing (6/6)

**Estimated Time:** 20-30 minutes

---

### 📋 Priority 4: Run Full E2E Test Suite (MEDIUM)

**Status:** 📋 **COMPREHENSIVE VALIDATION** — Run all 8 feature files

**Full Test Suite:**
1. Authentication.feature (login, logout, refresh token)
2. Authorization.feature (RBAC verification)
3. CustomerService.feature (CS workflows)
4. OperationsAlerts.feature (alert feed)
5. PricingAdmin.feature (6 scenarios)
6. ProductAdmin.feature (6 scenarios)
7. WarehouseAdmin.feature (10 scenarios)
8. UserManagement.feature (12 scenarios)

**Execution:**
```bash
dotnet test tests/Backoffice/Backoffice.E2ETests
```

**Expected Scenario Count:** 40+ scenarios across all features

**Success Criteria:**
- ✅ All scenarios passing
- ✅ Build: 0 errors
- ✅ No flaky tests (consistent pass rate)

**Estimated Time:** 15-20 minutes (full suite execution)

---

### 📝 Priority 5: Document Learnings (MEDIUM)

**Status:** 📋 **SESSION 10 DOCUMENTATION**

**Documents to Create:**
1. **M32.3 Session 10 Retrospective** (`m32-3-session-10-retrospective.md`)
   - What went well
   - What didn't go well
   - Lessons learned
   - Test coverage metrics
   - Integration test fix details
   - E2E test results summary

2. **M32.3 Milestone Retrospective** (`m32-3-retrospective.md`)
   - Overall milestone summary
   - 10 sessions recap
   - Cumulative deliverables (10 Blazor pages, 4 client interfaces, 34 E2E scenarios, etc.)
   - Key achievements
   - Deferred work
   - Recommendations for M32.4+

**Skill Files to Update:**
1. `docs/skills/e2e-playwright-testing.md`
   - Add two-click confirmation pattern examples
   - Add MudSelect interaction pattern examples
   - Add ScenarioContext URL replacement pattern
2. `docs/skills/critterstack-testing-patterns.md`
   - Add BackofficeIdentity integration test fix details
   - Add Wolverine compound handler pattern in tests

**Success Criteria:**
- ✅ Session 10 retrospective complete
- ✅ M32.3 milestone retrospective complete
- ✅ Skill files updated with new patterns

**Estimated Time:** 45-60 minutes

---

### 🔄 Priority 6: Update CURRENT-CYCLE.md (LOW)

**Status:** 📋 **MILESTONE COMPLETION**

**Updates Required:**
1. Move M32.3 from "Active Milestone" to "Recent Completions"
2. Update Quick Status table (M32.3 → COMPLETE)
3. Add M32.3 Session 10 progress summary
4. Add M32.3 retrospective link
5. Update "Last Updated" timestamp

**Success Criteria:**
- ✅ CURRENT-CYCLE.md reflects M32.3 completion
- ✅ All session retrospective links added

**Estimated Time:** 10-15 minutes

---

## Session Workflow

### Phase 1: Integration Test Fix (60 minutes)
1. **Investigate handler wiring** (15 min)
   - Read BackofficeIdentity handler implementation
   - Read BackofficeIdentity.Api Program.cs configuration
   - Compare to working integration tests
2. **Apply fix** (10 min)
   - Add handler assembly to Wolverine discovery
   - Rebuild BackofficeIdentity.Api.IntegrationTests
3. **Run integration tests** (5 min)
   - Execute all 6 ResetBackofficeUserPasswordTests
4. **Verify results** (10 min)
   - Confirm all tests passing
   - Verify password hash changed
   - Verify refresh token nullified
5. **Commit integration test fix** (5 min)

### Phase 2: E2E Test Execution (90 minutes)
1. **Start infrastructure** (5 min)
   - `docker-compose --profile infrastructure up -d`
2. **Run UserManagement E2E tests** (30 min)
   - Execute 12 scenarios
   - Investigate failures if any
   - Fix failures and re-run
3. **Run regression tests** (30 min)
   - PricingAdmin, WarehouseAdmin, ProductAdmin
   - Investigate failures if any
4. **Run full E2E suite** (20 min)
   - All 8 feature files
   - Final verification
5. **Commit E2E test results** (5 min)

### Phase 3: Documentation (90 minutes)
1. **Write Session 10 retrospective** (30 min)
   - Test coverage metrics
   - Integration test fix details
   - E2E test results summary
2. **Write M32.3 milestone retrospective** (40 min)
   - 10 sessions recap
   - Cumulative deliverables
   - Key achievements
3. **Update skill files** (20 min)
   - e2e-playwright-testing.md
   - critterstack-testing-patterns.md

### Phase 4: Cleanup & Commit (30 minutes)
1. **Update CURRENT-CYCLE.md** (10 min)
2. **Final build verification** (5 min)
3. **Final commit** (5 min)
4. **Session summary** (10 min)

**Total Estimated Time:** 4-5 hours

---

## Exit Criteria

### Must-Have (Blocking M32.3 Completion)
- ✅ All 6 BackofficeIdentity integration tests passing
- ✅ All 12 UserManagement E2E scenarios passing
- ✅ No regressions in PricingAdmin, WarehouseAdmin, ProductAdmin E2E tests
- ✅ Build: 0 errors across all projects
- ✅ Session 10 retrospective written
- ✅ M32.3 milestone retrospective written
- ✅ CURRENT-CYCLE.md updated

### Should-Have (Non-Blocking)
- ✅ Full E2E suite passing (all 8 feature files)
- ✅ Skill files updated with new patterns
- ✅ Test coverage documented (80%+ target)

### Nice-to-Have (M32.4+ Enhancements)
- 📋 GET /api/backoffice-identity/users/{userId} endpoint (performance optimization)
- 📋 Table sorting in UserList.razor
- 📋 Enhanced error messages (400 vs 500 vs 503)

---

## Risks & Mitigation

### R1: Integration Tests May Require Complex Fix (HIGH)

**Risk:** Handler wiring fix may be more complex than expected (e.g., requires custom middleware, complex DI registration).

**Mitigation:**
1. Time-box investigation to 30 minutes
2. If no solution found, defer to dedicated follow-up session
3. Document findings in retrospective for future reference

**Fallback:** Mark integration tests as "known issue" and defer to M32.4 if fix exceeds time budget.

---

### R2: E2E Tests May Have Environment-Specific Failures (MEDIUM)

**Risk:** Playwright tests may fail due to environment differences (timing, browser version, Postgres state).

**Mitigation:**
1. Enable Playwright tracing for all test runs
2. Investigate failures using trace viewer
3. Add explicit waits if timing issues detected
4. Clean database state between test runs

**Fallback:** Document failures as "known flaky tests" and investigate in M32.4.

---

### R3: Session May Run Over Time Budget (LOW)

**Risk:** 4-5 hour estimate may be optimistic if multiple issues arise.

**Mitigation:**
1. Prioritize must-have items (P1, P2)
2. Defer nice-to-have items to M32.4
3. Write retrospective even if not all tests passing

**Fallback:** Close M32.3 with documented gaps and create M32.4 for remaining work.

---

## References

- **Session 7 Retrospective:** `docs/planning/milestones/m32-3-session-7-retrospective.md`
- **Session 7 QA/UX Analysis:** `docs/planning/milestones/m32-3-session-7-qa-ux-analysis.md`
- **Session 9 Retrospective:** `docs/planning/milestones/m32-3-session-9-retrospective.md`
- **Session 9 Plan:** `docs/planning/milestones/m32-3-session-9-plan.md`
- **Skills:**
  - `docs/skills/e2e-playwright-testing.md` — Playwright patterns
  - `docs/skills/critterstack-testing-patterns.md` — Integration test patterns
  - `docs/skills/wolverine-message-handlers.md` — Compound handler patterns

---

## Success Definition

**M32.3 Session 10 is complete when:**
1. All integration tests passing (6/6)
2. All UserManagement E2E tests passing (12/12)
3. No regressions in existing E2E suites
4. Session 10 retrospective written
5. M32.3 milestone retrospective written
6. CURRENT-CYCLE.md updated
7. Build: 0 errors

**M32.3 Milestone is production-ready when:**
- Test coverage ≥ 80% (E2E + integration)
- All critical workflows verified via E2E tests
- No known security issues (password reset refresh token invalidation verified)
- Documentation complete

---

**Plan Created By:** Claude Sonnet 4.5
**Date:** 2026-03-20
**Milestone:** M32.3 (Backoffice Phase 3B: Write Operations Depth)
