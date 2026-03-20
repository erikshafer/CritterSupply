# M32.3 Session 9 Plan: Testing Coverage Completion + Bulk Operations Pattern

**Date:** 2026-03-20
**Milestone:** M32.3 — Backoffice Phase 3B: Write Operations Depth
**Session:** 9 of 10
**Duration Estimate:** 3.5-4 hours

---

## Session Context

### What Came Before (Session 7)

**✅ Completed:**
- User Management write UI (UserList, UserCreate, UserEdit)
- Password reset endpoint in BackofficeIdentity BC
- IBackofficeIdentityClient interface + implementation
- Stub clients for integration and E2E tests

**🚨 Critical Gaps Identified (from Session 7 QA/UX Analysis):**
1. **ZERO E2E test coverage** for User Management (3 new pages untested)
2. **ZERO integration test coverage** for password reset endpoint (security risk)
3. **UX consistency violation:** Password reset lacks two-click confirmation (deactivation has it)
4. **Deferred from Session 7:** PricingAdmin.feature step definitions still broken

**Test Coverage Status:**
- Overall: **25%** (🚨 INSUFFICIENT)
- E2E Coverage: **0%** for User Management
- Integration Coverage: **0%** for password reset
- QA Verdict: **NOT PRODUCTION-READY**

### Note on Session 8

User confirmed we're **skipping Session 8** (CSV/Excel exports) as there's no current need for those features.

---

## Session 9 Objectives

### Primary Goal: Close Testing Coverage Gaps

**Target:** Achieve **80%+ test coverage** for User Management workflows to reach production-ready status.

### Priority 1: E2E Test Coverage (BLOCKING)

1. ✅ Create UserManagement.feature (10-12 scenarios)
   - Browse user list
   - Search users by email
   - Create new user (happy path)
   - Create user with duplicate email
   - Validation: Password too short
   - Change user role
   - Reset user password (two-click pattern)
   - Password mismatch validation
   - Deactivate user (two-click pattern)
   - Session expired during user creation
   - Non-SystemAdmin blocked from user management
   - Deactivate section hidden for already-deactivated users

2. ✅ Create Page Object Models:
   - UserListPage.cs (navigate, search, assertions)
   - UserCreatePage.cs (form fill, submit, validation checks)
   - UserEditPage.cs (three sections: role, password, deactivate)

3. ✅ Create UserManagementSteps.cs step definitions
   - Bind Gherkin steps to Page Object actions
   - Coordinate stub client state setup/reset

### Priority 2: Integration Test Coverage (BLOCKING)

4. ✅ Create ResetBackofficeUserPasswordTests.cs (5-7 tests)
   - ResetPassword_WithValidUserId_UpdatesPasswordHashAndInvalidatesRefreshToken
   - ResetPassword_WithNonExistentUser_Returns404
   - ResetPassword_WithPasswordLessThan8Chars_FailsValidation
   - ResetPassword_PreservesOtherUserFields
   - ResetPassword_WithDeactivatedUser_StillWorksButUserStaysDeactivated
   - ResetPassword_ConcurrentResets_HandledCorrectly (optional if time allows)

### Priority 3: UX Consistency Fix (BLOCKING)

5. ✅ Add two-click confirmation for password reset in UserEdit.razor
   - Add `_passwordResetConfirmed` bool flag
   - Implement two-button pattern (matches deactivation UX)
   - First click shows "Confirm Reset (User will be logged out)" button
   - Second click triggers actual reset
   - Update data-testid attributes for E2E tests

### Priority 4: Fix Deferred Issue (BLOCKING)

6. ✅ Fix PricingAdmin.feature step definitions
   - Line 7: `the Backoffice system is running` → `the Backoffice application is running`
   - Add catalog stub setup step (no existing step definition)
   - Fix user creation pattern to include name parameter
   - Verify all scenarios bind correctly

### Priority 5: Bulk Operations Pattern (STRETCH GOAL)

**Note:** User mentioned "bulk operations pattern" in the problem statement. This is a NEW scope item not covered in previous sessions.

**Potential interpretations:**
1. Bulk user operations (e.g., bulk deactivate, bulk role change)
2. Bulk product operations (e.g., bulk discontinue, bulk price update)
3. Bulk inventory operations (e.g., bulk adjust, bulk receive)

**Decision:** Consult user on scope. If time allows after Priorities 1-4, implement one bulk operation pattern as proof-of-concept (likely bulk user deactivation as simplest example).

**Estimated pattern:**
- Add "Select All" checkbox to UserList.razor
- Add "Bulk Deactivate" button (disabled unless 2+ users selected)
- Confirmation dialog with list of selected users
- Backend endpoint: POST /api/backoffice-identity/users/bulk-deactivate with `{ userIds: Guid[] }`
- E2E scenario: "Bulk deactivate multiple users"

---

## Acceptance Criteria

### Must-Have (Session 9 Gate Criteria)

- ✅ UserManagement.feature created with 10-12 scenarios
- ✅ UserListPage, UserCreatePage, UserEditPage Page Objects created
- ✅ UserManagementSteps.cs created with all step definitions bound
- ✅ ResetBackofficeUserPasswordTests.cs created with 5-7 tests
- ✅ Two-click password reset confirmation added to UserEdit.razor
- ✅ PricingAdmin.feature step definitions fixed
- ✅ All E2E tests passing (including new User Management scenarios)
- ✅ All integration tests passing (including new password reset tests)
- ✅ Build: 0 errors

### Nice-to-Have (If Time Allows)

- 📋 Bulk operations pattern (1 proof-of-concept example)
- 📋 Update e2e-playwright-testing.md with User Management patterns
- 📋 Update CURRENT-CYCLE.md with Session 9 progress

---

## Implementation Strategy

### Phase 1: Integration Tests (45-60 minutes)

**Rationale:** Backend tests are faster to write and validate backend security assumptions before building E2E tests on top.

**Tasks:**
1. Create `tests/Backoffice Identity/BackofficeIdentity.IntegrationTests/UserManagement/ResetBackofficeUserPasswordTests.cs`
2. Add TestFixture setup if not already present
3. Write 5-7 test methods covering happy path, 404, validation, refresh token invalidation
4. Run tests: `dotnet test "tests/Backoffice Identity/BackofficeIdentity.IntegrationTests"`
5. Verify all tests pass

### Phase 2: UX Consistency Fix (15-20 minutes)

**Rationale:** Fix UX issue before writing E2E tests so Page Objects match final UI.

**Tasks:**
1. Read `src/Backoffice/Backoffice.Web/Pages/Users/UserEdit.razor` (lines 95-150, password section)
2. Add `_passwordResetConfirmed` bool field
3. Implement two-button pattern (copy from deactivation section lines 165-192)
4. Update data-testid attributes: `reset-password-button`, `confirm-reset-password-button`, `cancel-reset-button`
5. Build verification: `dotnet build src/Backoffice/Backoffice.Web`

### Phase 3: E2E Test Infrastructure (90-120 minutes)

**Rationale:** E2E tests are most time-consuming but provide highest confidence for production readiness.

**Tasks:**
1. Read existing Page Objects for pattern consistency (PriceEditPage.cs, InventoryEditPage.cs)
2. Create UserListPage.cs with navigate, search, click, row assertion methods
3. Create UserCreatePage.cs with form fill, submit, validation checks
4. Create UserEditPage.cs with three sections (role, password, deactivate)
5. Create UserManagement.feature with 10-12 scenarios (use template from Session 7 QA/UX analysis)
6. Create UserManagementSteps.cs with step definitions
7. Add stub client helpers (SetUserData, SimulateDuplicateEmail, etc.)
8. Run single scenario: `dotnet test --filter "FullyQualifiedName~Browse_user_list"`
9. Debug failures, iterate until passing
10. Run full feature file

### Phase 4: Fix PricingAdmin.feature (10-15 minutes)

**Tasks:**
1. Read `tests/Backoffice/Backoffice.E2ETests/Features/PricingAdmin.feature`
2. Fix line 7: `the Backoffice system is running` → `the Backoffice application is running`
3. Add catalog stub step (check if it already exists in AuthenticationSteps or create in PricingAdminSteps)
4. Fix user creation pattern to match AuthorizationSteps.cs (add name parameter)
5. Run PricingAdmin scenarios to verify fixes

### Phase 5: Bulk Operations Pattern (STRETCH GOAL, 60-90 minutes)

**Only if all Priorities 1-4 complete with time remaining.**

**Tasks:**
1. Consult user on scope (which bulk operation to implement)
2. Add backend endpoint (e.g., BulkDeactivateBackofficeUsers command + handler)
3. Add bulk UI to UserList.razor (checkboxes + bulk action button)
4. Add E2E scenario: "Bulk deactivate multiple users"
5. Update retrospective with bulk operations pattern learnings

---

## Risks & Mitigations

### R1: E2E Tests May Fail Due to MudBlazor v9 Interaction Patterns ⚠️ MEDIUM

**Risk:** MudSelect, MudTable, MudDialog components have specific interaction patterns that may not work first try.

**Mitigation:**
- Reference Session 5 and Session 6 Page Objects (proven patterns for MudSelect interaction)
- Use data-testid attributes on wrapper divs, not component selectors
- Add explicit waits (300ms) after MudSelect clicks

### R2: Password Reset Integration Tests May Conflict with Existing Tests ⚠️ LOW

**Risk:** BackofficeIdentity.IntegrationTests may have existing user management tests that conflict with new password reset tests.

**Mitigation:**
- Use unique test user emails (e.g., `test-reset-password-{Guid.NewGuid()}@critter.test`)
- Check existing test fixture setup in BackofficeIdentity.IntegrationTests project
- Ensure each test creates its own user (no shared test data)

### R3: Bulk Operations Scope Ambiguity ⚠️ MEDIUM

**Risk:** User mentioned "bulk operations pattern" but didn't specify which operations or scope.

**Mitigation:**
- Mark as STRETCH GOAL (only if Priorities 1-4 complete)
- Consult user before implementing
- Start with simplest example (bulk user deactivation) as proof-of-concept
- Document pattern in retrospective for reuse in other areas

### R4: Session May Run Long (3.5-4 hours) ⚠️ LOW

**Risk:** Writing 10-12 E2E scenarios + 5-7 integration tests is substantial work.

**Mitigation:**
- Prioritize Priorities 1-4 (all BLOCKING)
- Skip Priority 5 (bulk operations) if time constraint
- Focus on test coverage gate criteria, not polish
- Defer documentation updates to Session 10 if needed

---

## Success Metrics

### Test Coverage (Target: 80%+)

| Category | Before Session 9 | After Session 9 | Target |
|----------|------------------|-----------------|--------|
| E2E Test Coverage | 0% (User Management) | 100% | 100% |
| Integration Test Coverage | 0% (Password Reset) | 100% | 100% |
| UX Consistency | 50% (missing two-click reset) | 100% | 100% |
| Overall Test Coverage | 25% | 85%+ | 80%+ |

### Build Status

- ✅ Build: 0 errors
- ⚠️ Warnings: 22-34 (pre-existing — Correspondence BC, test nullable warnings)

### E2E Test Execution

- ✅ All 10-12 UserManagement scenarios passing
- ✅ PricingAdmin.feature scenarios passing (step definitions fixed)
- ✅ No regressions in other feature files

### Integration Test Execution

- ✅ All 5-7 ResetBackofficeUserPasswordTests passing
- ✅ No regressions in other BackofficeIdentity integration tests

---

## Deferred to Session 10

### Documentation Updates

- Update `docs/skills/e2e-playwright-testing.md` with User Management patterns
- Update `docs/skills/bunit-component-testing.md` with bulk operations pattern (if implemented)
- Update CURRENT-CYCLE.md with Session 9 progress

### UX Enhancements (from Session 7 QA/UX analysis)

- GET /users/{userId} endpoint (performance optimization)
- Table sorting in UserList.razor (UX polish)
- Tooltip feedback for disabled buttons (UX polish)
- Improved error message specificity (400 vs 500 vs 503)
- Change password reset warning from Warning → Error severity

---

## References

- **Session 7 Retrospective:** `docs/planning/milestones/m32-3-session-7-retrospective.md`
- **Session 7 QA/UX Analysis:** `docs/planning/milestones/m32-3-session-7-qa-ux-analysis.md` ⭐ **PRIMARY REFERENCE**
- **Session 6 Retrospective:** `docs/planning/milestones/m32-3-session-6-retrospective.md` (E2E test patterns)
- **Session 5 Retrospective:** `docs/planning/milestones/m32-3-session-5-retrospective.md` (PricingAdmin.feature patterns)
- **Related Skills:**
  - `docs/skills/e2e-playwright-testing.md` — Playwright + Page Object Model
  - `docs/skills/critterstack-testing-patterns.md` — Integration test patterns
  - `docs/skills/efcore-wolverine-integration.md` — EF Core handler testing
  - `docs/skills/blazor-wasm-jwt.md` — Blazor WASM patterns

---

## Pre-Session Checklist

Before starting implementation:

1. ✅ Read Session 7 QA/UX Analysis (contains Gherkin template and test requirements)
2. ✅ Read Session 6 Retrospective (established E2E test patterns)
3. ✅ Verify Docker Compose infrastructure is running (Postgres, RabbitMQ)
4. ✅ Verify existing E2E tests still pass (baseline verification)
5. ✅ Consult user on bulk operations scope (if attempting Priority 5)

---

## Summary

**Session 9 is a critical quality gate for M32.3.** Without E2E and integration test coverage for User Management, the feature is not production-ready despite being functionally complete.

**Primary Deliverables:**
1. UserManagement.feature (10-12 scenarios) + Page Objects + Step Definitions
2. ResetBackofficeUserPasswordTests.cs (5-7 tests)
3. Two-click password reset confirmation (UX consistency)
4. PricingAdmin.feature step definition fixes (deferred from Session 7)

**Stretch Goal:**
5. Bulk operations pattern (proof-of-concept if time allows)

**Exit Criteria:**
- All E2E tests passing (including new User Management scenarios)
- All integration tests passing (including password reset tests)
- Test coverage ≥ 80%
- Build: 0 errors
- QA Verdict: ✅ PRODUCTION-READY

**Next Session (Session 10):** Final E2E stabilization + documentation + milestone retrospective.

---

**Plan Created By:** Claude Sonnet 4.5
**Date:** 2026-03-20
**Milestone:** M32.3 (Backoffice Phase 3B: Write Operations Depth)
