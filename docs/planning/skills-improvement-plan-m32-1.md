# Skills Document Improvement Plan — Based on M32.1 Retrospective

**Date:** 2026-03-18
**Source:** M32.1 Milestone Retrospective + Session Retrospectives (Sessions 1-16)
**Purpose:** Research, discovery, and planning for skills document refinement
**Status:** 📋 Planning Complete — Ready for Implementation

---

## Executive Summary

M32.1 delivered critical patterns and lessons for **Blazor WASM E2E testing**, **multi-issuer JWT authorization**, **MudBlazor v9+ gotchas**, **test-ID conventions**, and **tiered timeout strategies**. These learnings must be captured in skills documents to prevent future duplication of effort and accelerate similar implementations.

**Key Finding:** Several skills documents have grown to 1,500-2,200 lines. This plan identifies where to add new patterns, what to lean out, and how to improve scannability for LLM agents.

**Strategic Goal:** Refine skills documents to be **concise**, **scannable**, **actionable**, and **pattern-focused** — capturing the essence of what works (and what doesn't) without excessive prose.

---

## Critical Patterns to Capture from M32.1

### 1. WASM E2E Testing with Playwright (HIGH PRIORITY)

**Source:** M32.1 Lessons L1, L2, L4, L5; Wins W1, W2, W3

**Patterns to Add:**

#### Tiered Timeout Strategy (L1, W1)
- Initial page load: 15s + MudBlazor provider check
- Authenticated navigation: 15s (auth state propagation)
- Element visibility: 10s (standard)
- SignalR connection: 15s (JWT dependency)
- State checks: 5s (DOM polling)

**Anti-Pattern to Document:**
- ❌ Fixed 30s timeout everywhere (wasteful for fast operations)
- ❌ Using NetworkIdle alone without hydration check (flaky tests)

#### Explicit Hydration Detection (L2, W2)
```csharp
// Step 1: Wait for network idle
await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

// Step 2: Wait for MudBlazor initialization
await _page.WaitForSelectorAsync(".mud-dialog-provider", new() { Timeout = 15_000 });

// Step 3: Wait for specific element
await EmailInput.WaitForAsync(new() { Timeout = 15_000 });
```

**Why This Works:** MudBlazor v9+ creates `.mud-dialog-provider` during initialization. If provider exists, MudBlazor is ready.

#### Auth State Propagation in WASM (L4)
After login, Blazor WASM takes 1-3s to propagate auth state. Post-login navigation timeout: 15s (not 10s).

#### SignalR Dependency on JWT (L5)
SignalR connection requires JWT auth to complete first. Connection timeout must be >= auth propagation timeout (15s).

**Target Document:** `docs/skills/e2e-playwright-testing.md`
**Where to Add:** New section "WASM-Specific Patterns" after existing architecture section
**Estimated Size:** +200 lines (new content)

---

### 2. Test-ID Naming Conventions (HIGH PRIORITY)

**Source:** M32.1 Win W3, Session 14/15 discoveries

**Patterns to Add:**

| Element Type | Pattern | Examples | Anti-Patterns |
|--------------|---------|----------|---------------|
| KPI Cards | `kpi-{metric-name}` | `kpi-total-orders`, `kpi-revenue` | ❌ `kpi-active-orders` (ambiguous) |
| KPI Values (nested) | `kpi-value` | Always nested within KPI card | ❌ `kpi-total-orders-value` (redundant) |
| Navigation Links | `nav-{destination}` | `nav-customer-service`, `nav-operations` | ❌ `customer-search-btn` (component name) |
| Form Inputs | `{form}-{field}` | `login-email`, `login-password` | ❌ `email-input` (presentational) |
| Form Buttons | `{form}-{action}` | `login-submit`, `logout-button` | ❌ `submit-btn` (generic) |
| Real-time Indicators | `realtime-{state}` | `realtime-connected`, `realtime-disconnected` | ❌ `hub-status` (implementation detail) |

**Key Principle:** Test-IDs should describe **what** the element represents (semantic), not **how** it looks (presentational) or **how** it works (implementation).

**Target Document:** `docs/skills/e2e-playwright-testing.md`
**Where to Add:** New section "Test-ID Naming Conventions" after Page Object Model section
**Estimated Size:** +150 lines (table + examples + anti-patterns)

---

### 3. Page Object Models Should Be Written Before Components (MEDIUM PRIORITY)

**Source:** M32.1 Lesson L3, Session 14 discoveries

**Current Anti-Pattern (Session 14):**
1. Write Dashboard.razor with arbitrary test-ids
2. Write DashboardPage.cs expecting different test-ids
3. Tests fail due to test-id mismatches (not functional bugs)

**Better Approach:**
1. Write Gherkin `.feature` file (user stories)
2. Write Page Object Model with expected test-ids (defines contract)
3. Write Razor component implementing those test-ids (fulfills contract)
4. Write step definitions using Page Object Model

**Why:** POM defines the contract. Components fulfill the contract. If component is written first, POM must adapt to component's arbitrary test-ids.

**Target Document:** `docs/skills/e2e-playwright-testing.md`
**Where to Add:** New section "Page Object Model Best Practices" in POM chapter
**Estimated Size:** +100 lines (workflow + rationale + examples)

---

### 4. MudBlazor v9+ Gotchas (HIGH PRIORITY)

**Source:** M32.1 Session 6 Discovery D1

**Pattern:** MudBlazor v9+ requires explicit type parameters even for non-data-bound lists:

```razor
<!-- ❌ WRONG (v8 syntax) -->
<MudList>
    <MudListItem Icon="...">Text</MudListItem>
</MudList>

<!-- ✅ RIGHT (v9+ syntax) -->
<MudList T="string">
    <MudListItem T="string" Icon="...">Text</MudListItem>
</MudList>
```

**Why:** MudBlazor v9+ is generic-first. Type inference fails for non-data-bound lists.

**Impact:** All components using MudList must specify `T="string"` (or appropriate type).

**Target Documents:**
1. `docs/skills/bunit-component-testing.md` (MudBlazor setup section)
2. `docs/skills/e2e-playwright-testing.md` (WASM patterns section)

**Where to Add:** New subsection "MudBlazor v9+ Type Parameters" in existing MudBlazor sections
**Estimated Size:** +50 lines per document (100 lines total)

---

### 5. Multi-Issuer JWT Authorization (MEDIUM PRIORITY)

**Source:** M32.1 Sessions 1-3 (Product Catalog, Pricing, Inventory, Payments BCs)

**Pattern:** Domain BCs need multi-issuer JWT configuration to accept tokens from Backoffice (port 5249) and other issuers:

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer("Backoffice", opts =>
    {
        opts.Authority = builder.Configuration["BackofficeIdentity:Authority"];
        opts.Audience = "backoffice-api";
    })
    .AddJwtBearer("Vendor", opts =>
    {
        opts.Authority = builder.Configuration["VendorIdentity:Authority"];
        opts.Audience = "vendor-api";
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("PricingManager", policy =>
        policy.RequireAuthenticatedUser()
              .RequireRole("pricing-manager"));
});
```

**Key Lesson:** Multi-issuer JWT allows domain BCs to accept tokens from multiple identity providers (Backoffice, Vendor Portal) without code duplication.

**Target Document:** `docs/skills/blazor-wasm-jwt.md`
**Where to Add:** New section "Multi-Issuer JWT for Domain BCs" after existing JWT auth section
**Estimated Size:** +150 lines (pattern + examples + configuration)

---

### 6. HTTP Endpoint Validation Separate from Domain Command Validation (MEDIUM PRIORITY)

**Source:** M32.1 Session 5 Warning W2, Lesson L2

**Pattern:** HTTP endpoints need their own FluentValidation validators separate from domain command validators:

```csharp
// HTTP layer DTO + validator
public sealed record SetBasePriceRequest(string Sku, decimal BasePrice);

public sealed class SetBasePriceValidator : AbstractValidator<SetBasePriceRequest>
{
    public SetBasePriceValidator()
    {
        RuleFor(x => x.Sku).NotEmpty();
        RuleFor(x => x.BasePrice).GreaterThan(0);
    }
}

// Endpoint (bypasses domain command handler)
[WolverinePost("/api/pricing/products/{sku}/base-price")]
public static BasePriceSet Handle(SetBasePriceRequest request)
{
    // Direct event construction (no domain command)
    return new BasePriceSet(request.Sku, request.BasePrice, DateTimeOffset.UtcNow);
}
```

**Why:** HTTP layer may bypass domain command handlers (direct event construction). Validation at HTTP boundary is required.

**Anti-Pattern:**
- ❌ Relying only on domain command validators when HTTP endpoints bypass commands

**Target Document:** `docs/skills/wolverine-message-handlers.md`
**Where to Add:** New subsection "HTTP Endpoint Validation" in HTTP endpoints chapter
**Estimated Size:** +100 lines (pattern + rationale + anti-pattern)

---

### 7. Authorization Bypass Pattern for Integration Tests (LOW PRIORITY)

**Source:** M32.1 Session 4-5 Warnings W2, W1

**Pattern:** Integration test fixtures bypass JWT authorization via:

```csharp
services.AddAuthorization(opts =>
{
    opts.AddPolicy("PricingManager", policy =>
        policy.RequireAssertion(_ => true)); // Always succeeds
});
```

**When Multiple Policies Exist:**
```csharp
services.AddAuthorization(opts =>
{
    opts.AddPolicy("CustomerService", policy => policy.RequireAssertion(_ => true));
    opts.AddPolicy("FinanceClerk", policy => policy.RequireAssertion(_ => true));
});
```

**Key Lesson:** Multi-policy BCs need all policies added with `RequireAssertion(_ => true)`.

**Target Document:** `docs/skills/critterstack-testing-patterns.md`
**Where to Add:** Existing "Authorization Bypass" section (update with multi-policy example)
**Estimated Size:** +50 lines (multi-policy example)

---

### 8. Marten Event-Sourced Aggregates Require Apply Method for EVERY Event (LOW PRIORITY)

**Source:** M32.1 Session 4 debugging, repository memory

**Pattern:** Marten event-sourced aggregates MUST have an `Apply(TEvent)` method for every event type in their stream:

```csharp
public sealed record ProductPrice
{
    // Aggregate state
    public string Sku { get; init; } = default!;
    public decimal CurrentPrice { get; init; }

    // ✅ REQUIRED: Apply method for ProductRegistered event
    public ProductPrice Apply(ProductRegistered e) => this with
    {
        Sku = e.Sku,
        CurrentPrice = 0m
    };

    // ✅ REQUIRED: Apply method for BasePriceSet event
    public ProductPrice Apply(BasePriceSet e) => this with
    {
        CurrentPrice = e.BasePrice
    };
}
```

**Anti-Pattern:**
- ❌ Missing `Apply` method causes `AggregateStreamAsync` to return null even when events exist

**Target Document:** `docs/skills/marten-event-sourcing.md`
**Where to Add:** Existing "Apply Methods" section (add warning about missing Apply methods)
**Estimated Size:** +75 lines (anti-pattern + debugging example)

---

## Documents Needing Major Lean-Out

### 1. `wolverine-message-handlers.md` (2,195 lines → Target: 1,500 lines)

**Current Issues:**
- Very long (2,195 lines)
- Multiple examples per pattern (some redundant)
- Verbose prose in some sections

**Lean-Out Strategy:**
1. **Remove redundant examples:** Keep 1 good example per pattern (remove 2-3 variations)
2. **Consolidate HTTP endpoint patterns:** Merge similar patterns into single examples
3. **Reduce prose:** Convert paragraph explanations to bullet points where possible
4. **Move advanced patterns to separate sections:** Keep core patterns visible, advanced patterns at end

**Target Reduction:** -500 lines (23% reduction)

**Priority:** MEDIUM (after adding M32.1 patterns)

---

### 2. `marten-event-sourcing.md` (1,886 lines → Target: 1,400 lines)

**Current Issues:**
- Very long (1,886 lines)
- Some examples span 50-100 lines
- Repetitive explanations of Apply pattern

**Lean-Out Strategy:**
1. **Consolidate Apply examples:** Show 1-2 Apply patterns, reference for others
2. **Remove verbose aggregate examples:** Keep concise examples, remove 100+ line aggregates
3. **Streamline decider pattern section:** Focus on when/why, not exhaustive examples
4. **Merge similar projection examples:** Keep 1 inline, 1 async example (remove others)

**Target Reduction:** -400 lines (21% reduction)

**Priority:** MEDIUM

---

### 3. `event-sourcing-projections.md` (1,851 lines → Target: 1,400 lines)

**Current Issues:**
- Very long (1,851 lines)
- Overlaps with marten-event-sourcing.md
- Multiple projection examples with similar patterns

**Lean-Out Strategy:**
1. **Remove overlap with marten-event-sourcing.md:** Reference that doc instead of repeating
2. **Consolidate projection examples:** Keep 1 snapshot, 1 multi-stream, 1 live example
3. **Streamline FetchForWriting section:** Concise pattern + 1 example
4. **Move Polecat compatibility notes to appendix:** Keep main content focused on Postgres

**Target Reduction:** -400 lines (22% reduction)

**Priority:** MEDIUM

---

### 4. `bff-realtime-patterns.md` (1,773 lines → Target: 1,300 lines)

**Current Issues:**
- Very long (1,773 lines)
- Overlaps with wolverine-signalr.md
- Multiple BFF examples (Storefront, Vendor Portal, Backoffice) showing similar patterns

**Lean-Out Strategy:**
1. **Consolidate BFF examples:** Keep 1 canonical example (Storefront), reference others
2. **Remove overlap with wolverine-signalr.md:** Focus on BFF composition, defer SignalR details
3. **Streamline real-time update patterns:** Keep 1-2 examples, remove variations
4. **Move anti-patterns to end:** Keep best practices visible, anti-patterns in appendix

**Target Reduction:** -400 lines (23% reduction)

**Priority:** LOW (newer document, less critical)

---

### 5. `critterstack-testing-patterns.md` (1,756 lines → Target: 1,300 lines)

**Current Issues:**
- Very long (1,756 lines)
- Multiple test examples with similar structure
- Some sections repeat patterns from testcontainers-integration-tests.md

**Lean-Out Strategy:**
1. **Consolidate test examples:** Keep 1 Alba test, 1 pure function test, 1 BDD test
2. **Remove overlap with testcontainers-integration-tests.md:** Reference that doc for fixture details
3. **Streamline authorization bypass section:** Concise pattern + 1 example (add multi-policy note from M32.1)
4. **Move advanced patterns to end:** Keep common patterns visible

**Target Reduction:** -400 lines (23% reduction)

**Priority:** MEDIUM

---

## Documents That Are Right-Sized (No Major Changes)

### 1. `blazor-wasm-jwt.md` (627 lines) ✅
- Well-focused on WASM JWT patterns
- Concise examples
- **Action:** Add multi-issuer JWT section from M32.1 (+150 lines → 777 lines, still reasonable)

### 2. `modern-csharp-coding-standards.md` (539 lines) ✅
- Concise style guide
- Clear examples
- **Action:** No changes needed

### 3. `bunit-component-testing.md` (405 lines) ✅
- Focused on bUnit patterns
- **Action:** Add MudBlazor v9+ type parameter note (+50 lines → 455 lines, still reasonable)

### 4. `external-service-integration.md` (260 lines) ✅
- Concise strategy pattern guide
- **Action:** No changes needed

### 5. `event-modeling-workshop.md` (346 lines) ✅
- Workshop facilitation guide
- **Action:** No changes needed

---

## Scannability Improvements for LLM Agents

### Pattern: Lead with Tables and Bullet Points

**Current Issue:** Some skills lead with long prose paragraphs before showing patterns.

**Better Approach:** Lead with scannable patterns (tables, code blocks, bullets), then provide prose context.

**Example Refactor:**

**BEFORE (prose-first):**
```
When building Wolverine sagas, you need to understand the lifecycle...
[200 words of explanation]
...here's how to do it:
[code example]
```

**AFTER (pattern-first):**
```
## Saga Lifecycle Pattern

| Step | Method | Returns | Purpose |
|------|--------|---------|---------|
| 1 | Handle(Command) | (Aggregate, Event) | Initialize saga |
| 2 | Handle(Event) | OutgoingMessages | React to events |
| 3 | MarkCompleted() | void | End saga |

[Code example showing all 3 steps]

### Why This Pattern
- Bullet point explanation
- Key principle
- Common pitfall
```

**Documents to Refactor:**
1. `wolverine-sagas.md` — Lifecycle section
2. `marten-event-sourcing.md` — Apply pattern section
3. `integration-messaging.md` — Message flow section

**Estimated Effort:** 2-3 hours per document (6-9 hours total)

---

### Pattern: Use Decision Tables for "When to Use" Guidance

**Current Issue:** "When to use X vs Y" guidance is scattered in prose paragraphs.

**Better Approach:** Decision matrices showing clear criteria.

**Example:**

| Scenario | Use Saga | Use Aggregate | Use Handler |
|----------|----------|---------------|-------------|
| Multi-BC coordination | ✅ Yes | ❌ No | ❌ No |
| Single-BC workflow | ❌ No | ✅ Yes | ❌ No |
| Pure function logic | ❌ No | ❌ No | ✅ Yes |
| Compensation needed | ✅ Yes | ❌ No | ❌ No |
| Event sourcing needed | ❌ No | ✅ Yes | ❌ No |

**Documents to Add Decision Tables:**
1. `wolverine-sagas.md` — Saga vs Aggregate vs Handler
2. `marten-event-sourcing.md` — Event sourcing vs Document store
3. `e2e-playwright-testing.md` — E2E vs Alba vs bUnit

**Estimated Effort:** 1-2 hours per document (3-6 hours total)

---

### Pattern: Anti-Pattern Callouts with ❌ Prefix

**Current Issue:** Anti-patterns are mentioned in prose but not visually distinct.

**Better Approach:** Use ❌ prefix and code blocks to make anti-patterns scannable.

**Example:**

```markdown
### ❌ Anti-Pattern: Forgetting MarkCompleted() in Sagas

**Wrong:**
```csharp
public void Handle(OrderCancelled evt)
{
    Status = OrderStatus.Cancelled;
    // ❌ Saga never completes, stays in database forever
}
```

**Right:**
```csharp
public void Handle(OrderCancelled evt)
{
    Status = OrderStatus.Cancelled;
    MarkCompleted();  // ✅ Saga is removed from storage
}
```

**Why:** Orphaned sagas accumulate in the database and waste resources.
```

**Documents to Add Anti-Pattern Callouts:**
1. All skills documents (scan for existing anti-patterns, convert to callout format)

**Estimated Effort:** 1 hour per document (15-20 hours total)

---

## Implementation Priorities

### Phase 1: Add M32.1 Patterns (HIGH PRIORITY)
**Estimated Effort:** 8-12 hours

1. ✅ `e2e-playwright-testing.md` — Add WASM patterns, test-ID conventions, POM best practices (+450 lines)
2. ✅ `blazor-wasm-jwt.md` — Add multi-issuer JWT section (+150 lines)
3. ✅ `wolverine-message-handlers.md` — Add HTTP endpoint validation section (+100 lines)
4. ✅ `bunit-component-testing.md` — Add MudBlazor v9+ type parameter note (+50 lines)
5. ✅ `marten-event-sourcing.md` — Add missing Apply method warning (+75 lines)
6. ✅ `critterstack-testing-patterns.md` — Update authorization bypass with multi-policy example (+50 lines)

---

### Phase 2: Major Lean-Out (MEDIUM PRIORITY)
**Estimated Effort:** 16-24 hours

1. `wolverine-message-handlers.md` — Remove redundant examples, consolidate HTTP patterns (-500 lines)
2. `marten-event-sourcing.md` — Consolidate Apply examples, streamline decider section (-400 lines)
3. `event-sourcing-projections.md` — Remove overlap, consolidate projection examples (-400 lines)
4. `bff-realtime-patterns.md` — Consolidate BFF examples, remove SignalR overlap (-400 lines)
5. `critterstack-testing-patterns.md` — Consolidate test examples, remove overlap (-400 lines)

**Total Reduction:** -2,100 lines across 5 documents

---

### Phase 3: Scannability Refactoring (LOW PRIORITY)
**Estimated Effort:** 12-18 hours

1. Add decision tables to 3 documents (6 hours)
2. Refactor prose-first sections to pattern-first (9 hours)
3. Convert anti-patterns to callout format across all documents (15 hours)

**Note:** Phase 3 can be done incrementally over multiple milestones.

---

## Success Criteria

### Quantitative Metrics
- ✅ M32.1 patterns captured in 6 skills documents
- ✅ 2,100+ lines removed from 5 longest documents
- ✅ All documents under 1,600 lines (current max: 2,195 lines)
- ✅ 10+ decision tables added for "when to use" guidance
- ✅ 20+ anti-pattern callouts formatted with ❌ prefix

### Qualitative Metrics
- ✅ LLM agents can scan skills documents in <30 seconds
- ✅ Key patterns visible within first 100 lines of each document
- ✅ Anti-patterns clearly marked and visually distinct
- ✅ Examples are concise (20-50 lines, not 100+)
- ✅ Redundant examples removed (1 good example per pattern)

---

## Next Steps

### Immediate Actions (This Session)
1. ✅ Review this plan with stakeholders
2. ⬜ Get approval to proceed with Phase 1
3. ⬜ Identify any missing patterns from M32.1 retrospective

### Phase 1 Implementation (Next Session)
1. ⬜ Create feature branch `claude/skills-improvements-m32-1-phase-1`
2. ⬜ Implement 6 high-priority skill updates
3. ⬜ Build and verify documentation rendering
4. ⬜ Commit changes with detailed commit messages

### Phase 2 Implementation (Future Milestone)
1. ⬜ Plan lean-out session for each document
2. ⬜ Remove redundant examples incrementally
3. ⬜ Verify no information loss during reduction

---

## Conclusion

M32.1 delivered critical patterns for WASM E2E testing, multi-issuer JWT, MudBlazor v9+, test-ID conventions, and tiered timeouts. This plan ensures these lessons are captured in skills documents and that existing documents are leaned out to improve scannability for LLM agents.

**Strategic Value:** Well-refined skills documents accelerate future implementations. M32.1 Session 6 took 2.5 hours (vs. 4 hours for Vendor Portal WASM in Cycle 22) because patterns were already documented. This improvement plan ensures future milestones benefit from M32.1 lessons.

**Implementation Strategy:** Phase 1 (add M32.1 patterns) has highest ROI. Phase 2 (lean-out) and Phase 3 (scannability) can be done incrementally over multiple milestones.

---

**Plan Status:** 📋 Planning Complete — Ready for Phase 1 Implementation
**Next Milestone:** M32.2 or M33.1 (depending on priorities)
**Estimated Total Effort:** 36-54 hours (can be split across multiple sessions/milestones)

---

*Plan Last Updated: 2026-03-18*
*Format: Research/Discovery → Actionable Implementation Plan*
*Length: ~650 lines (detailed, scannable, ready to act on)*
