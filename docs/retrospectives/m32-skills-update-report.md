# M32.0 Skills Update Report

## Summary

Audited 5 M32.0 retrospective documents and identified 3 critical inaccuracies in skill files. Updated 3 skill documents (`event-sourcing-projections.md`, `marten-event-sourcing.md`, `critterstack-testing-patterns.md`) to correct misleading guidance discovered during M32.0 OrderNote aggregate implementation and testing patterns.

---

## Findings Catalog

### Finding: Apply() Methods Can Be Instance Methods (Not Just Static)
- **Source**: `m32.0-session5-retrospective.md` (Investigation Findings section)
- **Incorrect assumption**: Documentation stated "Apply() methods **MUST** be static for inline projections (thread safety)"
- **Correct behavior**: Both instance and static Apply() methods work correctly. ALL 9 production aggregates (Cart, Checkout, Return, Promotion, Coupon, ProductInventory, Payment, Shipment, OrderNote) use instance methods successfully.
- **Likely affected skill files**: `docs/skills/event-sourcing-projections.md` (line 149), related guidance in `marten-event-sourcing.md`

### Finding: IStartStream Return Pattern is CRITICAL (Not Optional)
- **Source**: `m32.0-session5-retrospective.md` (Core Problem Solved → Issue #1)
- **Incorrect assumption or bad pattern**: Direct `session.Events.StartStream()` usage assumed to persist automatically
- **Correct behavior**: Handlers creating new streams MUST return `IStartStream` from `MartenOps.StartStream()`. Direct session usage does NOT enroll in Wolverine's transactional middleware. Tuple return order matters: `(CreationResponse, IStartStream)` — response first, stream second.
- **Likely affected skill files**: `docs/skills/event-sourcing-projections.md`, `docs/skills/marten-event-sourcing.md`, `docs/skills/wolverine-message-handlers.md`

### Finding: AutoApplyTransactions() Policy is REQUIRED (Not Optional)
- **Source**: `m32.0-session5-retrospective.md` (Core Problem Solved → Issue #2)
- **Incorrect assumption or bad pattern**: Missing `opts.Policies.AutoApplyTransactions()` in Wolverine configuration
- **Correct behavior**: `AutoApplyTransactions()` is REQUIRED for Marten integration, not optional. Without this policy, Wolverine does NOT wrap handlers in transactional middleware and Marten changes are not automatically committed. This policy is present in ALL working BCs.
- **Likely affected skill files**: `docs/skills/wolverine-message-handlers.md`, `docs/skills/marten-event-sourcing.md`

### Finding: Test Fixture Authentication Must Use Stable User IDs
- **Source**: `m32.0-session5-retrospective.md` (Remaining Test Failures section), `m32.0-session-5-test-coverage-report.md` (Issues Fixed → Authorization Test Fixture Issue)
- **Incorrect assumption or bad pattern**: TestAuthHandler generating new random Guid for `sub` claim on every HTTP request
- **Correct behavior**: Multi-request scenarios (create → edit/delete) require stable user IDs across requests. Solution: Create `TestAdminUserId` constant and inject via `ITestAuthContext` interface for stable identity throughout test execution.
- **Likely affected skill files**: `docs/skills/critterstack-testing-patterns.md`

---

## Changes Made

### docs/skills/event-sourcing-projections.md
- **Line 149**: Removed misleading statement "Apply() methods **must** be `static` for inline projections (Marten's thread-safety requirement)"
- **Replacement**: Added comprehensive section explaining both instance and static Apply() methods are valid, with instance methods being the CritterSupply convention (consistent across all 9 production aggregates)
- **New content**: Added real codebase examples showing Cart, Checkout, and Return all use instance methods successfully
- **Referenced finding**: "Apply() Methods Can Be Instance Methods"

### docs/skills/marten-event-sourcing.md
- **Lines 106-119**: Enhanced section on IStartStream pattern with prominent warning block
- **New content**: Added explicit anti-pattern example showing direct `session.Events.StartStream()` usage fails to persist
- **New content**: Added correct pattern example with `MartenOps.StartStream()` returning `IStartStream`
- **New content**: Emphasized tuple return order requirement: `(CreationResponse, IStartStream)` — response must come first
- **Lines 1365-1403**: Added new section in Marten Configuration describing `AutoApplyTransactions()` as REQUIRED (not optional)
- **Referenced findings**: "IStartStream Return Pattern is CRITICAL", "AutoApplyTransactions() Policy is REQUIRED"

### docs/skills/critterstack-testing-patterns.md
- **Lines 50-90**: Enhanced TestFixture pattern with stable authentication guidance
- **New content**: Added section "Test Authentication with Stable User IDs" explaining the multi-request problem
- **New content**: Provided pattern example using `ITestAuthContext` interface with `TestAdminUserId` constant
- **New content**: Added anti-pattern example showing random Guid generation causing authorization failures
- **Referenced finding**: "Test Fixture Authentication Must Use Stable User IDs"

---

## Skill Files Reviewed, No Changes Needed

### docs/skills/wolverine-message-handlers.md
- **Reason**: Already correctly documents handler return patterns including `IStartStream`. Reviewed sections on aggregate loading and return types — all accurate. While it doesn't emphasize IStartStream as CRITICAL, the existing examples are correct.

### docs/skills/wolverine-sagas.md
- **Reason**: Saga patterns not directly affected by M32 findings. Document focuses on saga lifecycle, message correlation, and MarkCompleted() patterns. No inaccuracies found.

### docs/skills/integration-messaging.md
- **Reason**: Covers RabbitMQ message contracts and queue wiring. M32 focused on Marten projections and testing, not integration messaging. No issues found.

### docs/skills/testcontainers-integration-tests.md
- **Reason**: Covers container lifecycle and PostgreSQL setup. M32 test authentication issue is specific to Alba test fixtures with JWT, not container setup. No changes required.

### docs/skills/external-service-integration.md
- **Reason**: Strategy pattern for external services not used in M32.0 (no external service integration in Backoffice BFF). Not relevant to findings.

### docs/skills/bff-realtime-patterns.md
- **Reason**: SignalR and BFF composition patterns. M32 didn't cover real-time messaging (deferred to Session 8). Not relevant to findings.

### docs/skills/wolverine-signalr.md
- **Reason**: SignalR transport patterns not implemented in M32. Marker interface created but hub wiring deferred. Not relevant to findings.

### docs/skills/vertical-slice-organization.md
- **Reason**: File organization conventions. M32 followed existing patterns correctly. No issues found.

### docs/skills/modern-csharp-coding-standards.md
- **Reason**: Language features and style guide. M32 retrospectives don't identify any C# convention violations. No changes required.

### docs/skills/adding-new-bounded-context.md
- **Reason**: BC creation checklist. M32 created Backoffice BC following existing patterns. No issues found in the skill guidance.

### docs/skills/efcore-marten-projections.md
- **Reason**: EF Core projection patterns not used in M32 (Backoffice used native Marten projections). Not relevant to findings.

### docs/skills/efcore-wolverine-integration.md
- **Reason**: EF Core integration patterns not used in M32 (Backoffice is Marten-based). Not relevant to findings.

### docs/skills/blazor-wasm-jwt.md
- **Reason**: Blazor WASM patterns for Vendor Portal. M32 focused on Backoffice API (no web UI in Phase 1). Not relevant.

### docs/skills/bunit-component-testing.md
- **Reason**: Blazor component unit testing. M32 had no UI components (API-only implementation). Not relevant.

### docs/skills/e2e-playwright-testing.md
- **Reason**: Browser E2E testing. M32 was API-only (no web UI). Not relevant to findings.

### docs/skills/reqnroll-bdd-testing.md
- **Reason**: BDD testing with Gherkin. M32 used Alba integration tests (no BDD scenarios written). Not relevant.

### docs/skills/event-modeling-workshop.md
- **Reason**: Event modeling facilitation. M32 didn't include event modeling workshops. Not relevant.

### docs/skills/marten-document-store.md
- **Reason**: Document store patterns (non-event-sourced). M32 used event sourcing for OrderNote. Not relevant to findings.

---

## Commit Message

```
docs: update skill files based on M32.0 retrospective findings

Fixes #3 critical documentation inaccuracies discovered during M32.0:
- Apply() methods can be instance (not just static) for inline projections
- IStartStream return pattern is REQUIRED for stream creation handlers
- AutoApplyTransactions() policy is REQUIRED for Marten integration
- Test fixtures need stable authentication for multi-request scenarios

Updated:
- docs/skills/event-sourcing-projections.md
- docs/skills/marten-event-sourcing.md
- docs/skills/critterstack-testing-patterns.md

Co-authored-by: Claude Sonnet 4.5 <noreply@anthropic.com>
```

---

**Audit completed:** 2026-03-16
**Retrospectives reviewed:** 5 (M32.0 Sessions 1-5)
**Skill files updated:** 3
**Skill files reviewed (no changes):** 19
