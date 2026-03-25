# GitHub Issue: Vendor Portal Dashboard RBAC — ReadOnly users blocked from viewing change requests

**Title:** `Vendor Portal Dashboard RBAC: ReadOnly users cannot view change requests`

**Labels:** `bc:vendor-portal`, `type:bug`, `priority:high`, `milestone:M34.0`

---

## Problem

The Vendor Portal Dashboard gates both "Submit Change Request" AND "View Change Requests" buttons behind the `CanSubmitChangeRequests` permission. This blocks ReadOnly users from viewing change requests entirely, when they should have read-only access.

**Location:** `src/Vendor Portal/VendorPortal.Web/Pages/Dashboard.razor` lines 111-126

**Current Behavior:**
- ReadOnly users see **neither button** (blocked from viewing)
- CatalogManager users see both buttons (correct)
- Admin users see both buttons (correct)

**Expected Behavior:**
- ReadOnly users should **VIEW** change requests (read-only access) but **NOT SUBMIT**
- All users should be able to view change requests (auditing/visibility)
- Only users with `CanSubmitChangeRequests` permission should see the Submit button

## Impact

**Blocking 3 E2E tests:**
1. ❌ "ReadOnly user cannot see the submit button" — Can't navigate to change requests page
2. ❌ "CatalogManager saves a draft change request" — Residual error UI issue after navigation
3. ❌ "CatalogManager submits a change request end-to-end" — Residual error UI issue

**Test Results Before Fix:**
- 9/12 passing (75%) after M33.0 E2E stabilization patterns applied
- 3/12 blocked by this RBAC bug

## Root Cause

```csharp
// Dashboard.razor lines 111-126
@if (AuthState.CanSubmitChangeRequests)
{
    <MudButton ... data-testid="submit-change-request-btn">Submit Change Request</MudButton>
    <MudButton ... data-testid="view-change-requests-btn">View Change Requests</MudButton>
}
```

Both buttons are inside the same `@if` block. The View button should be visible to all authenticated users (or gated by a separate `CanViewChangeRequests` permission).

## Proposed Fix

**Option A (Simple - Recommended):** Split the buttons

```csharp
<MudButton Variant="Variant.Outlined" Color="Color.Default"
           StartIcon="@Icons.Material.Filled.List" Class="mr-2"
           Href="/change-requests"
           data-testid="view-change-requests-btn">
    View Change Requests
</MudButton>

@if (AuthState.CanSubmitChangeRequests)
{
    <MudButton Variant="Variant.Outlined" Color="Color.Primary"
               StartIcon="@Icons.Material.Filled.Add" Class="mr-2"
               Href="/change-requests/submit"
               data-testid="submit-change-request-btn">
        Submit Change Request
    </MudButton>
}
```

**Option B (If Fine-Grained Control Needed):** Add separate permission

```csharp
@if (AuthState.CanViewChangeRequests)
{
    <MudButton ... data-testid="view-change-requests-btn">View Change Requests</MudButton>
}

@if (AuthState.CanSubmitChangeRequests)
{
    <MudButton ... data-testid="submit-change-request-btn">Submit Change Request</MudButton>
}
```

## Test Validation

After fix is applied, run:
```bash
dotnet test "tests/Vendor Portal/VendorPortal.E2ETests/VendorPortal.E2ETests.csproj"
```

Expected: 12/12 tests passing (100%)

## Context

Discovered during M34.0 prep work while applying E2E test stabilization patterns from M33.0 learnings.

**Related Work:**
- ✅ M33.0 E2E stabilization patterns applied (9 tests now passing)
- ✅ SignalR timeout configuration increased (30s → 60s)
- ✅ Aggressive Blazor error UI checks removed
- ❌ This RBAC bug is blocking remaining 3 tests

**Commit:** See `claude/fix-vendor-portal-e2e-tests` branch commit ddfddcb

## Priority

**High** — Blocking 25% of Vendor Portal E2E test suite (3/12 tests)

Should be addressed early in M34 Track A or B (preferably as first item in Track B or alongside Test Infrastructure Completion work).

## Acceptance Criteria

- [ ] ReadOnly users can navigate to change requests list page
- [ ] ReadOnly users see "View Change Requests" button on Dashboard
- [ ] ReadOnly users do NOT see "Submit Change Request" button on Dashboard
- [ ] All 12 Vendor Portal E2E tests pass (100%)
- [ ] No regression in existing RBAC behavior for CatalogManager/Admin roles

---

**To create this issue in GitHub:**
1. Navigate to https://github.com/erikshafer/CritterSupply/issues/new
2. Copy title and body content from above
3. Apply labels: `bc:vendor-portal`, `type:bug`, `priority:high`
4. Assign to Milestone: `M34.0`
