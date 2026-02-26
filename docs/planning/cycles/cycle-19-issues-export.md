# Cycle 19: Authentication & Authorization - Issues Export

**Milestone:** Cycle 19: Authentication & Authorization
**Status:** CLOSED
**Closed:** 2026-02-26 06:45:57 UTC
**Total Issues:** 8 (8 closed, 0 open)

---

## Issue #57: [Auth] Replace stub customerId with Customer Identity BC authentication

**State:** CLOSED
**Created:** 2026-02-24T19:00:31Z
**Closed:** 2026-02-25T03:11:48Z
**Labels:** bc:customer-experience, type:feature, status:in-progress, value:high, urgency:high

### Description

Replace hardcoded stub `customerId` with real authentication via Customer Identity BC.

Authentication adds significant complexity and isn't required to demonstrate the reference architecture's core capabilities (event sourcing, sagas, BFF pattern, SSE). Deferred from Cycle 17 to allow focus on integration completeness.

### Tasks

- [x] Create ADR for authentication strategy (cookie vs JWT, where to store session)
- [x] Implement authentication in Storefront.Web (cookie/JWT)
- [x] Call Customer Identity BC for login/logout
- [x] Store `customerId` in session/claims
- [x] Update `Cart.razor`, `Checkout.razor` to use authenticated `customerId`
- [x] Add authorization policies (only authenticated users can access cart/checkout)
- [x] Add Login/Logout pages with MudBlazor forms
- [x] Add "Sign In" / "My Account" buttons to AppBar

### Acceptance Criteria

- ✅ Users must log in to access cart/checkout
- ✅ `CustomerId` comes from authenticated session (no hardcoded GUIDs)
- ✅ Logout clears session
- ✅ Protected routes redirect to login page
- ✅ Session persists across browser refreshes

### Dependencies

- Customer Identity BC complete ✅
- Cycle 17 complete ✅
- Cycle 18 complete ✅

### Effort

2–3 sessions (~4–6 hours)

### References

- `docs/planning/cycles/cycle-17-customer-experience-enhancement.md`
- `docs/planning/cycles/cycle-18-customer-experience-phase-2.md`

---

## Issue #136: 🚀 Cycle 19: Authentication & Authorization

**State:** CLOSED
**Created:** 2026-02-25T00:46:25Z
**Closed:** 2026-02-25T03:49:01Z
**Labels:** type:feature, urgency:high

### Description

Parent epic tracking Cycle 19 work. See `docs/planning/cycles/cycle-19-authentication-authorization.md` for full plan.

---

## Issue #140: [ADR] Authentication Strategy (Cookie vs JWT)

**State:** CLOSED
**Created:** 2026-02-26T04:32:39Z
**Closed:** 2026-02-26T04:33:30Z
**Labels:** bc:customer-experience, type:documentation, value:high, urgency:high

### Objective

Create ADR 0012 documenting authentication strategy for Storefront.Web

### Context

Need to decide between cookie-based auth vs JWT tokens for Blazor Server authentication.

### Tasks

- [x] Create `docs/decisions/0012-authentication-strategy.md`
- [x] Evaluate cookie-based auth (ASP.NET Core Identity cookies)
- [x] Evaluate JWT tokens (access + refresh)
- [x] Consider Blazor Server render mode compatibility
- [x] Consider SSE authentication (EventSource limitation with headers)
- [x] Document decision rationale
- [x] Define claim structure (`customerId`, `email`, `name`)

### Acceptance Criteria

- ✅ ADR document created
- ✅ Authentication approach decided and documented
- ✅ Claim structure defined

### References

- Cycle 19 Plan: `docs/planning/cycles/cycle-19-authentication-authorization.md`
- [ASP.NET Core Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)
- [Blazor Server Security](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/server/)

---

## Issue #141: Login/Logout Pages with MudBlazor Forms

**State:** CLOSED
**Created:** 2026-02-26T04:32:41Z
**Closed:** 2026-02-26T04:45:31Z
**Labels:** bc:customer-experience, type:feature, value:high, urgency:high

### Objective

Create login and logout pages in Storefront.Web using MudBlazor components

### Tasks

- [x] Create `/Pages/Account/Login.razor` (MudBlazor form)
- [x] Create `/Pages/Account/Logout.razor`
- [x] Create `LoginModel` record with email/password
- [x] Create `LoginValidator` (FluentValidation)
- [x] Add authentication service interface `IAuthenticationService`
- [x] Implement authentication service calling Customer Identity BC
- [x] Handle authentication success (set cookies/claims)
- [x] Handle authentication failure (show error message)

### Acceptance Criteria

- ✅ Login page renders with MudBlazor form
- ✅ Validation works (required fields, email format)
- ✅ Successful login creates authentication cookie
- ✅ Successful login redirects to returnUrl (or home)
- ✅ Failed login shows error message
- ✅ Logout clears authentication cookie
- ✅ Logout redirects to home page

### References

- Cycle 19 Plan: `docs/planning/cycles/cycle-19-authentication-authorization.md` (Deliverable #2)

---

## Issue #142: Protected Routes & Authorization Policies

**State:** CLOSED
**Created:** 2026-02-26T04:32:43Z
**Closed:** 2026-02-26T04:45:33Z
**Labels:** bc:customer-experience, type:feature, value:high, urgency:high

### Objective

Restrict cart and checkout pages to authenticated users only

### Tasks

- [x] Add `@attribute [Authorize]` to Cart.razor
- [x] Add `@attribute [Authorize]` to Checkout.razor
- [x] Configure authentication middleware in Program.cs
- [x] Add `UseAuthentication()` and `UseAuthorization()` to pipeline
- [x] Create `/Pages/Account/AccessDenied.razor` (friendly error page)
- [x] Test: Unauthenticated user accessing /cart redirects to /account/login
- [x] Test: After login, user redirected back to /cart

### Acceptance Criteria

- ✅ Cart page requires authentication
- ✅ Checkout page requires authentication
- ✅ Unauthenticated access redirects to login page with returnUrl
- ✅ After successful login, user redirected to original page
- ✅ Logout redirects to home page

### Dependencies

- Blocked by: Login/Logout Pages issue

### References

- Cycle 19 Plan: `docs/planning/cycles/cycle-19-authentication-authorization.md` (Deliverable #3)

---

## Issue #143: Replace Stub CustomerId with Session

**State:** CLOSED
**Created:** 2026-02-26T04:32:45Z
**Closed:** 2026-02-26T04:48:06Z
**Labels:** bc:customer-experience, type:feature, value:high, urgency:high

### Objective

Remove hardcoded customerId GUIDs and use authenticated user's customerId from claims

### Current Hardcoded Locations

- `Cart.razor`: `_cartId = Guid.Parse("22222222-...")`
- `Products.razor`: `_customerId = Guid.Parse("11111111-...")`
- `Checkout.razor`: Similar stub GUIDs

### Tasks

- [x] Create `ICurrentUserService` interface with `GetCustomerId()` method
- [x] Implement `CurrentUserService` reading from `HttpContext.User.Claims`
- [x] Register in DI container (scoped lifetime)
- [x] Update `Cart.razor` to inject `ICurrentUserService` and get customerId
- [x] Update `Products.razor` to inject `ICurrentUserService` and get customerId
- [x] Update `Checkout.razor` to inject `ICurrentUserService` and get customerId
- [x] Update BFF query handlers (GetCartView, GetCheckoutView) to read customerId from claims
- [x] Remove all hardcoded GUID constants

### Acceptance Criteria

- ✅ All pages use authenticated customerId (no hardcoded GUIDs)
- ✅ Cart shows authenticated user's cart
- ✅ Checkout uses authenticated user's customerId
- ✅ Product "Add to Cart" uses authenticated customerId
- ✅ SSE connection includes authenticated customerId

### Dependencies

- Blocked by: Login/Logout Pages issue

### References

- Cycle 19 Plan: `docs/planning/cycles/cycle-19-authentication-authorization.md` (Deliverable #4)

---

## Issue #144: AppBar: Sign In / My Account UI

**State:** CLOSED
**Created:** 2026-02-26T04:32:47Z
**Closed:** 2026-02-26T04:45:35Z
**Labels:** bc:customer-experience, type:feature, value:medium, urgency:high

### Objective

Add authentication UI to application header (MainLayout.razor)

### Tasks

- [x] Update `Shared/MainLayout.razor` AppBar to show:
  - **If unauthenticated:** "Sign In" button (links to /account/login)
  - **If authenticated:** "My Account" dropdown menu with:
    - User's email/name
    - "Order History" link
    - "Sign Out" button
- [x] Use `<AuthorizeView>` component to conditionally render
- [x] Style with MudBlazor components (MudMenu, MudMenuItem, MudIconButton)
- [x] Add user icon (MudIcon Icons.Material.Filled.AccountCircle)

### Acceptance Criteria

- ✅ Unauthenticated users see "Sign In" button
- ✅ Authenticated users see "My Account" dropdown
- ✅ Dropdown shows user's email
- ✅ "Sign Out" button logs out user
- ✅ UI updates immediately after login/logout (reactive)

### Dependencies

- Blocked by: Login/Logout Pages issue

### References

- Cycle 19 Plan: `docs/planning/cycles/cycle-19-authentication-authorization.md` (Deliverable #5)
- [MudBlazor AppBar](https://mudblazor.com/components/appbar)
- [Blazor AuthorizeView](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/#authorizeview-component)

---

## Issue #145: Customer Identity BC: Add Password Authentication Endpoint

**State:** CLOSED
**Created:** 2026-02-26T04:32:49Z
**Closed:** 2026-02-26T04:34:32Z
**Labels:** bc:customer-identity, type:feature, value:high, urgency:high

### Objective

Add password authentication capability to Customer Identity BC

### Context

Currently Customer Identity BC only stores email/name. Need to add password storage and authentication endpoint for Storefront login.

### Tasks

- [x] Add `PasswordHash` field to Customer entity
- [x] Add `BCrypt.Net-Next` package reference
- [x] Create `POST /api/customers/authenticate` endpoint
  - Request: `{ "email": "alice@example.com", "password": "password123" }`
  - Response: `{ "customerId": "guid", "email": "...", "firstName": "...", "lastName": "..." }`
  - Returns 401 Unauthorized if credentials invalid
- [x] Update customer seeding to include password hashes
- [x] Create integration tests for authentication endpoint
- [x] Update `.http` file with authentication examples

### Acceptance Criteria

- ✅ Customer entity stores password hash (BCrypt)
- ✅ Authentication endpoint validates credentials
- ✅ Returns customer data on success
- ✅ Returns 401 on failure
- ✅ Seeded customers have passwords (e.g., "password123")

### Security Considerations

- Use BCrypt for password hashing (not plain text!)
- Never return password hash in API responses
- Log failed authentication attempts

### References

- Cycle 19 Plan: `docs/planning/cycles/cycle-19-authentication-authorization.md` (Technical Considerations)
- [BCrypt.Net](https://github.com/BcryptNet/bcrypt.net)

---

## Summary Statistics

- **Total Issues:** 8
- **Closed:** 8
- **Open:** 0
- **Labels:**
  - `bc:customer-experience`: 5 issues
  - `bc:customer-identity`: 1 issue
  - `type:feature`: 6 issues
  - `type:documentation`: 1 issue
  - `value:high`: 7 issues
  - `urgency:high`: 8 issues

## Key Deliverables Achieved

1. ✅ Cookie-based authentication with ASP.NET Core Identity
2. ✅ Customer Identity BC password authentication endpoint
3. ✅ Login/Logout pages with MudBlazor forms
4. ✅ Protected routes with `[Authorize]` attribute
5. ✅ AppBar authentication UI with user dropdown
6. ✅ Replaced stub customerId with authenticated session
7. ✅ Cart initialization tied to authenticated users
8. ✅ localStorage cart persistence across pages

## Bug Fixes (Post-Implementation)

- Added Swagger UI to ProductCatalog.Api
- Seeded product data for development testing
- Reduced Npgsql logging noise (changed to Warning level)
- Fixed cart initialization endpoint (POST /api/storefront/carts/initialize)
- Implemented localStorage cart persistence to fix cart page showing empty

## Related Pull Requests

- PR #148: Cycle 19 - Authentication & Authorization Implementation
