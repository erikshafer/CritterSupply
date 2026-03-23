# M33.0 Session 8 Plan: XC-1 ADR + CheckoutCompleted Fix (Phase 1 Completion)

**Session:** 8
**Date:** 2026-03-23
**Status:** 📋 Planning Complete — Ready for Implementation
**Prerequisites:** Session 7 complete (Priority 3 recovery fully delivered)

---

## Critical Discovery: Priority 2 Already Complete

**Session 2 completed Priority 2** (three Marten projections). See `docs/planning/milestones/m33-0-session-2-retrospective.md` for confirmation.

All three projections exist and are tested:
- ✅ `ReturnMetricsView` + `ReturnMetricsViewProjection` (registered, used in dashboard)
- ✅ `CorrespondenceMetricsView` + `CorrespondenceMetricsViewProjection` (registered)
- ✅ `FulfillmentPipelineView` + `FulfillmentPipelineViewProjection` (registered)
- ✅ 14 projection integration tests passing (`EventDrivenProjectionTests`)
- ✅ All 91 Backoffice.Api.IntegrationTests passing

**Session 8 actual scope:** Complete **Phase 1** of the M33-M34 Engineering Proposal by delivering:
- **Priority 7:** XC-1 ADR (canonical validator placement convention)
- **Priority 8:** CheckoutCompleted dual-payload collision fix

---

## Session Goal

Complete **Phase 1** of the M33-M34 Engineering Proposal (Sessions 1-2 scope). Phase 1 must ship completely before any downstream validator normalization work (R-6, VP-6) can begin.

**Why these two items together:**
- XC-1 ADR establishes the canonical validator placement pattern for the entire codebase
- CheckoutCompleted fix is the highest severity/effort ratio bug in the audit (S effort, 🔴 live risk)
- Both are small, independent, high-impact corrections that enable downstream work

**Exit Criteria:**
1. ADR 003x published documenting canonical validator placement (top-level `AbstractValidator<T>`, same file as command + handler)
2. Shopping's `CheckoutCompleted` renamed to `CartCheckoutCompleted`
3. Orders' internal `CheckoutCompleted` renamed to `OrderCreated`
4. All consumers of both events updated
5. Zero build errors
6. All tests passing

---

## Scope: Phase 1 Completion (XC-1 ADR + CheckoutCompleted Fix)

### MUST (Exit Criteria for Phase 1)

#### 1. ✅ **XC-1 ADR: Canonical Validator Placement**

**From M33+M34 Engineering Proposal:**
> "Write canonical ADR for validator placement (top-level `AbstractValidator<T>`, same file as command + handler; not nested, not extracted to separate file, not bulk file). Policy must exist before any validator normalization work begins."

**Purpose:** Establish the single correct pattern for validator placement across all BCs. This ADR will be referenced by all future validator normalization work (R-6, VP-6, etc.).

**Pattern to Document:**

**✅ Correct Pattern (CritterSupply Standard):**
```csharp
// src/Shopping/Shopping/Cart/AddItemToCart.cs
namespace Shopping.Cart;

public sealed record AddItemToCart(Guid CartId, string Sku, int Quantity);

public sealed class AddItemToCartValidator : AbstractValidator<AddItemToCart>
{
    public AddItemToCartValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Sku).NotEmpty();
    }
}

public static class AddItemToCartHandler
{
    public static ItemAdded Handle(AddItemToCart cmd, ShoppingCart cart)
    {
        // Pure function - returns event
        return new ItemAdded(cart.Id, cmd.Sku, cmd.Quantity);
    }
}
```

**❌ Anti-Patterns:**
- Nested validator class inside handler class
- Validator in separate file (`AddItemToCartValidator.cs`)
- Bulk validator file (`CartValidators.cs`)
- Validators folder (`Validators/AddItemToCartValidator.cs`)

**ADR Key Sections:**
- **Context:** Validator placement inconsistency across BCs (audit findings INV-1, R-3, VP-6)
- **Decision:** Top-level `AbstractValidator<T>`, same file as command + handler
- **Rationale:** Colocation for discoverability, namespace-based organization, one-file-per-slice
- **Consequences:** All future validator work follows this pattern; existing violations flagged for refactor
- **Alternatives Considered:** Validators folder (rejected: navigation friction), nested class (rejected: discovery issues)

**Implementation:**
- [ ] Create `docs/decisions/003X-canonical-validator-placement.md` (determine next ADR number)
- [ ] Document pattern with code examples
- [ ] Reference existing good examples: Shopping BC, Orders BC (post-Cycle 8 refactor)
- [ ] Document anti-patterns with specific violations from audit
- [ ] Commit ADR as standalone commit

---

#### 2. ✅ **CheckoutCompleted Dual-Payload Collision Fix**

**From M33+M34 Engineering Proposal:**
> "Rename `CheckoutCompleted` in `Messages.Contracts/Shopping` to `CartCheckoutCompleted`; rename Orders-internal `CheckoutCompleted` to `OrderCreated`; update all consumers. ≤1 hour per rename; highest severity/effort ratio in the entire audit."

**Problem:** Two `CheckoutCompleted` records exist with incompatible payloads:
1. **Shopping BC** (`Messages.Contracts/Shopping/CheckoutCompleted.cs`): Integration message published when customer completes checkout
2. **Orders BC** (internal event, not in `Messages.Contracts`): Domain event when order is created

**Live Risk:** A consumer binding the wrong record silently drops fields or throws a deserialization exception at runtime during checkout — the highest-value user action in the system.

**Fix Cost:** S (two renames, update consumers)

**Implementation Steps:**

**Step 1: Rename Shopping's CheckoutCompleted → CartCheckoutCompleted**

- [ ] Find `src/Shared/Messages.Contracts/Shopping/CheckoutCompleted.cs`
- [ ] Rename record: `CheckoutCompleted` → `CartCheckoutCompleted`
- [ ] Update all publishers:
  - Search for `new CheckoutCompleted(` in Shopping BC
  - Update to `new CartCheckoutCompleted(`
- [ ] Update all subscribers:
  - Search for handlers consuming `CheckoutCompleted` from Shopping namespace
  - Update to consume `CartCheckoutCompleted`

**Step 2: Rename Orders' Internal CheckoutCompleted → OrderCreated**

- [ ] Find Orders BC internal `CheckoutCompleted` event (likely in `Orders/Orders/Order/` folder)
- [ ] Rename record: `CheckoutCompleted` → `OrderCreated`
- [ ] Update all Apply() methods in Order aggregate
- [ ] Update all handlers consuming this internal event
- [ ] Update any projections consuming this event

**Step 3: Verify No Namespace Collisions**

- [ ] Search entire codebase for `CheckoutCompleted` — should find zero matches
- [ ] Verify Shopping consumers use `Messages.Contracts.Shopping.CartCheckoutCompleted`
- [ ] Verify Orders consumers use `Orders.Order.OrderCreated`

---

## Implementation Checklist

### Phase 1: XC-1 ADR

- [ ] Determine next ADR number (check `docs/decisions/` for latest)
- [ ] Create ADR file: `docs/decisions/003X-canonical-validator-placement.md`
- [ ] Write ADR sections:
  - [ ] Status: ✅ Accepted
  - [ ] Date: 2026-03-23
  - [ ] Context (validator placement inconsistency, audit findings)
  - [ ] Decision (pattern definition with code example)
  - [ ] Rationale (colocation, discoverability, vertical slice principle)
  - [ ] Consequences (all future work follows pattern, refactor flagged)
  - [ ] Alternatives Considered (Validators folder, nested class, bulk file)
  - [ ] References (audit findings INV-1, R-3, VP-6; skill file vertical-slice-organization.md)
- [ ] Commit: `git add docs/decisions/003X-*.md && git commit -m "Add ADR 003X: Canonical validator placement convention (XC-1)"`

### Phase 2: CheckoutCompleted Fix

- [ ] **Step 1:** Search Shopping BC for `CheckoutCompleted` publishers and consumers
  ```bash
  rg "CheckoutCompleted" src/Shopping/ src/Shared/Messages.Contracts/Shopping/
  ```
- [ ] Rename `Messages.Contracts/Shopping/CheckoutCompleted.cs` → `CartCheckoutCompleted`
- [ ] Update Shopping BC publishers (replace `new CheckoutCompleted(` → `new CartCheckoutCompleted(`)
- [ ] Update Shopping BC tests
- [ ] **Step 2:** Search Orders BC for internal `CheckoutCompleted` event
  ```bash
  rg "CheckoutCompleted" src/Orders/
  ```
- [ ] Rename Orders internal event → `OrderCreated`
- [ ] Update Order aggregate Apply() methods
- [ ] Update Orders BC handlers and projections
- [ ] Update Orders BC tests
- [ ] **Step 3:** Verify no remaining `CheckoutCompleted` references
  ```bash
  rg "CheckoutCompleted" src/ tests/
  ```
- [ ] Commit: `git add . && git commit -m "Fix CheckoutCompleted dual-payload collision: Shopping → CartCheckoutCompleted, Orders → OrderCreated (Top 10 #4)"`

### Phase 3: Build & Test Verification

- [ ] Run `dotnet build` (expect 0 errors)
- [ ] Run `dotnet test` (all tests passing)
- [ ] Search for `CheckoutCompleted` one final time (should find zero matches)
- [ ] Verify no new warnings introduced

### Phase 4: Documentation

- [ ] Write session retrospective: `m33-0-session-8-retrospective.md`
  - Document ADR creation process
  - Document CheckoutCompleted fix (search results, refactor steps)
  - Document any discoveries or issues
  - Document lessons learned
- [ ] Update CURRENT-CYCLE.md:
  - Mark Priority 2 as complete (Session 2)
  - Mark Priority 7 as complete (Session 8)
  - Mark Priority 8 as complete (Session 8)
  - Update "Recent Completions" section
- [ ] Commit: `git add docs/planning/ && git commit -m "M33.0 Session 8: Complete Phase 1 (XC-1 ADR + CheckoutCompleted fix)"`

---

## Expected Outcomes

### Must Have (Session Success Criteria)

1. ✅ ADR 003x published and committed
2. ✅ Shopping's `CheckoutCompleted` renamed to `CartCheckoutCompleted`
3. ✅ Orders' internal `CheckoutCompleted` renamed to `OrderCreated`
4. ✅ All consumers updated (zero `CheckoutCompleted` references remain)
5. ✅ Build succeeds (0 errors)
6. ✅ All tests pass (no regressions)
7. ✅ Session retrospective written
8. ✅ CURRENT-CYCLE.md updated

### Nice to Have (Defer if Time Runs Out)

- Update CONTEXTS.md if any BC ownership/communication changed (unlikely for these two items)
- Create GitHub Issues for R-6 (Returns validators) and VP-6 (Vendor Portal validators) referencing the new ADR

---

## Exit Criteria

Session 8 is complete when **all** of these are true:

1. ✅ Build succeeds: `dotnet build` (0 errors)
2. ✅ All tests pass: `dotnet test` (no new failures)
3. ✅ ADR 003x committed to `docs/decisions/`
4. ✅ No `CheckoutCompleted` references remain in codebase (search returns zero)
5. ✅ Session retrospective written: `m33-0-session-8-retrospective.md`
6. ✅ CURRENT-CYCLE.md updated to reflect Phase 1 completion

---

## References

- [M33+M34 Engineering Proposal](./m33-m34-engineering-proposal-2026-03-21.md) — Phase 1 scope (XC-1, CheckoutCompleted fix)
- [Post-Audit Top 10](../../audits/POST-AUDIT-DISCUSSION-2026-03-21.md) — CheckoutCompleted collision (#4)
- [Codebase Audit 2026-03-21](../../audits/CODEBASE-AUDIT-2026-03-21.md) — Validator placement findings (INV-1, R-3, VP-6)
- [Vertical Slice Organization Skill](../../skills/vertical-slice-organization.md) — Validator placement patterns
- [Session 2 Retrospective](./m33-0-session-2-retrospective.md) — Priority 2 (projections) completion
- [Session 7 Retrospective](./m33-0-session-7-retrospective.md) — Priority 3 recovery

---

## Notes for Implementation

### Validator Pattern (for ADR Reference)

From `docs/skills/vertical-slice-organization.md`:

**✅ Good Example (Shopping BC):**
```
src/Shopping/Shopping/Cart/
├── AddItemToCart.cs         # Command + Validator + Handler
├── RemoveItemFromCart.cs    # Command + Validator + Handler
└── ClearCart.cs             # Command + Validator + Handler
```

**❌ Anti-Pattern (Returns BC — to be fixed in Phase 3):**
```
src/Returns/Returns/Returns/
├── ReturnCommands.cs        # 11 commands in one file
├── ReturnCommandHandlers.cs # 5 handlers in one file
└── ReturnValidators.cs      # 7 validators in one file
```

### CheckoutCompleted Search Commands

```bash
# Find all CheckoutCompleted references
rg "CheckoutCompleted" src/ tests/

# Find Shopping BC publishers
rg "new CheckoutCompleted" src/Shopping/

# Find Orders BC internal event
rg "CheckoutCompleted" src/Orders/Orders/

# Verify fix (should return zero matches)
rg "CheckoutCompleted" src/ tests/
```

### Expected File Locations

**ADR:**
- `docs/decisions/003X-canonical-validator-placement.md` (where X = next available number)

**CheckoutCompleted Rename Targets:**
- `src/Shared/Messages.Contracts/Shopping/CheckoutCompleted.cs` → `CartCheckoutCompleted.cs`
- `src/Orders/Orders/Order/CheckoutCompleted.cs` (or similar) → `OrderCreated.cs`

---

**Session Start:** 2026-03-23 (now)
**Estimated Duration:** 2-3 hours (small, focused scope)
**Priority:** HIGH (completes Phase 1, unblocks all validator normalization work)
