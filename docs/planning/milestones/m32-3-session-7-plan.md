# M32.3 Session 7 Plan: User Management Write UI

**Date:** 2026-03-20
**Milestone:** M32.3 — Backoffice Phase 3B: Write Operations Depth
**Session:** 7 of 10
**Goal:** Implement User Management write UI for SystemAdmin role

---

## Context

### From Session 6 Retrospective

**Action Items for Session 7:**
1. **User Management write UI** (primary focus):
   - Admin user list page
   - Admin user create/edit page
   - Role assignment workflow
   - Password reset (SystemAdmin only)

2. **Fix PricingAdmin.feature step definition alignment** (if time permits):
   - Change `the Backoffice system is running` → `the Backoffice application is running`
   - Add `stub catalog client has product` step definition
   - Fix `admin user exists with email` pattern to include name parameter

### Current State (from CURRENT-CYCLE.md)

**M32.3 Sessions Completed:**
- ✅ Session 1: Product Admin write UI (ProductEdit.razor)
- ✅ Session 2: Product List UI + API routing audit (ProductList.razor)
- ✅ Session 3: E2E tests + Pricing Admin write UI (PriceEdit.razor)
- ✅ Session 4: Warehouse Admin write UI (InventoryList.razor, InventoryEdit.razor)
- ✅ Session 5: Pricing Admin E2E tests (6 scenarios)
- ✅ Session 6: Warehouse Admin E2E tests (10 scenarios)

**Build Status:** 0 errors, 34 pre-existing warnings

---

## Existing BackofficeIdentity BC Endpoints

### Available Endpoints (All Require SystemAdmin Role)

1. **GET /api/backoffice-identity/users** ✅
   - Handler: `GetBackofficeUsersHandler`
   - Returns: `IReadOnlyList<BackofficeUserSummary>`
   - Fields: Id, Email, FirstName, LastName, Role, Status, CreatedAt, LastLoginAt, DeactivatedAt

2. **POST /api/backoffice-identity/users** ✅
   - Handler: `CreateBackofficeUserHandler`
   - Request: `CreateBackofficeUser` (Email, Password, FirstName, LastName, Role)
   - Response: `CreateBackofficeUserResponse` (Id, Email, FirstName, LastName, Role, CreatedAt)
   - Validation: FluentValidation (email format, password min 8 chars, names max 100 chars)
   - Business Rule: Email must be unique (returns 400 if duplicate)

3. **PUT /api/backoffice-identity/users/{userId}/role** ✅
   - Handler: `ChangeBackofficeUserRoleHandler`
   - Request: `ChangeBackofficeUserRole` (UserId, NewRole)
   - Returns: Updated role or 404 if user not found

4. **POST /api/backoffice-identity/users/{userId}/deactivate** ✅
   - Handler: `DeactivateBackofficeUserHandler`
   - Request: `DeactivateBackofficeUser` (UserId, Reason)
   - Returns: Success or 404 if user not found
   - Side Effect: Sets Status=Deactivated, DeactivatedAt=UtcNow, DeactivationReason

### Missing Endpoint (Needs Implementation)

**5. Password Reset** ❌ NOT YET IMPLEMENTED
   - Proposed: `POST /api/backoffice-identity/users/{userId}/reset-password`
   - Request: `ResetBackofficeUserPassword` (UserId, NewPassword)
   - Validation: Password min 8 chars (same as CreateBackofficeUser)
   - Security: Only SystemAdmin can reset passwords
   - Implementation: Use same `PasswordHasher<BackofficeUser>` as CreateBackofficeUserHandler

---

## Session 7 Deliverables

### Phase 1: BackofficeIdentity BC — Password Reset Endpoint

**Files to Create:**
1. `src/Backoffice Identity/BackofficeIdentity/UserManagement/ResetBackofficeUserPassword.cs`
   - Command record: `ResetBackofficeUserPassword(Guid UserId, string NewPassword)`
   - Validator: `ResetBackofficeUserPasswordValidator` (password min 8 chars)
   - Handler: `ResetBackofficeUserPasswordHandler` (update password hash)
   - Response: `ResetPasswordResponse(Guid UserId, DateTimeOffset ResetAt)`

2. `src/Backoffice Identity/BackofficeIdentity.Api/UserManagement/ResetBackofficeUserPasswordEndpoint.cs`
   - Route: `POST /api/backoffice-identity/users/{userId}/reset-password`
   - Authorization: `[Authorize(Policy = "SystemAdmin")]`
   - Returns: 200 OK with response or 404 if user not found

**Pattern to Follow:**
- Copy structure from `CreateBackofficeUserHandler` (password hashing logic)
- Copy endpoint pattern from `DeactivateBackofficeUserEndpoint` (userId route parameter)

---

### Phase 2: Backoffice BFF — Client Interface Extension

**File to Modify:**
Create new interface (no existing IBackofficeIdentityClient):

`src/Backoffice/Backoffice/Clients/IBackofficeIdentityClient.cs`

**Methods to Add:**
```csharp
public interface IBackofficeIdentityClient
{
    Task<IReadOnlyList<BackofficeUserSummaryDto>> ListUsersAsync();
    Task<CreateUserResultDto?> CreateUserAsync(string email, string password, string firstName, string lastName, string role);
    Task<bool> ChangeUserRoleAsync(Guid userId, string newRole);
    Task<bool> DeactivateUserAsync(Guid userId, string reason);
    Task<bool> ResetUserPasswordAsync(Guid userId, string newPassword);
}
```

**DTOs to Define:**
```csharp
public sealed record BackofficeUserSummaryDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? DeactivatedAt);

public sealed record CreateUserResultDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    DateTimeOffset CreatedAt);
```

---

### Phase 3: Backoffice BFF — Client Implementation

**File to Create:**
`src/Backoffice/Backoffice.Api/Clients/BackofficeIdentityClient.cs`

**Implementation Pattern:**
- Follows same HttpClient pattern as `CatalogClient`, `PricingClient`, `InventoryClient`
- Base URL: `http://localhost:5249` (BackofficeIdentity.Api port from CLAUDE.md)
- All endpoints require JWT bearer token (SystemAdmin role)
- Returns null on unsuccessful responses

**Example Method:**
```csharp
public async Task<IReadOnlyList<BackofficeUserSummaryDto>> ListUsersAsync()
{
    var response = await _httpClient.GetAsync("/api/backoffice-identity/users");
    if (!response.IsSuccessStatusCode) return Array.Empty<BackofficeUserSummaryDto>();

    var users = await response.Content.ReadFromJsonAsync<IReadOnlyList<BackofficeUserSummaryDto>>();
    return users ?? Array.Empty<BackofficeUserSummaryDto>();
}
```

---

### Phase 4: Backoffice BFF — Proxy Endpoints (Optional)

**Decision Point:** Do we need BFF proxy endpoints, or can Blazor Web call BackofficeIdentity.Api directly?

**Option A (No BFF Proxy):**
- Blazor Web calls BackofficeIdentity.Api directly via IBackofficeIdentityClient
- Simpler architecture (fewer files)
- Consistent with how Login.razor calls BackofficeIdentity.Api

**Option B (With BFF Proxy):**
- Create 5 proxy endpoints in Backoffice.Api/Queries/ and Backoffice.Api/Commands/
- Consistent with CatalogClient, PricingClient, InventoryClient patterns
- Provides centralized authorization enforcement at BFF layer

**Recommendation:** Option A (no proxy endpoints) — User management is already isolated to BackofficeIdentity BC, no cross-BC composition needed.

---

### Phase 5: Blazor Web — User Management Pages

**Files to Create:**

1. **`src/Backoffice/Backoffice.Web/Pages/Users/UserList.razor`**
   Route: `/users`
   Authorization: `@attribute [Authorize(Policy = "SystemAdmin")]`

   **Features:**
   - MudTable displaying all users (Email, Name, Role, Status, Created, Last Login)
   - Client-side search by email or name
   - Color-coded status chips (Active=Green, Deactivated=Red)
   - Row click navigation to edit page
   - "Create User" button (top-right) → navigates to `/users/create`
   - Session-expired handling (401 → SessionExpiredService)

   **Data Flow:**
   ```csharp
   private async Task LoadUsersAsync()
   {
       var httpClient = HttpFactory.CreateClient("BackofficeIdentityApi");
       httpClient.DefaultRequestHeaders.Authorization =
           new AuthenticationHeaderValue("Bearer", AuthState.AccessToken);

       var client = new BackofficeIdentityClient(httpClient);
       _users = await client.ListUsersAsync();
   }
   ```

2. **`src/Backoffice/Backoffice.Web/Pages/Users/UserCreate.razor`**
   Route: `/users/create`
   Authorization: `@attribute [Authorize(Policy = "SystemAdmin")]`

   **Features:**
   - Form with 5 fields: Email, Password, First Name, Last Name, Role (MudSelect)
   - Role dropdown populated from BackofficeRole enum values (7 roles)
   - Client-side validation (required fields, email format, password min 8 chars)
   - Submit button → POST to BackofficeIdentity.Api
   - Success: Show success message + navigate back to `/users`
   - Error: Show error message (e.g., "Email already exists")
   - Cancel button → navigate back to `/users`
   - Session-expired handling

3. **`src/Backoffice/Backoffice.Web/Pages/Users/UserEdit.razor`**
   Route: `/users/{userId}/edit`
   Authorization: `@attribute [Authorize(Policy = "SystemAdmin")]`

   **Features:**
   - User details card (Email, Name, Role, Status, Created, Last Login — read-only)
   - **Section 1: Change Role**
     - MudSelect dropdown with current role pre-selected
     - Submit button (disabled if role unchanged)
   - **Section 2: Reset Password**
     - New Password input (MudTextField, Password=true)
     - Confirm Password input (must match)
     - Submit button (disabled if passwords don't match or < 8 chars)
     - ⚠️ Warning message: "User will be logged out immediately after password reset"
   - **Section 3: Deactivate User** (only if Status=Active)
     - Reason input (MudTextField)
     - Deactivate button (two-click pattern: "Deactivate" → "Confirm Deactivation")
     - ⚠️ Warning message: "User will lose access immediately"
   - Breadcrumbs navigation back to list
   - Session-expired handling

---

### Phase 6: Navigation Updates

**File to Modify:**
`src/Backoffice/Backoffice.Web/Pages/Index.razor`

**Change:**
```diff
- @* M32.3 Session 7: User Management coming soon *@
+ <AuthorizeView Policy="SystemAdmin">
+     <MudButton Href="/users" Variant="Variant.Filled" Color="Color.Primary" FullWidth>
+         User Management (Admin Users)
+     </MudButton>
+ </AuthorizeView>
```

**Update Progress Banner:**
```csharp
M32.3 Session 7: User Management now available (SystemAdmin role)
```

---

### Phase 7: Stub Client Updates

**Integration Tests:**
`tests/Backoffice/Backoffice.Api.IntegrationTests/StubClients.cs`

**E2E Tests:**
`tests/Backoffice/Backoffice.E2ETests/Stubs/StubBackofficeIdentityClient.cs`

**Mock Data Pattern:**
- ListUsersAsync: Return 3 test users (SystemAdmin, CopyWriter, WarehouseClerk)
- CreateUserAsync: Return mock CreateUserResultDto with new Guid
- ChangeUserRoleAsync: Return true
- DeactivateUserAsync: Return true
- ResetUserPasswordAsync: Return true
- Session expiry simulation: Check `SimulateSessionExpired` flag

---

### Phase 8: Program.cs Configuration

**File to Modify:**
`src/Backoffice/Backoffice.Web/Program.cs`

**Add Named HttpClient:**
```csharp
builder.Services.AddHttpClient("BackofficeIdentityApi", client =>
{
    client.BaseAddress = new Uri("http://localhost:5249");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});
```

**Why Named Client:**
- Blazor WASM requires explicit BaseAddress configuration (no environment-based discovery)
- Consistent with existing "CatalogApi", "PricingApi", "InventoryApi" clients

---

## Technical Patterns (From Sessions 1-6)

### 1. MudBlazor v9 Type Parameters
All MudBlazor components require explicit `T` parameter:
```csharp
<MudSelect T="string" @bind-Value="_selectedRole" Label="Role">
```

### 2. Session-Expired Handling
```csharp
if (response.StatusCode == HttpStatusCode.Unauthorized)
{
    SessionExpiredService.TriggerSessionExpired();
    return;
}
```

### 3. Success/Error Feedback
```csharp
@if (_showSuccessMessage)
{
    <MudAlert Severity="Severity.Success" data-testid="success-message">User created successfully.</MudAlert>
}
@if (_errorMessage is not null)
{
    <MudAlert Severity="Severity.Error" data-testid="error-message">@_errorMessage</MudAlert>
}
```

### 4. Two-Click Deactivation Pattern
```csharp
@if (!_deactivationConfirmed)
{
    <MudButton OnClick="() => _deactivationConfirmed = true" Color="Color.Error">Deactivate User</MudButton>
}
else
{
    <MudButton OnClick="DeactivateUserAsync" Color="Color.Error" Variant="Variant.Filled">Confirm Deactivation</MudButton>
}
```

### 5. Local DTO Pattern
Define DTOs in Backoffice/Backoffice/Clients/ (not in Blazor Web project):
```csharp
public sealed record BackofficeUserSummaryDto(...);
```

---

## Risks

### R1: Password Reset Security Considerations ⚠️
**Risk:** Password reset immediately invalidates user's JWT, potentially logging them out of active sessions.
**Mitigation:** Display warning message in UI + refresh token invalidation in handler.
**Status:** Accepted risk (standard security practice).

### R2: No Email Verification ⚠️
**Risk:** SystemAdmin can create users with any email address (no verification email sent).
**Mitigation:** Phase 1 MVP — email verification deferred to Phase 3+.
**Status:** Accepted risk (internal users only, SystemAdmin trusted).

### R3: Role Enum Serialization ⚠️
**Risk:** BackofficeRole enum serialized as kebab-case in JWT but PascalCase in JSON responses.
**Mitigation:** Use `role.ToString()` in DTOs (matches existing pattern from CreateBackofficeUserResponse).
**Status:** Low risk (consistent with existing endpoints).

---

## Success Criteria

1. ✅ Password reset endpoint implemented in BackofficeIdentity BC
2. ✅ IBackofficeIdentityClient interface created with 5 methods
3. ✅ BackofficeIdentityClient HTTP client implementation
4. ✅ UserList.razor page displays all users with search and navigation
5. ✅ UserCreate.razor page creates new users with validation
6. ✅ UserEdit.razor page supports role change, password reset, deactivation
7. ✅ Index.razor navigation updated for SystemAdmin role
8. ✅ Stub clients updated (integration + E2E tests)
9. ✅ Build succeeds with 0 errors
10. ✅ Manual smoke test: Create user → Change role → Reset password → Deactivate

---

## Deferred Work

### D1: E2E Tests for User Management
**Description:** Gherkin `.feature` file + Playwright page objects for:
- Browse user list
- Create new user (happy path + validation)
- Change user role
- Reset password
- Deactivate user

**Why Deferred:** Core UI functionality takes priority; E2E tests are polish layer.
**Tracking:** Create GitHub Issue for M32.4 or Session 10 (E2E stabilization).

### D2: Email Verification Workflow
**Description:** Send verification email when user is created, require email confirmation before login.
**Why Deferred:** Phase 1 MVP — internal users only, SystemAdmin trusted.
**Tracking:** Backlog item for Phase 3+.

### D3: Reactivate User Workflow
**Description:** Allow SystemAdmin to reactivate deactivated users (set Status=Active, clear DeactivatedAt).
**Why Deferred:** Deactivation is terminal in Phase 1 MVP.
**Tracking:** Backlog item for Phase 3+.

### D4: Audit Log for User Management Actions
**Description:** Record all user creation, role changes, password resets, deactivations in audit log.
**Why Deferred:** Phase 1 MVP — no audit log infrastructure yet.
**Tracking:** Dependent on Audit Log BC (future milestone).

---

## References

- **CLAUDE.md** — Port allocation (BackofficeIdentity.Api: 5249)
- **M32.3 Session 4 Retrospective** — Client extension checklist pattern
- **M32.3 Session 5 Retrospective** — E2E test patterns (hidden message divs)
- **M32.3 Session 6 Retrospective** — "Read before write" lesson for Page Objects
- **ADR 0031** — Backoffice RBAC Model (7 roles)
- **Skills:**
  - `docs/skills/blazor-wasm-jwt.md` — Named HttpClient, JWT auth patterns
  - `docs/skills/efcore-wolverine-integration.md` — EF Core handler patterns
  - `docs/skills/modern-csharp-coding-standards.md` — Immutability, sealed records

---

## Estimated Duration

**Time Estimate:** 2-3 hours

**Breakdown:**
- Phase 1 (Password Reset Endpoint): 30 minutes
- Phase 2-3 (Client Interface + Implementation): 30 minutes
- Phase 5 (Blazor Pages): 60-90 minutes (3 pages)
- Phase 6-8 (Navigation, Stubs, Config): 20 minutes
- Build verification + smoke test: 10 minutes

---

## Session Workflow

1. ✅ Read CURRENT-CYCLE.md, Session 6 retrospective, Session 4 retrospective
2. ✅ Create Session 7 plan (this document)
3. Implement Phase 1: Password reset endpoint
4. Implement Phase 2-3: Client interface + implementation
5. Implement Phase 5: Blazor pages (UserList, UserCreate, UserEdit)
6. Implement Phase 6-8: Navigation, stubs, config
7. Build verification (`dotnet build`)
8. Manual smoke test (create user, change role, reset password, deactivate)
9. Commit changes (atomic commits per phase)
10. Write Session 7 retrospective
11. Update CURRENT-CYCLE.md
12. Open PR

---

**Plan Written By:** Claude Sonnet 4.5
**Date:** 2026-03-20
**Milestone:** M32.3 (Backoffice Admin — Write Operations)
