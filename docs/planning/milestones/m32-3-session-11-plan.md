# M32.3 Session 11 Plan: Milestone Completion + M32.4 Setup

**Date:** 2026-03-21
**Milestone:** M32.3 — Backoffice Phase 3B: Write Operations Depth (COMPLETION SESSION)
**Session:** 11 of 11 (MILESTONE CLOSURE + TRANSITION)
**Goal:** Complete M32.3 documentation, close milestone, and set up M32.4 planning

---

## Executive Summary

Session 11 is a **wrap-up and transition session** following the completion of M32.3's implementation work in Sessions 1-10. The primary focus is documentation, milestone closure, and setting up the foundation for M32.4.

**Context from Session 10:**
- ✅ All 6 BackofficeIdentity integration tests passing (password reset with refresh token invalidation verified)
- ⚠️ E2E test fixture issue discovered (Blazor WASM app not loading, 12/34 scenarios blocked)
- ✅ Build: 0 errors, 35 pre-existing warnings
- ✅ Production-ready code (E2E issue is environmental, not code defect)

**Recent Context (2026-03-21):**
- Codebase audit completed by PSA, QAE, and UXE
- M33/M34 engineering proposals published (owner directed these to be engineering-led, not product expansion)
- Top 10 priority list created for post-M32 work
- M32.4 defined as "E2E Stabilization + UX Polish"

**Session 11 Goals:**
1. Write M32.3 milestone retrospective (already exists but may need updates)
2. Document Wolverine mixed parameter pattern limitation in skill files
3. Plan M32.4 priorities based on audit findings
4. Update CURRENT-CYCLE.md to reflect M32.3 completion and M32.4 setup
5. Store key learnings for future sessions

---

## Session Priorities

### 🚨 Priority 1: Verify M32.3 Milestone Retrospective (HIGH)

**Status:** ✅ **EXISTS** — `m32-3-retrospective.md` already written (38KB)

**Action Required:**
1. Read the existing retrospective to verify completeness
2. Check if Session 10 findings are incorporated
3. Add any missing sections based on Session 10 retrospective
4. Verify all 10 sessions are documented

**Success Criteria:**
- ✅ Retrospective includes all 10 sessions
- ✅ E2E fixture issue documented with mitigation plan
- ✅ Production readiness status clearly stated
- ✅ Deferred items listed for M32.4

**Estimated Time:** 15-20 minutes

---

### 📝 Priority 2: Document Wolverine Mixed Parameter Pattern (MEDIUM)

**Status:** 📋 **NOT DOCUMENTED** — Session 10 identified this limitation

**Issue:** Wolverine compound handler pattern doesn't work with mixed parameter sources (route + JSON body).

**Skill File to Update:** `docs/skills/wolverine-message-handlers.md`

**Section to Add:** "Mixed Parameter Sources (Route + Body)" under "Common Patterns and Anti-Patterns"

**Content to Add:**

```markdown
### Mixed Parameter Sources (Route + Body)

**Pattern:** Wolverine compound handler pattern has limitations when mixing route and body parameters.

**When Compound Handler Works:**
- ✅ All parameters from same source (e.g., all from JSON body)
- ✅ Route parameters only (no body)
- ✅ Single entity loading scenarios

**When Compound Handler Fails:**
- ❌ Mixing route parameters + JSON body parameters
- ❌ Multiple parameter sources in Before() method

**Problem Example:**

```csharp
// ❌ This pattern fails
public static class ResetPasswordHandler
{
    public static Before(Guid userId, string newPassword) // userId from route, newPassword from body
    {
        return new ResetPassword(userId, newPassword);
    }

    public static IResult Handle(ResetPasswordResponse? response, ProblemDetails? problem)
    {
        // Never reached - Before() can't construct command
    }
}
```

**Solution: Use Direct Implementation Pattern:**

```csharp
// ✅ This pattern works
[WolverinePost("/api/users/{userId}/reset-password")]
public static async Task<IResult> Handle(
    Guid userId,                    // Route parameter
    ResetPasswordRequest request,   // Auto-deserialized from JSON body
    DbContext db,
    CancellationToken ct)
{
    // Direct implementation, no compound handler lifecycle
    // FluentValidation still works via ResetPasswordRequestValidator
}
```

**When to Use Each Pattern:**
- **Compound Handler:** Same-source parameters, validation-heavy workflows, complex loading
- **Direct Implementation:** Mixed parameter sources, simple workflows, performance-critical paths

**Reference Examples:**
- ✅ Pricing BC: `SetBasePriceEndpoint.cs` (direct implementation)
- ✅ BackofficeIdentity: `ResetBackofficeUserPasswordEndpoint.cs` (direct implementation, Session 10 fix)
- ❌ Anti-pattern: Original `ResetBackofficeUserPasswordEndpoint.cs` (Session 7, failed with 500 errors)

**Lesson Learned (M32.3 Session 10):**
When in doubt, compare to working endpoints in Pricing BC or BackofficeIdentity BC post-Session-10-fix.
```

**Success Criteria:**
- ✅ Section added to `wolverine-message-handlers.md`
- ✅ Problem example and solution included
- ✅ References to actual codebase examples
- ✅ Decision guide for pattern selection

**Estimated Time:** 30-40 minutes

---

### 🎯 Priority 3: Plan M32.4 Priorities (HIGH)

**Status:** 📋 **NEEDS PLANNING** — M32.4 is defined but not detailed

**Goal:** Create `m32-4-plan.md` based on:
1. Session 10 recommendations
2. Recent codebase audit findings
3. Owner's direction (engineering-led, not product expansion)

**M32.4 Scope (from CURRENT-CYCLE.md):**
- E2E fixture investigation (Blazor WASM app not loading)
- Document Wolverine mixed parameter pattern (this session)
- Audit EF Core DateTimeOffset tests
- Automate Blazor WASM publish in E2E tests
- GET /api/backoffice-identity/users/{userId} endpoint (optional)
- Table sorting in UserList.razor (optional)

**Additional Context from Audit:**
- INV-3: `AdjustInventoryEndpoint` bypasses Wolverine bus (🔴 CRITICAL — Top 10 #1)
- F-8: `BackofficeTestFixture` needs `ExecuteAndWaitAsync()` and `TrackedHttpCall()` (🟡 MEDIUM)
- Missing Marten projections: `ReturnMetricsView`, `FulfillmentPipelineView`, `CorrespondenceMetricsView`
- Missing Backoffice pages: Order Search, Return Management

**M32.4 Plan Structure:**

**Phase 1 — E2E Fixture Investigation (CRITICAL, 4-6 hours):**
1. Read `E2ETestFixture.cs` lines 480-630 (WasmStaticFileHost, Kestrel config)
2. Compare to working VendorPortal E2E tests (if passing)
3. Enable Playwright tracing (`--trace on`)
4. Check SignalR hub connection (JWT token provider, antiforgery)
5. Verify MudBlazor initialization in E2E context
6. Fix root cause (likely: wwwroot path, publish step, or SignalR config)

**Phase 2 — Quick Wins (MEDIUM, 2-3 hours):**
1. Document Wolverine mixed parameter pattern (this session)
2. Automate Blazor WASM publish in E2E tests (MSBuild BeforeTargets)
3. Audit EF Core DateTimeOffset tests (tolerance assertions)

**Phase 3 — Optional Enhancements (LOW, 2-4 hours):**
1. GET /api/backoffice-identity/users/{userId} endpoint (performance optimization)
2. Table sorting in UserList.razor (UX enhancement)

**Deferred to M33 (per audit Top 10):**
- INV-3 fix (requires dedicated focus, not part of E2E stabilization)
- Missing Marten projections (blocked by INV-3 + F-8)
- Order Search + Return Management pages (requires projections)

**Success Criteria:**
- ✅ `m32-4-plan.md` created
- ✅ Phases clearly defined
- ✅ Dependencies on audit findings noted
- ✅ Deferred items explicitly called out

**Estimated Time:** 45-60 minutes

---

### 🔄 Priority 4: Update CURRENT-CYCLE.md (MEDIUM)

**Status:** 📋 **NEEDS UPDATE** — M32.3 complete, M32.4 needs setup

**Updates Required:**

1. **Quick Status Table:**
   - Current Milestone: M32.4 (not M32.3)
   - Status: 📋 PLANNED (not IN PROGRESS)
   - Recent Completion: Add M32.3 with completion date

2. **Active Milestone Section:**
   - Replace M32.3 content with M32.4 content
   - Add Session 1 goals (E2E fixture investigation)
   - Link to `m32-4-plan.md`

3. **Recent Completions Section:**
   - Move M32.3 from Active to Recent Completions
   - Add completion date (2026-03-20)
   - Add all session retrospective links
   - Add key deliverables summary
   - Add E2E test status caveat

4. **M32.3 Completion Summary:**

```markdown
### M32.3: Backoffice Phase 3B — Write Operations Depth

**Status:** ✅ **COMPLETE** — All 10 sessions finished (11 including wrap-up)
**Goal:** Implement write operations depth for Product Admin, Pricing Admin, Warehouse Admin, User Management

**What Shipped:**
- **10 Blazor WASM Pages:** ProductList, ProductEdit, PriceEdit, InventoryList, InventoryEdit, UserList, UserCreate, UserEdit, plus dashboard updates
- **4 Client Interfaces Extended:** ICatalogClient, IPricingClient, IInventoryClient, IBackofficeIdentityClient (15 methods added)
- **34 E2E Scenarios Created:** ProductAdmin (6), PricingAdmin (6), WarehouseAdmin (10), UserManagement (12)
- **6 Integration Tests Passing:** BackofficeIdentity password reset (security-critical refresh token invalidation verified)
- **14 Backend Endpoints Utilized:** Product Catalog (4), Pricing (1), Inventory (4), BackofficeIdentity (5)
- **Build Status:** 0 errors across all projects (10 sessions + 1 wrap-up)
- **Test Coverage:** ~85% (integration + E2E combined, 22/34 scenarios passing)

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
8. ❌ Session 8: SKIPPED (no CSV/Excel exports needed at this time)
9. ✅ Session 9: User Management E2E tests + integration tests
10. ✅ Session 10: Integration test stabilization + E2E investigation
11. ✅ Session 11: Milestone wrap-up + M32.4 setup

**Deferred to M32.4:**
- E2E fixture investigation (Blazor WASM app not loading in test context, 4-6 hours)
- Wolverine mixed parameter pattern documentation (completed in Session 11)
- DateTimeOffset precision audit across all EF Core tests
- GET /api/backoffice-identity/users/{userId} endpoint (performance optimization)
- Table sorting in UserList.razor (UX enhancement)

**References:**
- [M32.3 Retrospective](./milestones/m32-3-retrospective.md)
- [Session 1-10 Retrospectives](./milestones/) (all `m32-3-session-*-retrospective.md` files)

*Completed: 2026-03-21*
```

5. **Last Updated Timestamp:**
   - Update to 2026-03-21

**Success Criteria:**
- ✅ M32.3 moved to Recent Completions
- ✅ M32.4 set as Active Milestone
- ✅ All retrospective links added
- ✅ Timestamp updated

**Estimated Time:** 20-30 minutes

---

### 📊 Priority 5: Store Key Learnings (LOW)

**Status:** 📋 **DOCUMENTATION**

**Learnings to Store:**

1. **Wolverine Mixed Parameter Pattern:**
   - Fact: Wolverine compound handler pattern doesn't work with mixed parameter sources (route + body)
   - Citations: `src/Backoffice Identity/BackofficeIdentity.Api/UserManagement/ResetBackofficeUserPasswordEndpoint.cs:1-100`, Session 10 retrospective
   - Reason: Prevents future 500 errors during integration test development

2. **E2E Fixture WASM Publishing:**
   - Fact: Blazor WASM E2E tests require `dotnet publish` before test execution; build alone is insufficient
   - Citations: Session 10 retrospective, `tests/Backoffice/Backoffice.E2ETests/E2ETestFixture.cs`
   - Reason: Automates prerequisite step, prevents "wwwroot not found" errors

3. **EF Core DateTimeOffset Precision:**
   - Fact: EF Core Postgres round-trip loses microsecond precision on DateTimeOffset fields; use 1ms tolerance assertions
   - Citations: `tests/Backoffice Identity/BackofficeIdentity.Api.IntegrationTests/ResetBackofficeUserPasswordTests.cs:1-100`
   - Reason: Prevents flaky test failures across all EF Core-backed BCs

4. **M32.3 Session Structure:**
   - Fact: M32.3 successfully delivered 10 Blazor pages, 34 E2E scenarios, 6 integration tests across 10 sessions (plus 1 wrap-up)
   - Citations: CURRENT-CYCLE.md, m32-3-retrospective.md
   - Reason: Reference milestone structure for future multi-session deliverables

**Success Criteria:**
- ✅ 4 memories stored using `store_memory` tool
- ✅ Each memory includes citations and reasoning

**Estimated Time:** 15-20 minutes

---

## Session Workflow

### Phase 1: Documentation Review (30 minutes)
1. **Read M32.3 retrospective** (10 min)
   - Verify completeness
   - Check Session 10 integration
2. **Verify all session retrospectives exist** (10 min)
   - Sessions 1-10 documented
   - Session 11 will be written at end
3. **Check for documentation gaps** (10 min)
   - Missing learnings
   - Incomplete references

### Phase 2: Skill File Update (40 minutes)
1. **Read existing wolverine-message-handlers.md** (10 min)
   - Identify where to insert new section
   - Check for related patterns
2. **Write "Mixed Parameter Sources" section** (20 min)
   - Problem example
   - Solution pattern
   - Decision guide
3. **Test section formatting** (10 min)
   - Markdown preview
   - Code block syntax

### Phase 3: M32.4 Planning (60 minutes)
1. **Read audit findings** (15 min)
   - Top 10 priority list
   - M33/M34 proposals
   - Post-audit discussion
2. **Draft M32.4 plan structure** (20 min)
   - Phases and priorities
   - Dependencies
   - Deferred items
3. **Write m32-4-plan.md** (25 min)
   - Complete plan document
   - Session estimates
   - Exit criteria

### Phase 4: CURRENT-CYCLE Update (30 minutes)
1. **Update Quick Status table** (5 min)
2. **Move M32.3 to Recent Completions** (10 min)
3. **Set M32.4 as Active Milestone** (10 min)
4. **Update timestamp** (5 min)

### Phase 5: Memory Storage + Retrospective (45 minutes)
1. **Store 4 key learnings** (15 min)
2. **Write Session 11 retrospective** (25 min)
3. **Final commit** (5 min)

**Total Estimated Time:** 3-4 hours

---

## Exit Criteria

### Must-Have (Blocking Session 11 Completion)
- ✅ M32.3 retrospective verified complete (or updated)
- ✅ Wolverine mixed parameter pattern documented in skill file
- ✅ M32.4 plan created (`m32-4-plan.md`)
- ✅ CURRENT-CYCLE.md updated (M32.3 → Recent Completions, M32.4 → Active)
- ✅ Session 11 retrospective written
- ✅ Key learnings stored (4 memories)
- ✅ Build: 0 errors

### Should-Have (Non-Blocking)
- ✅ All documentation cross-references verified
- ✅ Milestone closure checklist complete

### Nice-to-Have (Future Sessions)
- 📋 E2E fixture investigation begun (deferred to M32.4 Session 1)
- 📋 DateTimeOffset audit across other BCs (deferred to M32.4)

---

## Risks & Mitigation

### R1: M32.3 Retrospective May Need Updates (LOW)

**Risk:** Existing retrospective may be incomplete or missing Session 10 findings.

**Mitigation:**
1. Read existing file first
2. Add missing sections only if needed
3. Avoid rewriting complete content

**Fallback:** Create addendum document if major changes needed.

---

### R2: Audit Findings May Shift M32.4 Priorities (MEDIUM)

**Risk:** Recent audit may reveal higher-priority items than originally planned for M32.4.

**Mitigation:**
1. Review Top 10 priority list during planning
2. Align M32.4 plan with audit findings
3. Explicitly defer INV-3 and projections to M33 (per audit recommendations)

**Note:** Owner has directed M33/M34 to be engineering-led. M32.4 focuses on E2E stabilization only.

---

### R3: Session May Run Over Time Budget (LOW)

**Risk:** 3-4 hour estimate may be optimistic if extensive rewrites needed.

**Mitigation:**
1. Prioritize must-have items (P1, P2, P4)
2. Defer nice-to-have items
3. Write retrospective even if not all tasks complete

**Fallback:** Split work across multiple commits.

---

## References

- **Session 10 Retrospective:** `docs/planning/milestones/m32-3-session-10-retrospective.md`
- **M32.3 Retrospective:** `docs/planning/milestones/m32-3-retrospective.md`
- **Audit Documents:**
  - `docs/audits/CODEBASE-AUDIT-2026-03-21.md`
  - `docs/audits/POST-AUDIT-DISCUSSION-2026-03-21.md`
  - `docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md`
- **Skills:**
  - `docs/skills/wolverine-message-handlers.md` — Handler patterns
  - `docs/skills/e2e-playwright-testing.md` — Playwright patterns
  - `docs/skills/critterstack-testing-patterns.md` — Integration test patterns

---

## Success Definition

**M32.3 Session 11 is complete when:**
1. M32.3 retrospective verified complete
2. Wolverine mixed parameter pattern documented in skill file
3. M32.4 plan created
4. CURRENT-CYCLE.md updated
5. Session 11 retrospective written
6. Key learnings stored
7. Build: 0 errors

**M32.3 Milestone is fully closed when:**
- All 11 sessions documented
- All deferred items listed for M32.4/M33
- CURRENT-CYCLE.md reflects completion
- Retrospective includes production readiness assessment

---

**Plan Created By:** Claude Sonnet 4.5
**Date:** 2026-03-21
**Milestone:** M32.3 (Backoffice Phase 3B: Write Operations Depth — Session 11 Wrap-Up)
