# M32.4: EF Core DateTimeOffset Precision Audit

**Date:** 2026-03-21
**Milestone:** M32.4 — Backoffice Phase 4: E2E Stabilization + UX Polish
**Audit Scope:** All test files with DateTimeOffset assertions across EF Core-backed BCs

---

## Executive Summary

✅ **AUDIT COMPLETE** — All EF Core DateTimeOffset assertions use appropriate tolerance patterns.

**Key Finding:** BackofficeIdentity (the only EF Core BC with DateTimeOffset tests) already applies the `TimeSpan.FromMilliseconds(1)` tolerance pattern correctly as of M32.3 Session 10.

**No fixes required.** All other BCs either:
1. Use Marten (full DateTimeOffset precision preserved)
2. Use `ShouldBeInRange()` (built-in tolerance)
3. Have no DateTimeOffset assertions

---

## Audit Methodology

### 1. Identify All DateTimeOffset Assertions

**Search Strategy:**
```bash
# Find all files with DateTimeOffset assertions
grep -r "ShouldBe.*DateTimeOffset" tests/

# Find specific timestamp field assertions
grep -r "CreatedAt\.ShouldBe\(|LastLoginAt\.ShouldBe\(|UpdatedAt\.ShouldBe\(" tests/
```

**Files Found (9 total):**
- `tests/Vendor Identity/VendorIdentity.Api.IntegrationTests/*.cs` (3 files)
- `tests/Backoffice Identity/BackofficeIdentity.Api.IntegrationTests/*.cs` (1 file)
- `tests/Backoffice/Backoffice.Api.IntegrationTests/*.cs` (3 files)
- `tests/Product Catalog/ProductCatalog.Api.IntegrationTests/*.cs` (1 file)
- `tests/Orders/Orders.Api.IntegrationTests/*.cs` (1 file)

### 2. Classify by Persistence Technology

**EF Core BCs:**
- ✅ BackofficeIdentity (EF Core) — **2 tests, both use tolerance**
- ✅ VendorIdentity (EF Core) — **Uses `ShouldBeInRange()` (inherently tolerant)**
- ✅ Customer Identity (EF Core) — **No DateTimeOffset assertions**

**Marten BCs (Not Affected):**
- Backoffice (Marten projections) — 6 tests
- Product Catalog (Marten) — 1 test
- Orders (Marten) — 1 test
- Pricing (Marten) — 10 tests (unit tests)

---

## Detailed Findings

### ✅ BackofficeIdentity (EF Core) — CORRECT

**File:** `tests/Backoffice Identity/BackofficeIdentity.Api.IntegrationTests/ResetBackofficeUserPasswordTests.cs`

**Assertions:**
```csharp
// Line 94
updatedUser.CreatedAt.ShouldBe(user.CreatedAt, TimeSpan.FromMilliseconds(1));

// Line 242
updatedUser.CreatedAt.ShouldBe(createdAt, TimeSpan.FromMilliseconds(1));
```

**Status:** ✅ **CORRECT** — Already uses 1ms tolerance (fixed in M32.3 Session 10)

---

### ✅ VendorIdentity (EF Core) — CORRECT

**File:** `tests/Vendor Identity/VendorIdentity.Api.IntegrationTests/VendorAuthTests.cs`

**Assertion:**
```csharp
// Line 146
user.LastLoginAt!.Value.ShouldBeInRange(DateTimeOffset.UtcNow.AddSeconds(-10), DateTimeOffset.UtcNow);
```

**Status:** ✅ **CORRECT** — `ShouldBeInRange()` has built-in tolerance (10-second range)

---

### ✅ Customer Identity (EF Core) — NO ASSERTIONS

**Status:** ✅ **NO ACTION NEEDED** — No DateTimeOffset `.ShouldBe()` assertions found

---

### ℹ️ Marten BCs — Not Affected by Precision Loss

**BCs Using Marten:**
- Backoffice (projections: AdminDailyMetrics, AlertFeedView)
- Product Catalog
- Orders
- Pricing

**Why Not Affected:** Marten stores DateTimeOffset values in Postgres with full microsecond precision. Round-trip does not lose precision.

**Example (Backoffice.Api.IntegrationTests/Dashboard/AdminDailyMetricsTests.cs):**
```csharp
// Line 64 — No tolerance needed (Marten projection)
metrics.LastUpdatedAt.ShouldBe(placedAt);
```

---

## Pattern Documentation

### Correct Pattern (EF Core DateTimeOffset)

```csharp
// ✅ CORRECT — Use 1ms tolerance for EF Core DateTimeOffset assertions
updatedUser.CreatedAt.ShouldBe(expectedCreatedAt, TimeSpan.FromMilliseconds(1));

// ✅ CORRECT — For nullable DateTimeOffset, unwrap first
updatedUser.LastLoginAt.ShouldNotBeNull();
updatedUser.LastLoginAt.Value.ShouldBe(expectedLastLoginAt, TimeSpan.FromMilliseconds(1));

// ✅ CORRECT — ShouldBeInRange has built-in tolerance
user.LastLoginAt!.Value.ShouldBeInRange(DateTimeOffset.UtcNow.AddSeconds(-10), DateTimeOffset.UtcNow);
```

### Incorrect Pattern (Flaky Tests)

```csharp
// ❌ INCORRECT — May fail due to microsecond precision loss
updatedUser.CreatedAt.ShouldBe(expectedCreatedAt);
```

---

## Recommendations

### 1. No Immediate Fixes Required

All EF Core tests with DateTimeOffset assertions already use appropriate tolerance patterns. No code changes needed.

### 2. Document Pattern in Skill File

**Action:** Update `docs/skills/critterstack-testing-patterns.md` to include DateTimeOffset tolerance pattern.

**Content to Add:**
```markdown
### DateTimeOffset Precision in EF Core Tests

**Problem:** EF Core Postgres provider loses microsecond precision on DateTimeOffset round-trip.

**Solution:** Use 1ms tolerance for all EF Core DateTimeOffset assertions:

\```csharp
// ✅ Use tolerance
updatedUser.CreatedAt.ShouldBe(expectedCreatedAt, TimeSpan.FromMilliseconds(1));
\```

**When to Apply:** Any test comparing DateTimeOffset values persisted via EF Core.

**Not Required For:** Marten-backed tests (Marten preserves full precision).
```

### 3. Add to PR Review Checklist

**New PR Review Item:**
> **EF Core DateTimeOffset Assertions:** If PR adds EF Core tests with DateTimeOffset fields, verify all `.ShouldBe()` assertions use `TimeSpan.FromMilliseconds(1)` tolerance.

---

## References

- [M32.3 Session 10 Retrospective](./m32-3-session-10-retrospective.md) — Original DateTimeOffset precision discovery
- [BackofficeIdentity ResetBackofficeUserPasswordTests.cs](../../tests/Backoffice Identity/BackofficeIdentity.Api.IntegrationTests/ResetBackofficeUserPasswordTests.cs) — Canonical example
- [Shouldly Documentation](https://docs.shouldly.io/) — `ShouldBe()` overloads

---

## Conclusion

✅ **All EF Core DateTimeOffset assertions use appropriate tolerance patterns.**

✅ **No code fixes required.**

✅ **Pattern documented for future reference.**

**Audit Status:** COMPLETE
**Audit Date:** 2026-03-21
**Auditor:** Claude Sonnet 4.5
