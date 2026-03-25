# M34.0 Plan — Stabilization First, Then Experience Completion

**Status:** ✅ Planning complete — ready to begin  
**Date:** 2026-03-25  
**Owners:** M34.0 planning panel (architecture, QA, UX, product synthesis)

---

## Why M34.0 starts differently

M33.0 closed with meaningful progress, but too much effort was spent fighting E2E signal quality instead of shipping confidently. The lesson is clear: M34.0 cannot begin with feature implementation. It must begin by restoring a trustworthy feedback loop, especially for Backoffice and Vendor Portal.

This plan is based on:

- `docs/planning/CURRENT-CYCLE.md`
- `docs/planning/milestones/m33-0-milestone-closure-retrospective.md`
- `docs/planning/milestones/m33-0-e2e-test-efforts-retrospective.md`
- `docs/planning/milestones/m33-0-post-mortem-recovery-review.md`
- `CONTEXTS.md`
- `docs/skills/e2e-playwright-testing.md`
- `docs/skills/reqnroll-bdd-testing.md`
- `docs/skills/bunit-component-testing.md`
- `docs/skills/critterstack-testing-patterns.md`
- `docs/skills/vertical-slice-organization.md`
- `docs/skills/wolverine-message-handlers.md`
- `docs/skills/marten-event-sourcing.md`
- `docs/skills/event-sourcing-projections.md`
- GitHub Issue #460: Vendor Portal Dashboard RBAC — ReadOnly users cannot view change requests
- GitHub Actions E2E workflow run 302 (`Backoffice E2E` and `Vendor Portal E2E`)

---

## Phase 1 findings that shape the plan

### 1. Backoffice E2E is currently not trustworthy

The latest Backoffice E2E CI failure is primarily an infrastructure/bootstrap problem, not a meaningful product signal.

- Workflow run 302 failed all 111 Backoffice E2E tests during startup because `BackofficeIdentity.Api` tried to connect to Postgres at `127.0.0.1:5433`
- The E2E fixture is intended to start from a TestContainers connection string:
  - `tests/Backoffice/Backoffice.E2ETests/E2ETestFixture.cs:83-113`
- Because bootstrap failed before meaningful browser interaction, the current Backoffice E2E suite cannot yet be used to judge feature correctness

### 2. Backoffice has real drift behind the infrastructure noise

Once the bootstrap issue is fixed, expected product/test mismatches remain:

- Route drift in Backoffice E2E pages vs current UI routes
- Selector drift in older Backoffice page objects
- Query contract drift between Backoffice.Web and Backoffice.Api
- Return status vocabulary drift between Backoffice and Returns BC
- Partial Backoffice operator flows that stop at list/search views

This means M34.0 must sequence Backoffice stabilization as:

1. bootstrap truthfulness
2. route/selector drift cleanup
3. contract/vocabulary cleanup
4. only then use the suite as a release gate

### 3. Vendor Portal Issue #460 is a real product bug

The root cause in `src/Vendor Portal/VendorPortal.Web/Pages/Dashboard.razor:111-126` matches the GitHub issue exactly: both buttons are gated by `AuthState.CanSubmitChangeRequests`.

That means:

- ReadOnly users cannot submit change requests — correct
- ReadOnly users also cannot view change requests — incorrect

**Decision:** use **Option A** from the issue draft.

Rationale:

- This is a localized UI gating bug, not evidence that the product needs a new permission model
- The existing experience already assumes read-only viewing without edit powers
- Adding `CanViewChangeRequests` now would expand the RBAC surface area without a demonstrated business need

### 4. M34.0 should remain engineering-led

M34.0 is not the time to overload scope with new ambition. The most valuable work is the work that restores trustworthy verification, closes the high-value RBAC bug, and finishes already-supported experiences that users still cannot reliably reach.

---

## Phase 2 panel outcomes

### Where findings compound

The riskiest M34.0 items are the ones where multiple problems stack together:

1. **Backoffice E2E**
   - infrastructure failure hides behavioral failures
   - stale selectors/routes are likely present behind the bootstrap issue
   - passing tests would still be suspect until false positives are audited

2. **Backoffice vocabulary**
   - implementation drift exists
   - tests likely encode stale expectations
   - operator-facing labels can become misleading if not aligned to domain truth

3. **Vendor Portal change requests**
   - implementation bug is understood
   - tests are already pointing at the right business outcome
   - the fix must be sequenced after stabilization work begins, but early enough to unblock the Vendor Portal suite

### What is explicitly not the first act of M34.0

The first act of M34.0 is **not** adding new Backoffice features, expanding permissions, or writing more E2E tests on top of an untrusted base.

### Mandatory first act of M34.0

**Restore trustworthy Backoffice E2E signal.**

Before any feature work begins, the team must make the Backoffice E2E suite capable of reaching behavioral failures instead of dying during bootstrap.

---

## Phase 3 — Sequenced M34.0 plan

## 1. Stabilization track

This track comes first and should be completable within the first one to two sessions.

### S1. Fix Backoffice E2E bootstrap/configuration

**Goal:** eliminate the `127.0.0.1:5433` startup failure and ensure Backoffice E2E uses the TestContainers database consistently.

**Acceptance criteria**

- Backoffice E2E no longer fails during test bootstrap because `BackofficeIdentity.Api` tries to connect to `127.0.0.1:5433`
- CI logs show the suite reaching real scenario execution
- The suite produces a meaningful pass/fail list rather than 111 identical startup failures

### S2. Establish the Backoffice test baseline

**Goal:** create a trustworthy inventory of what actually passes, fails, skips, or is missing after bootstrap is fixed.

**Acceptance criteria**

- Every Backoffice test project is accounted for:
  - `Backoffice.E2ETests`
  - `Backoffice.Web.UnitTests`
  - `Backoffice.Api.IntegrationTests`
  - `Backoffice.UnitTests`
- Every failing Backoffice E2E scenario is categorized as one of:
  - test is wrong
  - implementation is wrong
  - business logic is wrong
  - infrastructure/environment

### S3. Fix stale Backoffice E2E routes/selectors and obvious false positives

**Goal:** align tests to current user-facing routes and stable selectors before changing behavior.

**Acceptance criteria**

- Stale route assumptions are removed
- Brittle selectors are replaced with resilient, behavior-oriented selectors
- Tests no longer assert implementation detail when a user-visible outcome is available

### S4. Normalize Backoffice contract and vocabulary drift

**Goal:** align Backoffice.Web, Backoffice.Api, and adjacent BC vocabulary before treating E2E as authoritative.

**Focus areas**

- route/query contract drift
- return status vocabulary drift
- misleading KPI or operator-facing labels

**Acceptance criteria**

- Backoffice UI labels and API semantics match the underlying business meaning
- Tests assert the canonical vocabulary, not stale historical names

### S5. Define the trustworthy suite gate

**Goal:** finish stabilization with a clear rule for M34 feature work.

**Acceptance criteria**

- Backoffice E2E is green or reduced to a short, explicitly-triaged list with named owners
- No known false-positive E2E tests remain in the release gate
- M34 feature work may begin only after this gate is met

---

## 2. Bug fix track — Issue #460

### B1. Vendor Portal Dashboard RBAC — ReadOnly users can view, not submit

**Issue:** #460  
**Priority:** high-value, high-urgency  
**Decision:** **Option A**

**Implementation notes**

- Move `View Change Requests` outside the `CanSubmitChangeRequests` gate
- Keep `Submit Change Request` gated by `CanSubmitChangeRequests`
- Do not introduce a new `CanViewChangeRequests` permission in M34.0

**Why Option A won**

- Fixes the actual bug with the smallest correct change
- Matches the current product expectation for read-only access
- Avoids widening the RBAC model while stabilization work is still in progress

**Tests that must pass to close the issue**

- Vendor Portal E2E scenarios covering ReadOnly, CatalogManager, and Admin dashboard behavior
- Change request list navigation for ReadOnly users
- Existing submit flow scenarios for submit-capable users

**Close criteria**

- ReadOnly users see `View Change Requests`
- ReadOnly users do not see `Submit Change Request`
- Existing submit-capable roles keep current behavior

---

## 3. Feature track

Feature work begins only after the stabilization track is complete.

### F1. Experience completion work already supported by the architecture

This track should favor finishing incomplete or inaccessible experiences over introducing new ones.

**Candidate items**

- Backoffice experience completion items that remain blocked by route/detail/view drift
- Vendor Portal workflow polish that is already backed by existing APIs and projections
- Vocabulary-alignment work where event or UI naming still causes confusion

### F2. Vocabulary alignment

This remains part of M34.0, but only after the test feedback loop is trustworthy.

**Dependencies**

- Stabilization track complete
- Any renamed terms must be reflected consistently in:
  - UI labels
  - projections/read models
  - endpoint/query names
  - tests

---

## 4. Deferred items

- **Option B for Issue #460 (`CanViewChangeRequests`)** — deferred because M34.0 needs a localized bug fix, not an RBAC model expansion
- **New feature work that adds fresh E2E surface area before stabilization is complete** — deferred to prevent repeating M33.0’s broken feedback loop
- **Any Backoffice scope that depends on unresolved detail-view or broader search design decisions** — deferred until stabilization reveals what is truly broken versus merely incomplete
- **Broad product expansion unrelated to existing inaccessible experiences** — deferred because M34.0 remains engineering-led and stabilization-led

---

## 5. Guard rails for M34.0 implementation agents

These are non-negotiable.

1. **Do not write or modify feature code until the relevant test signal is trustworthy.**
2. **Do not treat a green E2E test as automatically correct; audit for false positives and stale expectations.**
3. **Prefer resilient selectors and user-visible outcomes over brittle DOM-shape assertions or aggressive error-UI checks.**
4. **When a test and implementation disagree, classify the disagreement explicitly: test bug, implementation bug, business-rule bug, or infrastructure bug.**
5. **Do not widen RBAC, workflow, or vocabulary scope when a localized fix is sufficient.**

---

## Definition of M34.0 readiness

M34.0 is ready to begin when:

- this plan is committed
- `CURRENT-CYCLE.md` references the finalized plan
- the first implementation session starts with stabilization, not feature expansion

At that point, the cycle can begin with a measured first move instead of another round of guessing.
