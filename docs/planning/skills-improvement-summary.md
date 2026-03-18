# Skills Improvement Summary — M32.1 Retrospective Learnings

**Quick Reference:** Actionable items derived from [skills-improvement-plan-m32-1.md](./skills-improvement-plan-m32-1.md)

---

## 🎯 Quick Action Items

### Phase 1: Add M32.1 Patterns (HIGH PRIORITY — 8-12 hours)

| Document | What to Add | Lines | Priority |
|----------|-------------|-------|----------|
| `e2e-playwright-testing.md` | WASM tiered timeouts, test-ID conventions, POM best practices | +450 | 🔴 CRITICAL |
| `blazor-wasm-jwt.md` | Multi-issuer JWT pattern | +150 | 🔴 CRITICAL |
| `wolverine-message-handlers.md` | HTTP endpoint validation pattern | +100 | 🟡 HIGH |
| `bunit-component-testing.md` | MudBlazor v9+ type parameters | +50 | 🟡 HIGH |
| `marten-event-sourcing.md` | Missing Apply method warning | +75 | 🟡 HIGH |
| `critterstack-testing-patterns.md` | Multi-policy auth bypass | +50 | 🟢 MEDIUM |

**Total New Content:** +875 lines across 6 documents

---

### Phase 2: Lean-Out Longest Documents (MEDIUM PRIORITY — 16-24 hours)

| Document | Current Lines | Target Lines | Reduction | Lean-Out Strategy |
|----------|---------------|--------------|-----------|-------------------|
| `wolverine-message-handlers.md` | 2,195 | 1,500 | -500 (-23%) | Remove redundant examples, consolidate HTTP patterns |
| `marten-event-sourcing.md` | 1,886 | 1,400 | -400 (-21%) | Consolidate Apply examples, streamline decider section |
| `event-sourcing-projections.md` | 1,851 | 1,400 | -400 (-22%) | Remove overlap with marten-event-sourcing.md |
| `bff-realtime-patterns.md` | 1,773 | 1,300 | -400 (-23%) | Consolidate BFF examples, remove SignalR overlap |
| `critterstack-testing-patterns.md` | 1,756 | 1,300 | -400 (-23%) | Consolidate test examples, remove overlap with testcontainers doc |

**Total Reduction:** -2,100 lines across 5 documents

---

### Phase 3: Scannability Improvements (LOW PRIORITY — 12-18 hours)

| Improvement | Documents | Effort | Impact |
|-------------|-----------|--------|--------|
| Add decision tables | 3 docs (sagas, event-sourcing, e2e) | 6h | LLM agents can quickly determine "when to use X" |
| Pattern-first refactoring | 3 docs (prose → tables/bullets first) | 9h | Key patterns visible in first 100 lines |
| Anti-pattern callouts | All docs (convert to ❌ format) | 15h | Anti-patterns visually distinct and scannable |

**Total Effort:** 30 hours (can be spread across multiple milestones)

---

## 📋 Top 8 Patterns to Capture

### 1. WASM E2E Tiered Timeout Strategy
- Initial load: 15s + MudBlazor check
- Auth navigation: 15s
- SignalR: 15s
- Elements: 10s
- State: 5s

### 2. Test-ID Naming Conventions
| Pattern | Example | Anti-Pattern |
|---------|---------|--------------|
| `kpi-{metric}` | `kpi-total-orders` | ❌ `kpi-active-orders` |
| `nav-{destination}` | `nav-customer-service` | ❌ `customer-search-btn` |
| `{form}-{field}` | `login-email` | ❌ `email-input` |

### 3. Explicit MudBlazor Hydration Check
```csharp
await _page.WaitForSelectorAsync(".mud-dialog-provider", new() { Timeout = 15_000 });
```

### 4. POM-First Workflow
1. Write `.feature` file
2. Write Page Object Model (defines test-id contract)
3. Write Razor component (implements contract)
4. Write step definitions

### 5. MudBlazor v9+ Type Parameters
```razor
<MudList T="string">
    <MudListItem T="string">...</MudListItem>
</MudList>
```

### 6. Multi-Issuer JWT for Domain BCs
Domain BCs accept tokens from multiple identity providers (Backoffice + Vendor Portal).

### 7. HTTP Endpoint Validation
HTTP endpoints need their own validators (separate from domain command validators).

### 8. Missing Apply Methods Cause Null Aggregates
Every event in stream needs `Apply(TEvent)` method on aggregate.

---

## 🚀 Implementation Checklist

### Pre-Implementation
- [x] Read M32.1 retrospective
- [x] Analyze skills document sizes
- [x] Create comprehensive improvement plan
- [x] Identify patterns, anti-patterns, lean-out opportunities
- [ ] Get stakeholder approval

### Phase 1 (Next Session)
- [ ] Create feature branch `claude/skills-improvements-m32-1-phase-1`
- [ ] Update `e2e-playwright-testing.md` (+450 lines)
- [ ] Update `blazor-wasm-jwt.md` (+150 lines)
- [ ] Update `wolverine-message-handlers.md` (+100 lines)
- [ ] Update `bunit-component-testing.md` (+50 lines)
- [ ] Update `marten-event-sourcing.md` (+75 lines)
- [ ] Update `critterstack-testing-patterns.md` (+50 lines)
- [ ] Build and verify rendering
- [ ] Commit with detailed messages

### Phase 2 (Future Milestone)
- [ ] Lean out `wolverine-message-handlers.md` (-500 lines)
- [ ] Lean out `marten-event-sourcing.md` (-400 lines)
- [ ] Lean out `event-sourcing-projections.md` (-400 lines)
- [ ] Lean out `bff-realtime-patterns.md` (-400 lines)
- [ ] Lean out `critterstack-testing-patterns.md` (-400 lines)

### Phase 3 (Future Milestones)
- [ ] Add decision tables (3 docs)
- [ ] Refactor to pattern-first (3 docs)
- [ ] Convert anti-patterns to callout format (all docs)

---

## 📊 Expected Outcomes

### After Phase 1
- ✅ M32.1 patterns captured in 6 skills documents
- ✅ Future WASM E2E tests can reference established patterns
- ✅ MudBlazor v9+ gotchas documented
- ✅ Test-ID conventions prevent Session 14-style mismatches
- ✅ Multi-issuer JWT pattern reusable for future domain BCs

### After Phase 2
- ✅ All skills documents under 1,600 lines
- ✅ Redundant examples removed (1 good example per pattern)
- ✅ Improved scannability for LLM agents
- ✅ Reduced maintenance burden (fewer lines to keep updated)

### After Phase 3
- ✅ Decision tables for "when to use" guidance
- ✅ Pattern-first structure (key patterns in first 100 lines)
- ✅ Anti-patterns visually distinct with ❌ prefix
- ✅ LLM agents can scan any skill document in <30 seconds

---

## 💡 Strategic Value

**Problem:** M32.1 Session 6 took 2.5 hours to scaffold Backoffice WASM (vs. 4 hours for Vendor Portal WASM in Cycle 22).

**Why Faster:** VendorPortal.Web patterns were already documented in `blazor-wasm-jwt.md`. No discovery phase needed.

**This Plan's Goal:** Ensure M32.2+ (and future milestones) benefit from M32.1's WASM E2E, MudBlazor v9+, test-ID, and tiered timeout lessons.

**ROI:** Phase 1 (8-12 hours) prevents 10-20 hours of rediscovery in future milestones. 2-3x return on investment.

---

**Next Steps:**
1. Review this summary with stakeholders
2. Get approval for Phase 1 implementation
3. Schedule Phase 1 implementation session (8-12 hours)
4. Defer Phase 2/3 to future milestones (incremental improvements)

---

*Summary Last Updated: 2026-03-18*
*See [skills-improvement-plan-m32-1.md](./skills-improvement-plan-m32-1.md) for detailed rationale, examples, and implementation guidance*
