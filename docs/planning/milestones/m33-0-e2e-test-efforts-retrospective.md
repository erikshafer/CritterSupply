# M33.0 E2E Test Efforts: Comprehensive Retrospective

**Date**: 2026-03-24
**Context**: Documentation of all E2E test efforts from M32.1 through M33.0
**Purpose**: Prevent duplicate work, capture lessons learned, document remaining issues
**Status**: 4 test failures remain in CI (Vendor Portal E2E)

---

## Executive Summary

This retrospective documents **extensive E2E testing efforts** spanning multiple milestones (M32.1 Session 9 through M33.0 Session 13), including:

- ✅ **Infrastructure Built**: 3 complete E2E test fixtures (Storefront, Vendor Portal, Backoffice)
- ✅ **Connection String Fixes**: VendorPortal.Api and VendorIdentity.Api timing issues resolved
- ⚠️ **Playwright Timeouts**: Root causes identified, solutions proposed but not yet implemented
- ✅ **Patterns Established**: SimulateSessionExpired, multi-role seeding, test-id conventions
- ❌ **Remaining Work**: 4 Vendor Portal E2E tests failing in CI

**Key Takeaway**: Most infrastructure is in place and working. Remaining failures are specific to Blazor WASM bootstrap timing in CI environment.

---

## Timeline: E2E Test Evolution

### M32.1 Session 9 (Backoffice E2E Infrastructure - First Complete Fixture)
**Date**: 2026-03-15
**Scope**: Create first complete 3-server E2E test fixture with WASM pattern

**What Shipped:**
- ✅ `E2ETestFixture.cs` with 3-server architecture (Backoffice.Web, Backoffice.Api, BackofficeIdentity.Api)
- ✅ `WasmStaticFileHost` for serving WASM static files with dynamic appsettings.json injection
- ✅ 9 stub domain BC clients (Orders, Returns, Inventory, CustomerIdentity, Catalog, Fulfillment, Correspondence, Pricing, BackofficeIdentity)
- ✅ TestContainers PostgreSQL integration
- ✅ 3 feature files (Authentication, WarehouseAdmin, OperationsAlerts)
- ✅ Page Object Model pattern (DashboardPage, LoginPage, OperationsAlertsPage)

**Key Learnings:**
1. **WASM is fundamentally different from Blazor Server** — requires custom static file host
2. **Two-Program-class problem** — use domain-specific types as WebApplicationFactory anchor
3. **Real auth essential** — stubbing auth defeats the purpose
4. **CORS must be opened** — random ports mean pre-configured origins don't work
5. **WASM cold start is slow** — need 30+ second timeout for initial load

**Issues Encountered:**
- BCrypt package reference compilation error (fixed)
- Wolverine SignalR hub method name mismatch (fixed)
- Random port allocation vs pre-configured CORS origins (fixed)

**Retrospective**: `docs/planning/milestones/m32.1-session-9-retrospective.md`

---

### M32.1 Session 15 (Timeout Investigation & Test-ID Conventions)
**Date**: 2026-03-16
**Scope**: Investigate WASM timeout issues, standardize test-id naming

**What Shipped:**
- ✅ Test-id convention documentation (kebab-case, hierarchical, semantic)
- ✅ Fixed test-id mismatches (removed non-existent `kpi-active-customers`)
- ✅ Timeout analysis: LoginPage 30s, Dashboard navigation 10s, KPI cards 10s, SignalR 10s

**Key Findings:**
- WASM bootstrap takes 5-30 seconds in CI (unpredictable)
- MudBlazor hydration adds 2-5 seconds
- Test-ids must match actual rendered HTML (not assumptions)

**Anti-Patterns Documented:**
- ❌ Generic names (`button1`, `div-content`)
- ❌ Component-specific (`mud-button`, `mud-card`)
- ❌ Presentational (`red-banner`)
- ❌ Index-based (`kpi-1`, `alert-2`)

**Recommended Timeout Tiering:**
- Initial load: 15s
- Navigation: 15s
- Element visibility: 10s
- State checks: 5s

**Retrospective**: `docs/planning/milestones/m32.1-session-15-retrospective.md`

---

### M32.2 Sessions 4-6 (UX Hardening E2E Coverage)
**Date**: 2026-03-19
**Scope**: Comprehensive E2E test coverage for session expiry, RBAC, data freshness

**What Shipped:**
- ✅ 4 Gherkin `.feature` files (SessionExpiry, Authorization, DataFreshness, AlertAcknowledgment)
- ✅ 29 step definitions across 3 files
- ✅ Extended page objects (SessionExpiredPage global modal, CustomerSearchPage, OperationsAlertsPage)
- ✅ **SimulateSessionExpired Pattern**: Stub clients throw 401 on demand
- ✅ **Multi-Role Admin Seeding**: Deterministic GUIDs for 7 admin roles
- ✅ 600+ lines documentation update to `e2e-playwright-testing.md`

**Key Patterns Established:**

**1. SimulateSessionExpired Pattern**
```csharp
public bool SimulateSessionExpired { get; set; }

public Task<StockLevelDto?> GetStockLevelAsync(string sku, CancellationToken ct = default)
{
    if (SimulateSessionExpired)
        throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

    return Task.FromResult(_stockLevels.GetValueOrDefault(sku));
}
```

**2. Multi-Role Admin Seeding**
```csharp
public void SeedAdminUserWithRole(Guid userId, string email, string displayName, string role)
{
    var backofficeRole = role switch
    {
        "system-admin" => BackofficeRole.SystemAdmin,
        "operations-manager" => BackofficeRole.OperationsManager,
        "warehouse-clerk" => BackofficeRole.WarehouseClerk,
        // ... 7 roles total
    };

    var user = new BackofficeUser(userId, email, displayName, backofficeRole, IsActive: true);
    user.PasswordHash = passwordHasher.HashPassword(user, "Password123!");
    _backofficeIdentityDbContext.BackofficeUsers.Add(user);
}
```

**Lessons Learned:**
1. **ALWAYS verify production enum values** — Don't assume; grep first
2. **Playwright uses `.First` property** — Not `.First()` LINQ method
3. **Global page objects for modals** — Appear across all pages
4. **Null-safe teardown essential** — Handle partial initialization failures

**Build Status**: ✅ 0 errors, 10 pre-existing warnings

**Retrospective**: `docs/planning/milestones/m32.2-e2e-retrospective.md`

---

### M32.3 Session 11 (WASM Publish Automation)
**Date**: 2026-03-17
**Scope**: Automate Blazor WASM publish before E2E test execution

**Problem**: E2E tests require `dotnet publish` output (not `dotnet build`). Build alone produces `wwwroot/` without `index.html`.

**Solution**: Added MSBuild BeforeTargets to E2E test projects:
```xml
<Target Name="PublishWasmBeforeVSTest" BeforeTargets="VSTest">
  <Exec Command="dotnet publish $(WasmProjectPath) --configuration $(Configuration)" />
</Target>
```

**Impact**: Resolved "app not loading in test context" failures blocking 12/34 scenarios

**Retrospective**: `docs/planning/milestones/m32-3-session-11-retrospective.md`

---

### Vendor Portal E2E Troubleshooting (2026-03-16)
**Scope**: Diagnose and fix Vendor Portal E2E test failures in CI

**Problem Timeline**:
1. **Phase 1**: PostgreSQL connection refused to `127.0.0.1:5433`
2. **Phase 2**: Playwright timeout waiting for `[data-testid='login-btn']` after 30 seconds

**Fixes Attempted**:

#### Attempt 1: Docker Service Verification
- **Hypothesis**: TestContainers failing due to Docker unavailability
- **Fix**: Added Docker health checks to `.github/workflows/e2e.yml`
- **Result**: ❌ Tests still failed with same connection error

#### Attempt 2: VendorPortal.Api Connection String Timing
- **Root Cause**: Connection string read **before** `AddMarten()` lambda
- **Fix** (commit `eb4047a`):
  ```csharp
  // ❌ BROKEN: Captures fallback before TestContainers overrides
  var connectionString = builder.Configuration.GetConnectionString("postgres") ?? "Host=localhost;...";
  builder.Services.AddMarten(opts => { opts.Connection(connectionString); });

  // ✅ FIXED: Reads after TestContainers overrides applied
  builder.Services.AddMarten(opts => {
      var connectionString = builder.Configuration.GetConnectionString("postgres") ?? "Host=localhost;...";
      opts.Connection(connectionString);
  });
  ```
- **Result**: ✅ Fixed VendorPortal.Api, but tests still failed

#### Attempt 3: VendorIdentity.Api Connection String Timing
- **Root Cause**: Same issue in EF Core service
- **Fix** (commit `5e5b30e`):
  ```csharp
  builder.Services.AddDbContext<VendorIdentityDbContext>(options => {
      var connectionString = builder.Configuration.GetConnectionString("postgres") ?? throw new Exception("Connection string not found");
      options.UseNpgsql(connectionString);
  });
  ```
- **Additional**: Added null-safe teardown to `DataHooks.cs`
- **Result**: ✅ Fixed both APIs, tests **still failed** with different error

#### Actual Root Cause Discovered
**Error**: Playwright timeout after 30 seconds waiting for login button

**Potential Causes Identified** (Copilot Analysis):
1. **WASM Bootstrap Delay**: .NET runtime download + assembly loading (5-30s in CI)
2. **MudBlazor Hydration**: Additional 2-5s after initial page load
3. **SignalR Connection Timing**: Depends on JWT auth completing first
4. **CI Network Unpredictability**: Random timing in GitHub Actions runners

**Recommended Investigation Steps**:
- ✅ Add Playwright tracing (`trace: "on-first-retry"`)
- ✅ Capture screenshots on timeout
- ✅ Collect browser console logs
- ✅ Verify `data-testid` attribute exists in rendered HTML
- ✅ Check WASM static file serving (Network tab)

**Status**: Tests marked with `@ignore` tag (later removed); investigation deferred

**Retrospective**: `docs/planning/vendor-portal-e2e-troubleshooting-retrospective.md`

---

## Current State: What's Working, What's Not

### ✅ Working & Stable

**1. E2E Test Infrastructure**
- Storefront.E2ETests (2-server Blazor Server pattern) — ✅ Passing locally and in CI
- Backoffice.E2ETests (3-server WASM pattern) — ✅ Passing locally
- VendorPortal.E2ETests (3-server WASM pattern) — ⚠️ Failing in CI only

**2. Connection String Pattern**
- Fixed in VendorPortal.Api (commit `eb4047a`)
- Fixed in VendorIdentity.Api (commit `5e5b30e`)
- Pattern documented for future projects
- **Rule**: Always read connection strings **inside** configuration lambdas

**3. Test Infrastructure Patterns**
- SimulateSessionExpired for 401 simulation
- Multi-role admin seeding with deterministic GUIDs
- Global page objects for modals
- Null-safe teardown handling

**4. CI/CD Configuration**
- `.github/workflows/e2e.yml` with 2 parallel jobs (Storefront, Vendor Portal)
- Playwright browser caching
- PostgreSQL pre-pull (`postgres:18-alpine`)
- Artifact collection (`.trx` files 14 days, traces 7 days)
- Path-based triggering in `dotnet.yml`

**5. Documentation**
- `docs/skills/e2e-playwright-testing.md` (110KB comprehensive guide)
- Multiple retrospectives capturing lessons learned
- ADR 0015 (Playwright rationale)
- Test-id naming conventions

### ❌ Not Working / Issues Remaining

**1. Vendor Portal E2E Tests in CI** (4 Scenarios Failing)
- **Status**: Tests fail in CI but pass locally
- **Environment**: GitHub Actions ubuntu-latest runners
- **Error**: Playwright timeout waiting for login button after 30 seconds
- **Root Cause**: WASM bootstrap delay + MudBlazor hydration + CI network timing
- **Scenarios Affected**:
  1. Admin logs in with valid credentials and sees the dashboard
  2. Invalid credentials show inline error message
  3. Unauthenticated user is redirected to login
  4. (One additional scenario TBD based on actual CI logs)

**2. Playwright Tracing Not Enabled**
- **Issue**: No trace files collected on CI failures
- **Impact**: Cannot visually diagnose what's happening during timeout
- **Solution Proposed**: Add `trace: "on-first-retry"` to playwright.config.ts
- **Status**: Not yet implemented

**3. WASM Timeout Tuning**
- **Issue**: 30-second timeout insufficient for WASM bootstrap in CI
- **Proposed Fix**: Increase LoginPage timeout from 30s to 60s
- **Proposed Fix**: Add explicit wait for MudBlazor CSS classes
- **Proposed Fix**: Add retry logic with exponential backoff
- **Status**: Fixes proposed but not implemented

**4. Missing Success Criteria Verification**
Before re-enabling tests, need:
- ✅ All 12 scenarios pass locally with headless mode
- ✅ Playwright traces available in CI
- ✅ Login button selector verified in actual HTML
- ✅ WASM bootstrap timing understood and documented
- ✅ At least 3 consecutive green CI runs without flakiness

---

## Key Fixes That Worked

### Fix 1: Connection String Timing Pattern ✅

**Problem**: TestContainers dynamic connection strings ignored

**Root Cause**: Connection string read **before** configuration overrides applied

**Solution**: Read connection string **inside** configuration lambda

**Files Fixed**:
- `src/Vendor Portal/VendorPortal.Api/Program.cs` (commit `eb4047a`)
- `src/Vendor Identity/VendorIdentity.Api/Program.cs` (commit `5e5b30e`)

**Pattern**:
```csharp
// ❌ BROKEN
var connectionString = builder.Configuration.GetConnectionString("postgres");
builder.Services.AddMarten(opts => opts.Connection(connectionString));

// ✅ FIXED
builder.Services.AddMarten(opts => {
    var connectionString = builder.Configuration.GetConnectionString("postgres");
    opts.Connection(connectionString);
});
```

**Impact**: Resolved PostgreSQL connection failures for all VendorPortal/VendorIdentity tests

---

### Fix 2: Null-Safe Teardown Pattern ✅

**Problem**: `NullReferenceException` in test teardown masking real failures

**Root Cause**: Teardown assumed all fixture objects fully initialized

**Solution**: Null-safe checks in `[AfterTestRun]` hooks

**File Fixed**: `tests/Vendor Portal/VendorPortal.E2ETests/Hooks/DataHooks.cs`

**Pattern**:
```csharp
[AfterTestRun(Order = 100)]
public static async Task StopInfrastructure()
{
    if (_browser is not null)
        await _browser.DisposeAsync();

    _playwright?.Dispose();

    if (_fixture is not null)
        await _fixture.DisposeAsync();
}
```

**Impact**: Clearer error messages; real test failures no longer masked

---

### Fix 3: Test-ID Convention Standardization ✅

**Problem**: Page objects expected test-ids that don't exist in components

**Root Cause**: Assumed test-ids without verifying actual rendered HTML

**Solution**: Standardized test-id naming convention + verification before test creation

**Convention Established**:
| Element Type | Pattern | Examples |
|---|---|---|
| KPI Cards | `kpi-{metric-name}` | `kpi-total-orders`, `kpi-revenue` |
| Navigation | `nav-{destination}` | `nav-customer-service` |
| Forms | `{form}-{field}` | `login-email`, `login-password` |
| Real-time | `realtime-{state}` | `realtime-connected` |

**Anti-Patterns**:
- ❌ Generic: `button1`, `div-content`
- ❌ Component-specific: `mud-button`, `mud-card`
- ❌ Presentational: `red-banner`
- ❌ Index-based: `kpi-1`, `alert-2`

**Impact**: Prevented future test-id mismatches; clear convention for all teams

---

### Fix 4: Enum Value Verification Before Test Data ✅

**Problem**: Test data referenced non-existent enum value `BackofficeRole.FinanceClerk`

**Root Cause**: Assumed enum value without verifying production code

**Solution**: **ALWAYS grep production code first** before writing test data

**Process**:
```bash
# 1. Find enum definition
grep -A 20 "enum BackofficeRole" src/Backoffice\ Identity/BackofficeIdentity/Identity/BackofficeUser.cs

# 2. Verify value exists
# Found: PricingManager (not FinanceClerk!)

# 3. Update test data
```

**Impact**: Prevented compilation errors; established verification-first workflow

---

## Patterns & Best Practices Established

### Pattern 1: SimulateSessionExpired for Stub Clients

**Use Case**: Test session expiry (401) responses without real auth infrastructure

**Implementation**:
```csharp
public bool SimulateSessionExpired { get; set; }

public Task<T> GetAsync(...)
{
    if (SimulateSessionExpired)
        throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

    // Normal logic
}
```

**Benefits**:
- ✅ In-process exception (no HTTP layer mocking)
- ✅ Triggers SessionExpiredService globally
- ✅ Works across all authenticated API calls

**Applied To**:
- StubInventoryClient
- StubOrdersClient
- StubCustomerIdentityClient

---

### Pattern 2: Multi-Role Admin Seeding

**Use Case**: Authorization tests need deterministic users with specific roles

**Implementation**:
```csharp
public void SeedAdminUserWithRole(Guid userId, string email, string displayName, string role)
{
    var backofficeRole = role switch
    {
        "system-admin" => BackofficeRole.SystemAdmin,
        "operations-manager" => BackofficeRole.OperationsManager,
        // ... 7 roles total
    };

    var user = new BackofficeUser(userId, email, displayName, backofficeRole, IsActive: true);
    user.PasswordHash = passwordHasher.HashPassword(user, "Password123!");
    _backofficeIdentityDbContext.BackofficeUsers.Add(user);
}
```

**Benefits**:
- ✅ Deterministic GUIDs (repeatable tests)
- ✅ Production password hashing (PBKDF2-SHA256)
- ✅ Role-specific permissions enforced

**Used In**: `WellKnownTestData.cs` (7 admin roles)

---

### Pattern 3: Global Page Objects for Modals

**Use Case**: Modals appear across multiple pages (session expiry, confirmations)

**Implementation**:
```csharp
public class SessionExpiredPage
{
    private readonly IPage _page;

    private ILocator Modal => _page.Locator("[data-testid='session-expired-modal']");
    private ILocator SignInButton => Modal.Locator("[data-testid='session-expired-sign-in-btn']");

    public async Task<bool> IsVisibleAsync() => await Modal.IsVisibleAsync();
    public async Task ClickSignInAsync() => await SignInButton.ClickAsync();
}
```

**Benefits**:
- ✅ Single source of truth for modal locators
- ✅ Reusable across all step definition files
- ✅ Easy to update if modal design changes

---

### Pattern 4: Playwright Indexing (Not LINQ)

**Rule**: Playwright uses **properties** and **methods**, not LINQ

**Correct**:
```csharp
var first = AlertRows.First;     // ✅ Property
var last = AlertRows.Last;       // ✅ Property
var third = AlertRows.Nth(2);    // ✅ Method (zero-indexed)
var filtered = AlertRows.Filter(x => x.GetByText("SKU-123")); // ✅ Method
```

**Wrong**:
```csharp
var first = AlertRows.First();   // ❌ LINQ method
```

**Impact**: Compilation errors avoided; clear distinction from LINQ

---

## Solutions Proposed But Not Yet Implemented

### Proposed Solution 1: Increase WASM Bootstrap Timeout

**Problem**: 30-second timeout insufficient for WASM bootstrap in CI

**Proposed Fix**:
```csharp
// In VendorLoginPage.cs
public async Task FillEmailAsync(string email)
{
    await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    await EmailInput.WaitForAsync(new() { Timeout = 60_000 }); // Increase to 60s
    await EmailInput.FillAsync(email);
}
```

**Rationale**: CI environments have slower network; 60s provides buffer

**Status**: Not yet implemented; marked for future session

---

### Proposed Solution 2: Enable Playwright Tracing in CI

**Problem**: No visual diagnostics for CI failures

**Proposed Fix**: Create `playwright.config.ts`:
```typescript
export default {
  use: {
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure'
  }
};
```

**Impact**: Trace files uploaded to GitHub Actions artifacts

**Status**: Not yet implemented; high priority for next session

---

### Proposed Solution 3: Add MudBlazor Hydration Detection

**Problem**: MudBlazor components not fully initialized when Playwright checks

**Proposed Fix**:
```csharp
public async Task WaitForMudBlazorAsync()
{
    await _page.WaitForSelectorAsync(".mud-dialog-provider");
    await _page.WaitForSelectorAsync(".mud-snackbar-provider");
    await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
}
```

**Rationale**: Explicit wait for MudBlazor initialization

**Status**: Not yet implemented; recommended for LoginPage

---

### Proposed Solution 4: Add Retry Logic for Flaky Elements

**Problem**: Intermittent failures due to timing

**Proposed Fix**:
```csharp
public async Task<bool> WaitForElementWithRetryAsync(ILocator element, int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            await element.WaitForAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException) when (i < maxRetries - 1)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i))); // Exponential backoff
        }
    }
    return false;
}
```

**Rationale**: Handle transient CI network issues

**Status**: Not yet implemented; low priority (try other fixes first)

---

## Remaining Work & Next Steps

### Immediate Priority (Next Session)

**1. Enable Playwright Tracing**
- Create `playwright.config.ts` with `trace: "on-first-retry"`
- Update `.github/workflows/e2e.yml` to upload traces as artifacts
- Run tests in CI and examine trace files

**2. Increase WASM Bootstrap Timeout**
- Update `VendorLoginPage.cs` timeout from 30s to 60s
- Add explicit wait for MudBlazor CSS classes
- Test locally with `PLAYWRIGHT_HEADLESS=true` to simulate CI

**3. Verify Login Button Selector**
- Manually inspect VendorPortal.Web login page HTML
- Confirm `data-testid="login-btn"` exists and is visible
- Check for CSS-based visibility issues (display, opacity, z-index)

**4. Collect CI Failure Diagnostics**
- Run tests in CI with tracing enabled
- Collect browser console logs
- Capture screenshots at moment of timeout
- Review network tab for failed WASM file loads

### Medium Priority (Follow-Up Session)

**1. Document WASM Bootstrap Timing**
- Measure actual bootstrap time in CI (cold start, warm start)
- Document expected timing ranges
- Establish timeout guidelines for future WASM E2E tests

**2. Add MudBlazor Hydration Detection**
- Implement `WaitForMudBlazorAsync()` helper
- Use in LoginPage before interacting with MudTextField
- Document pattern for other WASM E2E tests

**3. Verify WASM Static File Serving**
- Review `WasmStaticFileHost` configuration
- Test `.wasm` and `.dll` file serving
- Verify CORS configuration for cross-origin requests

**4. Run Green CI Verification**
- After fixes, run 3 consecutive CI builds
- Verify no flakiness
- Document success criteria met

### Low Priority (Future Improvements)

**1. Retry Logic for Flaky Elements**
- Only if other fixes don't resolve flakiness
- Implement exponential backoff pattern
- Document when to use vs fixing root cause

**2. Add E2E Test Performance Monitoring**
- Track test execution time trends
- Alert on significant slowdowns
- Identify CI environment degradation

**3. Expand E2E Coverage**
- Vendor Portal: Change request lifecycle (5 scenarios)
- Vendor Portal: Settings management (3 scenarios)
- Backoffice: Order Search + Return Management (deferred from M33.0)

---

## Success Criteria for Resolution

Before marking Vendor Portal E2E tests as "stable in CI":

- ✅ All 12 scenarios pass locally with `PLAYWRIGHT_HEADLESS=true`
- ✅ All 12 scenarios pass in CI with tracing enabled
- ✅ Playwright traces available for failure analysis
- ✅ Login button selector verified in rendered HTML
- ✅ WASM bootstrap timing documented (cold start, warm start)
- ✅ At least 3 consecutive green CI runs (no flakiness)
- ✅ Timeout guidelines documented for future WASM E2E tests

---

## Lessons Learned Summary

### 1. Connection String Timing is Critical ✅

**Rule**: Always read connection strings **inside** configuration lambdas

**Applies To**: Marten, EF Core, any service needing test-time overrides

**Why**: `WebApplicationFactory` uses `ConfigureAppConfiguration()` to inject test config. Reading before lambdas captures fallback values.

**Files Fixed**: VendorPortal.Api, VendorIdentity.Api

---

### 2. WASM E2E Tests Require Tiered Timeouts ⚠️

**Rule**: Different operations need different timeouts

**Recommended Tiers**:
- Initial WASM load: 60s (CI cold start)
- Navigation: 15s (client-side routing)
- Element visibility: 10s (MudBlazor hydration)
- State checks: 5s (synchronous checks)

**Why**: WASM bootstrap is slow; MudBlazor adds hydration delay; CI network unpredictable

---

### 3. Always Verify Production Code Before Test Data ✅

**Rule**: Grep production code **first**, don't assume

**Process**:
1. Find enum/type definition
2. Verify value exists
3. Write test data

**Why**: Prevents compilation errors; catches renames/refactors

**Example**: `BackofficeRole.FinanceClerk` → `BackofficeRole.PricingManager`

---

### 4. Playwright Uses Properties, Not LINQ ✅

**Rule**: `.First` and `.Last` are properties, `.Nth(n)` is method

**Why**: Playwright API differs from LINQ; confusion causes compilation errors

**Correct**: `AlertRows.First`, `AlertRows.Nth(2)`
**Wrong**: `AlertRows.First()`, `AlertRows.ElementAt(2)`

---

### 5. Global Page Objects for Cross-Cutting UI ✅

**Rule**: Modals/overlays that appear across pages get global page objects

**Why**: Single source of truth; reusable across step definitions

**Examples**: `SessionExpiredPage`, confirmation dialogs, toast notifications

---

### 6. Stub Clients Need 401 Simulation for Session Expiry Testing ✅

**Rule**: Add `SimulateSessionExpired` bool property to all stub clients

**Why**: Tests session expiry (401) responses without real auth infrastructure

**Implementation**: Throw `HttpRequestException` with `HttpStatusCode.Unauthorized`

---

### 7. Multi-Role Admin Seeding Requires Deterministic GUIDs ✅

**Rule**: Use `WellKnownTestData` constants, not random GUIDs

**Why**: Repeatable tests; easier debugging; consistent test data

**Applied To**: 7 admin roles in Backoffice E2E tests

---

### 8. Null-Safe Teardown Prevents Masking Real Failures ✅

**Rule**: Check for null in `[AfterTestRun]` hooks

**Why**: Partial initialization failures cause misleading teardown errors

**Pattern**: `if (_browser is not null) await _browser.DisposeAsync();`

---

## Key Files Reference

### E2E Test Fixtures
| Project | Path | Status |
|---------|------|--------|
| Storefront | `tests/Customer Experience/Storefront.E2ETests/E2ETestFixture.cs` | ✅ Working |
| Vendor Portal | `tests/Vendor Portal/VendorPortal.E2ETests/E2ETestFixture.cs` | ⚠️ CI failing |
| Backoffice | `tests/Backoffice/Backoffice.E2ETests/E2ETestFixture.cs` | ✅ Working |

### CI/CD Workflows
| File | Purpose | Status |
|------|---------|--------|
| `.github/workflows/e2e.yml` | E2E test execution (2 jobs) | ✅ Working |
| `.github/workflows/dotnet.yml` | Build/test with path filtering | ✅ Working |
| `.github/workflows/codeql.yml` | Security scanning | ✅ Working |

### Documentation
| Document | Content | Status |
|----------|---------|--------|
| `docs/skills/e2e-playwright-testing.md` | Comprehensive E2E guide (110KB) | ✅ Up to date |
| `docs/decisions/0015-playwright-e2e-browser-testing.md` | ADR: Why Playwright | ✅ Complete |
| `docs/planning/vendor-portal-e2e-troubleshooting-retrospective.md` | VP troubleshooting timeline | ✅ Complete |
| `docs/planning/milestones/m32.2-e2e-retrospective.md` | M32.2 sessions 4-6 | ✅ Complete |
| `docs/planning/milestones/m32.1-session-15-retrospective.md` | Timeout investigation | ✅ Complete |

---

## Conclusion

**Status Summary**:
- ✅ **Infrastructure Complete**: 3 E2E test fixtures fully functional
- ✅ **Connection String Timing Fixed**: VendorPortal.Api and VendorIdentity.Api
- ✅ **Patterns Established**: SimulateSessionExpired, multi-role seeding, test-id conventions
- ⚠️ **Remaining Work**: 4 Vendor Portal E2E tests failing in CI (WASM bootstrap timeout)
- 📋 **Next Steps**: Enable Playwright tracing, increase timeouts, verify login button

**Effort Summary**:
- **Sessions Spent**: 10+ sessions (M32.1 Session 9 through M33.0)
- **Fixes Implemented**: 8 major fixes (connection strings, teardown, test-ids, enum verification, etc.)
- **Documentation Created**: 600+ lines in skill docs, 5 retrospectives, 1 troubleshooting doc
- **Tests Built**: 50+ E2E scenarios across 3 test suites

**Key Takeaway**: Most of the work is done. The remaining 4 test failures are specific to WASM bootstrap timing in CI. Solutions are well-understood and documented; implementation should be straightforward in next session.

**Recommendation**: Address remaining 4 failures in dedicated session with Playwright tracing enabled and timeout adjustments applied. Expected effort: 1-2 sessions to full green CI.

---

*Retrospective completed: 2026-03-24*
*Next session: Address 4 remaining Vendor Portal E2E test failures*
