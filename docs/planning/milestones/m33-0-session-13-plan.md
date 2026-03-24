# M33.0 Session 13 Plan — Phase 4: Vendor Portal Structural Refactor

**Date:** 2026-03-23
**Phase:** M33.0 Phase 4 (Vendor Portal Structural Refactor)
**Branch:** `claude/m33-phase-4-vendor-portal-refactor`

---

## Session Context

### Phase 3 Status Verification
According to Session 12 retrospective, Phase 3 (Returns BC structural refactor) is **COMPLETE**:
- ✅ R-1: All 11 commands migrated to vertical slices (Session 11)
- ✅ R-3: Bulk files deleted (Session 11)
- ✅ R-4: Handler file exploded (Session 10)
- ✅ R-5: Query handlers created (Session 12)
- ✅ R-6: Validators added (Session 11)
- ✅ R-7: Integration handler moved (Session 12)
- ✅ R-8: Folder renamed to `ReturnProcessing/` (Session 12)

Build status: 0 errors, 36 pre-existing warnings (unchanged)

### Session 13 Objective
Execute Phase 4 of M33.0: Vendor Portal structural refactor following the same vertical slice patterns established in Returns BC and documented in ADR 0039.

---

## Phase 4 Work Items (from M33-M34 Proposal)

From `docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md`:

| Item | Finding | Effort | Priority | Notes |
|------|---------|--------|----------|-------|
| **F-2 Phase A** | Remove feature-level `@ignore` tags | S | 1 | Do first - enables CI signal |
| **VP-1** | Flatten `ChangeRequests/Commands/` + `ChangeRequests/Handlers/` | M | 2 | Create vertical slices |
| **VP-2** | Flatten `VendorAccount/Commands/` + `VendorAccount/Handlers/` | M | 3 | Create vertical slices |
| **VP-3** | Flatten `Analytics/Handlers/` → `Analytics/` | S | 4 | Move handlers up one level |
| **VP-4** | Explode `CatalogResponseHandlers.cs` (7 handlers) | S | 5 | Must coordinate with UXE rename |
| **VP-5** | Split `VendorHubMessages.cs` | S | 6 | One file per message |
| **VP-6** | Add `AbstractValidator<T>` to all 7 commands | M | 7 | Follow ADR 0039 pattern |
| **UXE ❌ #2** | Rename `MoreInfoRequestedForChangeRequest` → `AdditionalInfoRequested` | S | 5 | MUST ship with VP-4 |
| **UXE ❌ #1** | Rename `DeliveryFailed` → `MessageDeliveryFailed` in Correspondence BC | S | 8 | Sets canonical name |

**Key Constraints:**
- VP-4 refactor and `MoreInfoRequestedForChangeRequest` rename MUST be one changeset
- All validator placement MUST follow ADR 0039 (published in Session 8)

---

## Session 13 Execution Plan

### Part 1: E2E Test Preparation (F-2 Phase A)
**Goal:** Remove feature-level `@ignore` tags, add scenario-level `@ignore` with comments

**Tasks:**
1. Find all Vendor Portal E2E feature files
2. Remove `@ignore` tags at feature level
3. Add scenario-level `@ignore` tags with blocking-reason comments for unbound steps
4. Document 3 unbound feature files as GitHub Issues (tracking Phase B work for M34)
5. Run E2E tests to verify CI signal is present (even if some scenarios are ignored)

**Success Criteria:**
- No feature-level `@ignore` tags remain
- Scenario-level `@ignore` has clear comments explaining blockers
- At least some E2E scenarios run in CI (not all ignored)

---

### Part 2: ChangeRequests Vertical Slices (VP-1)
**Goal:** Flatten `ChangeRequests/Commands/` + `ChangeRequests/Handlers/` → single slice files

**Current Structure (to investigate):**
```
src/Vendor Portal/VendorPortal/
├── ChangeRequests/
│   ├── Commands/
│   │   └── [Command files]
│   └── Handlers/
│       └── [Handler files]
```

**Target Structure:**
```
src/Vendor Portal/VendorPortal/
└── ChangeRequests/
    ├── SubmitChangeRequest.cs (command + handler + validator)
    ├── CancelChangeRequest.cs (command + handler + validator)
    └── [other command slices]
```

**Tasks:**
1. Inventory all command files in `ChangeRequests/Commands/`
2. Inventory all handler files in `ChangeRequests/Handlers/`
3. For each command/handler pair:
   - Create single slice file: `CommandName.cs`
   - Include: command record, validator (if exists), handler
   - Follow ADR 0039 pattern (validator at top level, same file)
4. Delete empty `Commands/` and `Handlers/` folders
5. Verify build succeeds
6. Run Vendor Portal integration tests

**Expected Commands:**
- SubmitChangeRequest (high-risk - user-supplied payload)
- CancelChangeRequest
- [others to discover during inventory]

---

### Part 3: VendorAccount Vertical Slices (VP-2)
**Goal:** Flatten `VendorAccount/Commands/` + `VendorAccount/Handlers/` → single slice files

**Tasks:**
1. Inventory all command files in `VendorAccount/Commands/`
2. Inventory all handler files in `VendorAccount/Handlers/`
3. For each command/handler pair:
   - Create single slice file: `CommandName.cs`
   - Include: command record, validator (if exists), handler
   - Follow ADR 0039 pattern
4. Delete empty `Commands/` and `Handlers/` folders
5. Verify build succeeds
6. Run Vendor Portal integration tests

---

### Part 4: Analytics Handlers Flattening (VP-3)
**Goal:** Move handlers from `Analytics/Handlers/` directly to `Analytics/`

**Tasks:**
1. Inventory all handler files in `Analytics/Handlers/`
2. Move each handler file up one level to `Analytics/`
3. Delete empty `Handlers/` folder
4. Update namespace declarations (if needed)
5. Verify build succeeds

---

### Part 5: Catalog Response Handlers + Rename (VP-4 + UXE ❌ #2)
**Goal:** Explode `CatalogResponseHandlers.cs` + rename `MoreInfoRequestedForChangeRequest` → `AdditionalInfoRequested`

**⚠️ CRITICAL:** These MUST ship in the same changeset per M33 proposal

**Tasks:**
1. Read `CatalogResponseHandlers.cs` to understand all 7 handlers
2. Find all references to `MoreInfoRequestedForChangeRequest` event
3. Create single commit with:
   - Rename event: `MoreInfoRequestedForChangeRequest` → `AdditionalInfoRequested`
   - Update all event references (handlers, tests, consumers)
   - Explode handler file → 7 individual handler files
4. Verify build succeeds
5. Run integration tests

**Expected Handlers:**
- ProductApprovedHandler
- ProductRejectedHandler
- AdditionalInfoRequestedHandler (renamed from MoreInfoRequested...)
- PriceApprovedHandler
- PriceRejectedHandler
- DiscontinuedHandler
- [1-2 others to discover]

---

### Part 6: VendorHubMessages Split (VP-5)
**Goal:** Split `VendorHubMessages.cs` → one file per message record

**Tasks:**
1. Read `VendorHubMessages.cs` to inventory all message types
2. Create one file per message record
3. Delete bulk file
4. Verify build succeeds

---

### Part 7: Add Validators (VP-6)
**Goal:** Add `AbstractValidator<T>` to all 7 VP commands following ADR 0039

**High-Risk Commands (prioritize):**
- SubmitChangeRequest (user-supplied payload with no guard)

**Tasks:**
1. For each command without a validator:
   - Add `AbstractValidator<T>` in same file as command + handler
   - Add appropriate validation rules
   - Follow ADR 0039 pattern (top-level class, not nested)
2. Run integration tests to verify validation works
3. Verify build succeeds

---

### Part 8: Correspondence BC Event Rename (UXE ❌ #1)
**Goal:** Rename `DeliveryFailed` → `MessageDeliveryFailed` in Correspondence BC

**Tasks:**
1. Find `DeliveryFailed` event in Correspondence BC
2. Rename to `MessageDeliveryFailed`
3. Update all internal handlers
4. Update `CorrespondenceMetricsView` projection
5. Verify build succeeds
6. Run Correspondence integration tests

---

## Exit Criteria

Phase 4 is complete when ALL of the following are true:

1. ✅ F-2 Phase A: Feature-level `@ignore` tags removed, scenario-level tags added with comments
2. ✅ VP-1: ChangeRequests commands in vertical slice files
3. ✅ VP-2: VendorAccount commands in vertical slice files
4. ✅ VP-3: Analytics handlers moved to `Analytics/` folder
5. ✅ VP-4 + UXE ❌ #2: `CatalogResponseHandlers.cs` exploded + `AdditionalInfoRequested` rename in ONE commit
6. ✅ VP-5: `VendorHubMessages.cs` split into individual files
7. ✅ VP-6: All 7 VP commands have validators following ADR 0039
8. ✅ UXE ❌ #1: Correspondence BC `MessageDeliveryFailed` rename complete
9. ✅ Build: 0 errors, 36 warnings (unchanged from Session 12)
10. ✅ All previously-passing tests still pass
11. ✅ Session retrospective created
12. ✅ CURRENT-CYCLE.md updated

---

## Commit Strategy

**Recommended commits (8-12 total):**
1. Create session plan document
2. F-2 Phase A: Remove feature-level `@ignore` tags
3. VP-1: ChangeRequests vertical slices
4. VP-2: VendorAccount vertical slices
5. VP-3: Analytics handlers flattening
6. VP-4 + UXE ❌ #2: CatalogResponseHandlers + MoreInfoRequested rename (ATOMIC)
7. VP-5: VendorHubMessages split
8. VP-6: Add validators (may be multiple commits, one per command group)
9. UXE ❌ #1: Correspondence MessageDeliveryFailed rename
10. Session retrospective + CURRENT-CYCLE.md update

---

## References

- **M33.0 Proposal:** `docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md`
- **ADR 0039:** `docs/decisions/0039-canonical-validator-placement.md`
- **Vertical Slice Skill:** `docs/skills/vertical-slice-organization.md`
- **Session 12 Retrospective:** `docs/planning/milestones/m33-0-session-12-retrospective.md`

---

## Risk Mitigation

### Known Risks
1. **VP-4/UXE rename coordination:** Handler file is primary consumer of renamed event
   - Mitigation: Single atomic commit with both changes

2. **Validator placement:** Must follow ADR 0039 exactly
   - Mitigation: Reference ADR before each validator addition

3. **Test failures:** Auth issues in Vendor Portal tests (similar to Returns BC)
   - Mitigation: Document pre-existing vs new failures; focus on build errors first

### Pre-existing Issues to Document
- Returns BC has 14 auth-related test failures (documented in Session 10)
- These are infrastructure issues, not refactoring-related

---

## Session Duration Estimate

**Total:** 3-4 hours

**Breakdown:**
- Part 1 (F-2 Phase A): 30 minutes
- Part 2 (VP-1): 45 minutes
- Part 3 (VP-2): 45 minutes
- Part 4 (VP-3): 15 minutes
- Part 5 (VP-4 + UXE rename): 45 minutes (atomic commit requires care)
- Part 6 (VP-5): 20 minutes
- Part 7 (VP-6): 45 minutes
- Part 8 (UXE Correspondence): 20 minutes
- Documentation: 20 minutes

---

## Success Indicators

- All 8 Phase 4 work items complete
- Build succeeds with 0 errors
- Pre-existing warning count unchanged (36 warnings)
- Vendor Portal follows same vertical slice patterns as Returns BC
- ADR 0039 validator placement pattern applied consistently
- Session retrospective documents learnings and patterns
