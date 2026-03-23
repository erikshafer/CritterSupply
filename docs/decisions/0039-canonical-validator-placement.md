# ADR 0039: Canonical Validator Placement Convention (XC-1)

**Status:** ✅ Accepted

**Date:** 2026-03-23

**Context:** M33.0 Phase 1 — Correctness + Regression Foundation

---

## Context

The 2026-03-21 codebase audit identified validator placement inconsistency across bounded contexts. Multiple patterns exist in production code, creating navigation friction and violating the vertical slice principle:

**Inconsistency Examples (from audit findings):**

1. **INV-1 (Inventory BC):** Four-file shatter — `AdjustInventory.cs` (command), `AdjustInventoryHandler.cs` (handler), `AdjustInventoryValidator.cs` (validator), `AdjustInventoryEndpoint.cs` (HTTP endpoint)

2. **R-3 (Returns BC):** Bulk validator file — `ReturnValidators.cs` containing 7 validators for 7 different commands, separated from command files

3. **VP-6 (Vendor Portal):** Missing validators entirely — 7 commands with no validation (`SubmitChangeRequest` accepts user payload with no guards)

4. **Shopping BC (good example):** Command + validator + handler colocated in single file (`AddItemToCart.cs`)

**Problem:** No documented canonical pattern exists. Developers have no guidance on where to place validators when adding new commands. Existing code shows 3+ different patterns, each with precedent.

**Impact:**
- Navigation friction (find command → search for validator in different location)
- Discoverability issues (forgotten validators in bulk files)
- Inconsistent enforcement of vertical slice architecture
- Downstream validator normalization work (R-6, VP-6, INV-1) blocked until pattern is established

---

## Decision

**All validators MUST be placed as top-level classes in the same file as their command and handler.**

**Pattern:**

```csharp
// src/<BoundedContext>/<BoundedContext>/<Feature>/<CommandName>.cs

namespace BoundedContext.Feature;

// 1. Command record (sealed)
public sealed record CommandName(Guid Id, string Sku, int Quantity);

// 2. Validator class (sealed, top-level, inherits AbstractValidator<T>)
public sealed class CommandNameValidator : AbstractValidator<CommandName>
{
    public CommandNameValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be positive");
        RuleFor(x => x.Sku).NotEmpty().WithMessage("SKU is required");
    }
}

// 3. Handler class (static)
public static class CommandNameHandler
{
    public static EventName Handle(CommandName cmd, Aggregate aggregate)
    {
        // Pure function - returns event
        return new EventName(aggregate.Id, cmd.Sku, cmd.Quantity);
    }
}
```

**Key Requirements:**
- Validator MUST be a **top-level class** (not nested inside handler class)
- Validator MUST be **sealed**
- Validator MUST be in the **same file** as command and handler
- Validator MUST inherit **AbstractValidator&lt;TCommand&gt;** from FluentValidation
- Validator class name MUST be `{CommandName}Validator`

**File naming:** `{CommandName}.cs` (e.g., `AddItemToCart.cs`, `PlaceOrder.cs`)

**Folder structure:** Feature-oriented, not technical layers (`Shopping/Cart/`, not `Commands/` + `Validators/` + `Handlers/`)

---

## Rationale

### 1. Colocation for Discoverability

When a developer opens `AddItemToCart.cs`, they see:
- What the command accepts (command record)
- What validation rules apply (validator class)
- What the business logic does (handler)

**Navigation path:** One file, zero jumps. Compare to anti-patterns:
- **Bulk validator file:** Open `AddItemToCart.cs` → search for `AddItemToCartValidator.cs` (doesn't exist) → search for `CartValidators.cs` → find validator at line 47 of 200
- **Separate validator file:** Open `AddItemToCart.cs` → no validator visible → search `Validators/` folder → open `AddItemToCartValidator.cs`

### 2. Vertical Slice Architecture Compliance

CritterSupply follows vertical slice organization: **each slice file contains everything for one user-facing operation.**

From `docs/skills/vertical-slice-organization.md`:
> "A slice is a complete vertical cut through the layers of the system, containing the command/query, validation, business logic, and any side effects needed to complete a single user action."

Separating validators into bulk files or separate folders violates this principle by creating horizontal technical layers.

### 3. Namespace-Based Organization

File structure mirrors namespace structure. Command lives in `Shopping.Cart` namespace → command file lives in `src/Shopping/Shopping/Cart/` folder.

No `Validators/` subfolder needed — the namespace already provides organizational structure.

### 4. FluentValidation Discovery Works Automatically

Wolverine discovers validators via assembly scanning. As long as the validator:
1. Inherits `AbstractValidator<T>`
2. Lives in an assembly included in Wolverine's `opts.Discovery.IncludeAssembly()`

...it will be discovered and applied automatically. Placement in separate files or folders provides **zero discovery benefit.**

### 5. Consistency with Established Good Examples

Shopping BC (post-Cycle 8 refactor) and Orders BC (post-Cycle 8 refactor) already follow this pattern:
- `src/Shopping/Shopping/Cart/AddItemToCart.cs` — command + validator + handler
- `src/Orders/Orders/Order/PlaceOrder.cs` — command + validator + handler

This ADR codifies the pattern already in use by our most recently refactored BCs.

---

## Consequences

### Positive

1. **Single source of truth** — All future validator work has a canonical reference
2. **Refactor roadmap** — Existing violations (INV-1, R-3, VP-6) now have clear target state
3. **Onboarding clarity** — New developers see consistent pattern across all BCs
4. **Navigation speed** — Zero-friction access to validation rules when reading commands
5. **Vertical slice enforcement** — Pattern structurally enforces slice architecture

### Negative (Acceptable Trade-offs)

1. **File length** — Files with complex validators may exceed 100 lines (acceptable — colocation benefit outweighs length concern)
2. **Refactor cost** — Existing bulk validator files must be dissolved (cost: S per BC, already planned in Phase 2/3)
3. **Line-of-business validator extraction** — Cannot extract shared validation logic into separate file (mitigation: use nested validator composition via FluentValidation's `SetValidator()` method)

### Migration Path for Existing Code

**Phase 2 (Quick Wins):**
- INV-1: Consolidate `AdjustInventory*` four-file shatter → single file
- PR-1: Merge Pricing three-way split → single file

**Phase 3 (Returns BC Refactor):**
- R-3: Dissolve `ReturnValidators.cs` bulk file → colocate validators with commands

**Phase 4 (Vendor Portal Refactor):**
- VP-6: Add missing validators (colocated with commands per this ADR)

**All downstream work must reference this ADR as the canonical pattern.**

---

## Alternatives Considered

### Alternative 1: Validators in Separate Files (Rejected)

**Pattern:** Each validator in its own file (`AddItemToCartValidator.cs`)

**Why rejected:**
- Doubles file count (command file + validator file + handler file)
- Navigation friction (must open two files to understand one operation)
- Zero technical benefit — FluentValidation discovery works regardless of file placement
- Violates vertical slice principle (creates horizontal technical layers)

### Alternative 2: Nested Validator Class Inside Handler (Rejected)

**Pattern:**
```csharp
public static class AddItemToCartHandler
{
    public sealed class Validator : AbstractValidator<AddItemToCart> { }
    public static ItemAdded Handle(AddItemToCart cmd) { }
}
```

**Why rejected:**
- Discovery issues — Wolverine expects top-level classes inheriting `AbstractValidator<T>`
- Naming friction — must reference as `AddItemToCartHandler.Validator` (not `AddItemToCartValidator`)
- Semantic confusion — validator is not part of the handler; they are separate concerns that execute at different lifecycle stages (Before → Validate → Load → Handle)

### Alternative 3: Bulk Validator File (Rejected)

**Pattern:** All validators for a feature in one file (`CartValidators.cs` with 7 validators)

**Why rejected:**
- Navigation friction (find validator at line N of 200-line file)
- Merge conflict risk (all validator changes touch same file)
- Violates vertical slice principle (creates horizontal layer)
- Real-world evidence: `ReturnValidators.cs` (R-3 audit finding) shows this pattern breaks down at scale

### Alternative 4: Validators Folder (Rejected)

**Pattern:** Separate folder for all validators (`Shopping/Validators/AddItemToCartValidator.cs`)

**Why rejected:**
- Creates technical layer folder (violates vertical slice architecture)
- Navigation friction (jump from `Cart/` to `Validators/`)
- Namespace mismatch (`Shopping.Validators.AddItemToCartValidator` vs `Shopping.Cart.AddItemToCart`)
- Real-world evidence: Inventory BC INV-1 finding shows this pattern creates four-file shatters

---

## References

- **M33+M34 Engineering Proposal** (Phase 1): `docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md`
- **Codebase Audit 2026-03-21** (findings INV-1, R-3, VP-6): `docs/audits/CODEBASE-AUDIT-2026-03-21.md`
- **Vertical Slice Organization Skill:** `docs/skills/vertical-slice-organization.md`
- **Wolverine Message Handlers Skill:** `docs/skills/wolverine-message-handlers.md`
- **Post-Audit Top 10:** `docs/audits/POST-AUDIT-DISCUSSION-2026-03-21.md`

**Good Examples in Codebase:**
- `src/Shopping/Shopping/Cart/AddItemToCart.cs` (command + validator + handler)
- `src/Orders/Orders/Order/PlaceOrder.cs` (command + validator + handler)

**Anti-Pattern Examples (to be refactored):**
- `src/Returns/Returns/Returns/ReturnValidators.cs` (bulk file, R-3 finding)
- `src/Inventory/Inventory.Api/Commands/AdjustInventoryValidator.cs` (separate file, INV-1 finding)

---

## Implementation Checklist for Future Work

When adding a new command with validation:

1. Create `src/<BC>/<BC>/<Feature>/<CommandName>.cs`
2. Define command record (sealed)
3. Add validator class (sealed, top-level, `AbstractValidator<TCommand>`)
4. Add handler class (static)
5. Wolverine discovers validator automatically (no registration needed)

When refactoring existing validators to this pattern:

1. Move validator class from separate file → command file
2. Change nested validator → top-level class
3. Delete empty validator file / dissolve bulk validator file
4. Verify Wolverine discovery still works (`opts.Discovery.IncludeAssembly()` unchanged)
5. Run tests (validator discovery is automatic; tests should still pass)

---

**This ADR establishes the canonical pattern for all validator placement in CritterSupply. All future validator work MUST follow this pattern. All existing violations are flagged for refactor in Phase 2–4.**
