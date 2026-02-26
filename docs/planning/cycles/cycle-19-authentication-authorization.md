# Cycle 19: Authentication & Authorization

**Status:** 🚀 In Progress
**Started:** 2026-02-25
**Target Duration:** 2-3 sessions
**GitHub Milestone:** [Cycle 19: Authentication & Authorization](https://github.com/erikshafer/CritterSupply/milestone/1)

---

## Objective

Replace stub `customerId` with real authentication via Customer Identity BC. Implement login/logout flows, protected routes, and session management in Storefront.Web (Blazor).

**Current State:** Storefront.Web uses hardcoded `customerId` GUIDs in Cart.razor and Checkout.razor
**Goal:** Users must authenticate to access cart/checkout. CustomerId comes from authenticated session.

---

## Key Deliverables

### 1. Authentication Strategy ADR

**Objective:** Decide on authentication approach (cookie vs JWT, session storage, claim structure)

**Tasks:**
- [ ] Create `docs/decisions/0012-authentication-strategy.md`
- [ ] Evaluate options:
  - **Cookie-based auth** (ASP.NET Core Identity cookies) - simple, works with Blazor Server
  - **JWT tokens** (access + refresh) - more complex, better for APIs
  - **Hybrid** (cookies for web, JWT for API)
- [ ] Document decision rationale
- [ ] Define claim structure (`customerId`, `email`, `name`)

**Decision Criteria:**
- Blazor Server render mode compatibility
- Session persistence across page refreshes
- SSE authentication (EventSource doesn't support custom headers)
- Development simplicity vs production readiness

**Recommendation:** Cookie-based auth with ASP.NET Core Authentication middleware (simplest for Blazor Server + SSE)

**Acceptance Criteria:**
- ✅ ADR document created and reviewed
- ✅ Authentication approach decided and documented
- ✅ Claim structure defined

**References:**
- [ASP.NET Core Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)
- [Blazor Authentication & Authorization](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/)

---

### 2. Login/Logout Pages (MudBlazor Forms)

**Objective:** Create login and logout pages in Storefront.Web using MudBlazor components

**Tasks:**
- [ ] Create `/Pages/Account/Login.razor` (MudBlazor form)
  - Email input (MudTextField)
  - Password input (MudTextField type="password")
  - "Sign In" button (MudButton)
  - Validation feedback (FluentValidation)
  - Error messages (MudAlert)
- [ ] Create `/Pages/Account/Logout.razor` (simple confirmation page)
- [ ] Create `LoginModel` record with email/password
- [ ] Create `LoginValidator` (FluentValidation)
- [ ] Add authentication service interface `IAuthenticationService`
- [ ] Implement authentication service calling Customer Identity BC
- [ ] Handle authentication success (set cookies/claims)
- [ ] Handle authentication failure (show error message)

**UI Interactions:**
```
User navigates to /account/login
  ↓
Enters email + password
  ↓
Clicks "Sign In"
  ↓
Storefront.Web calls Customer Identity API (port 5235)
  ↓
Customer Identity validates credentials
  ↓
If valid: Return customer data (customerId, email, name)
  ↓
Storefront.Web creates authentication cookie
  ↓
Redirect to /cart (or returnUrl)
```

**Acceptance Criteria:**
- ✅ Login page renders with MudBlazor form
- ✅ Validation works (required fields, email format)
- ✅ Successful login creates authentication cookie
- ✅ Successful login redirects to returnUrl (or home)
- ✅ Failed login shows error message
- ✅ Logout clears authentication cookie
- ✅ Logout redirects to home page

**Notes:**
- Use existing Customer Identity BC endpoints (GetCustomerByEmail, ValidatePassword)
- May need to add password validation endpoint to Customer Identity BC
- Store customerId in claims (`ClaimTypes.NameIdentifier`)

---

### 3. Protected Routes & Authorization Policies

**Objective:** Restrict cart and checkout pages to authenticated users only

**Tasks:**
- [ ] Add `@attribute [Authorize]` to Cart.razor
- [ ] Add `@attribute [Authorize]` to Checkout.razor
- [ ] Configure authentication middleware in Program.cs:
  ```csharp
  builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
      .AddCookie(options =>
      {
          options.LoginPath = "/account/login";
          options.LogoutPath = "/account/logout";
          options.AccessDeniedPath = "/account/access-denied";
      });
  builder.Services.AddAuthorization();
  ```
- [ ] Add authentication/authorization to Blazor app:
  ```csharp
  app.UseAuthentication();
  app.UseAuthorization();
  ```
- [ ] Create `/Pages/Account/AccessDenied.razor` (friendly error page)
- [ ] Test: Unauthenticated user accessing /cart redirects to /account/login
- [ ] Test: After login, user redirected back to /cart

**Acceptance Criteria:**
- ✅ Cart page requires authentication
- ✅ Checkout page requires authentication
- ✅ Unauthenticated access redirects to login page with returnUrl
- ✅ After successful login, user redirected to original page
- ✅ Logout redirects to home page

**References:**
- [Blazor Server Authorization](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/server/)
- ADR 0012 (Authentication Strategy)

---

### 4. Replace Stub CustomerId with Session

**Objective:** Remove hardcoded customerId GUIDs and use authenticated user's customerId from claims

**Current Hardcoded Locations:**
- `Cart.razor`: `_cartId = Guid.Parse("22222222-...")`
- `Products.razor`: `_customerId = Guid.Parse("11111111-...")`
- `Checkout.razor`: Similar stub GUIDs

**Tasks:**
- [ ] Create `ICurrentUserService` interface with `GetCustomerId()` method
- [ ] Implement `CurrentUserService` reading from `HttpContext.User.Claims`
- [ ] Register in DI container (scoped lifetime)
- [ ] Update `Cart.razor` to inject `ICurrentUserService` and get customerId
- [ ] Update `Products.razor` to inject `ICurrentUserService` and get customerId
- [ ] Update `Checkout.razor` to inject `ICurrentUserService` and get customerId
- [ ] Update BFF query handlers (GetCartView, GetCheckoutView) to read customerId from claims
- [ ] Remove all hardcoded GUID constants

**Pattern:**
```csharp
@inject ICurrentUserService CurrentUser

@code {
    private Guid _customerId;

    protected override async Task OnInitializedAsync()
    {
        _customerId = await CurrentUser.GetCustomerIdAsync();
        // Fetch cart for authenticated customer
        _cart = await Http.GetFromJsonAsync<CartView>($"/api/carts/{_customerId}");
    }
}
```

**Acceptance Criteria:**
- ✅ All pages use authenticated customerId (no hardcoded GUIDs)
- ✅ Cart shows authenticated user's cart
- ✅ Checkout uses authenticated user's customerId
- ✅ Product "Add to Cart" uses authenticated customerId
- ✅ SSE connection includes authenticated customerId

**Known Challenge:** SSE EventSource doesn't support custom headers. Options:
- **Option A:** Pass customerId as query parameter: `/sse/storefront?customerId={guid}` (current approach)
- **Option B:** Use cookies for SSE authentication (EventSource sends cookies automatically)
- **Option C:** Use SignalR instead of SSE (supports headers)

**Recommendation:** Stick with Option A for now (query parameter). Cookies are more secure but require validating cookie on SSE endpoint.

---

### 5. "Sign In" / "My Account" in AppBar

**Objective:** Add authentication UI to application header (MainLayout.razor)

**Tasks:**
- [ ] Update `Shared/MainLayout.razor` AppBar to show:
  - **If unauthenticated:** "Sign In" button (links to /account/login)
  - **If authenticated:** "My Account" dropdown menu with:
    - User's email/name
    - "Order History" link
    - "Account Settings" link (future)
    - "Sign Out" button
- [ ] Use `<AuthorizeView>` component to conditionally render
- [ ] Style with MudBlazor components (MudMenu, MudMenuItem, MudIconButton)
- [ ] Add user icon (MudIcon Icons.Material.Filled.AccountCircle)

**UI Mockup:**
```
┌─────────────────────────────────────────────────────────┐
│ CritterSupply         [Search]   🛒 (3)  [👤 My Account ▼]│
└─────────────────────────────────────────────────────────┘
                                            ↓ (on click)
                                    ┌──────────────────┐
                                    │ alice@example.com│
                                    ├──────────────────┤
                                    │ Order History    │
                                    │ Sign Out         │
                                    └──────────────────┘
```

**Acceptance Criteria:**
- ✅ Unauthenticated users see "Sign In" button
- ✅ Authenticated users see "My Account" dropdown
- ✅ Dropdown shows user's email
- ✅ "Sign Out" button logs out user
- ✅ UI updates immediately after login/logout (reactive)

**References:**
- [MudBlazor AppBar](https://mudblazor.com/components/appbar)
- [MudBlazor Menu](https://mudblazor.com/components/menu)
- [Blazor AuthorizeView](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/#authorizeview-component)

---

## Testing Strategy

### Integration Tests (Alba)

**Authentication Service Tests:**
- [ ] Test `AuthenticationService.LoginAsync()` with valid credentials
- [ ] Test `AuthenticationService.LoginAsync()` with invalid credentials
- [ ] Test `CurrentUserService.GetCustomerIdAsync()` reads from claims

**Protected Route Tests:**
- [ ] Test `/cart` returns 302 redirect when unauthenticated
- [ ] Test `/cart` returns 200 OK when authenticated
- [ ] Test `/checkout` returns 302 redirect when unauthenticated

**Session Tests:**
- [ ] Test authentication cookie persists across requests
- [ ] Test logout clears authentication cookie

**Note:** May need to use Playwright for Blazor UI testing (defer to Cycle 20 if too complex)

---

### Manual Testing Scenarios

**Scenario 1: Login Flow (Happy Path)**
1. Navigate to `http://localhost:5238`
2. Click "Sign In" in AppBar
3. Enter valid credentials (alice@example.com / password123)
4. Click "Sign In"
5. **Verify:** Redirected to home page
6. **Verify:** AppBar shows "My Account" dropdown with alice@example.com

**Scenario 2: Protected Route Access**
1. Open browser in incognito/private mode
2. Navigate to `http://localhost:5238/cart`
3. **Verify:** Redirected to `/account/login?returnUrl=/cart`
4. Login with valid credentials
5. **Verify:** Redirected back to `/cart` (with authenticated user's cart data)

**Scenario 3: Logout Flow**
1. Login as Alice
2. Navigate to Cart page (should show Alice's cart)
3. Click "My Account" dropdown
4. Click "Sign Out"
5. **Verify:** Redirected to home page
6. **Verify:** AppBar shows "Sign In" button
7. **Verify:** Navigating to `/cart` redirects to login page

**Scenario 4: Session Persistence**
1. Login as Alice
2. Close browser tab
3. Open new tab and navigate to `http://localhost:5238`
4. **Verify:** Still logged in (AppBar shows "My Account")
5. Navigate to `/cart`
6. **Verify:** Shows Alice's cart (no redirect to login)

**Scenario 5: Multi-User Isolation (Real CustomerIds)**
1. Login as Alice in Browser 1
2. Add item to cart
3. **Verify:** Cart badge increments
4. Login as Bob in Browser 2 (different user)
5. **Verify:** Bob's cart is empty (doesn't see Alice's items)
6. Bob adds item to cart
7. **Verify:** Alice's cart unchanged (customer isolation working)

---

## Exit Criteria

**Must Have (Blocking):**
- ✅ ADR 0012 created and approved
- ✅ Login/Logout pages implemented and working
- ✅ Protected routes enforce authentication
- ✅ All hardcoded customerId GUIDs removed
- ✅ Cart, Checkout, Products pages use authenticated customerId
- ✅ AppBar shows authentication state (Sign In / My Account)
- ✅ All manual test scenarios pass
- ✅ Integration tests pass

**Nice to Have (Deferred to Cycle 20+):**
- ⏳ "Remember Me" checkbox (persistent login)
- ⏳ "Forgot Password" flow
- ⏳ Registration page (new customer signup)
- ⏳ Account settings page (change password, update profile)
- ⏳ Email verification
- ⏳ Two-factor authentication (2FA)

---

## Technical Considerations

### Customer Identity BC API Requirements

**Existing Endpoints:**
- `GET /api/customers/{customerId}` ✅ (already implemented)
- `GET /api/customers/by-email/{email}` ✅ (already implemented)

**Missing Endpoints (Need to Add):**
- `POST /api/customers/authenticate` — Validate email + password, return customer data
  - Request: `{ "email": "alice@example.com", "password": "password123" }`
  - Response: `{ "customerId": "guid", "email": "...", "firstName": "...", "lastName": "..." }`
  - Returns 401 Unauthorized if credentials invalid

**Alternative:** Use existing `GET /api/customers/by-email/{email}` and add password field to Customer aggregate. But this requires:
- Adding `PasswordHash` to Customer entity (breaking change)
- Adding password hashing logic (use `BCrypt.Net` or ASP.NET Core Identity PasswordHasher)

**Recommendation:** Add simple password field to Customer entity for now. Production system would use ASP.NET Core Identity or external IdP (Auth0, Azure AD).

---

### Session Storage & Claims

**Claim Structure:**
```csharp
var claims = new List<Claim>
{
    new(ClaimTypes.NameIdentifier, customerId.ToString()),
    new(ClaimTypes.Email, customer.Email),
    new(ClaimTypes.Name, $"{customer.FirstName} {customer.LastName}"),
    new(ClaimTypes.GivenName, customer.FirstName),
    new(ClaimTypes.Surname, customer.LastName)
};

var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

await HttpContext.SignInAsync(
    CookieAuthenticationDefaults.AuthenticationScheme,
    claimsPrincipal,
    new AuthenticationProperties
    {
        IsPersistent = rememberMe, // "Remember Me" checkbox
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
    });
```

**Reading CustomerId from Claims:**
```csharp
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<Guid> GetCustomerIdAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("User is not authenticated");

        var customerIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(customerIdClaim))
            throw new InvalidOperationException("CustomerId claim not found");

        return Task.FromResult(Guid.Parse(customerIdClaim));
    }
}
```

---

### SSE Authentication

**Challenge:** JavaScript `EventSource` doesn't support custom headers (can't send `Authorization: Bearer <token>`)

**Options:**

**Option A: Query Parameter (Current Approach)**
```javascript
const customerId = await getCurrentCustomerId(); // From authenticated session
const eventSource = new EventSource(`/sse/storefront?customerId=${customerId}`);
```

**Pros:** Simple, works with EventSource
**Cons:** CustomerId visible in URL (not sensitive data, but not ideal)

**Option B: Cookies (More Secure)**
```csharp
// SSE endpoint validates authentication cookie
[Authorize]
public async Task Get(CancellationToken cancellationToken)
{
    var customerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    // Stream events...
}
```

**Pros:** Cookies sent automatically by EventSource, more secure
**Cons:** Requires [Authorize] attribute on SSE endpoint, complicates testing

**Option C: SignalR (Alternative to SSE)**
- SignalR supports authentication via cookies or tokens
- More complex setup, but better for production

**Recommendation for Cycle 19:** Use **Option B (Cookies)** — simplest and most secure for Blazor Server. EventSource sends cookies automatically, so no code changes needed in JavaScript.

---

## Risks & Mitigations

**Risk 1: Customer Identity BC Doesn't Store Passwords**
- **Impact:** Can't authenticate users without password storage
- **Mitigation:** Add `PasswordHash` field to Customer entity, use BCrypt for hashing
- **Alternative:** Use stub authentication service for demo (hardcoded credentials)

**Risk 2: SSE Authentication Complexity**
- **Impact:** Real-time updates break after adding authentication
- **Mitigation:** Use cookie-based auth (EventSource sends cookies automatically)
- **Testing:** Manual test with authenticated + unauthenticated browsers

**Risk 3: Blazor Render Mode Issues**
- **Impact:** Authentication state not reactive (UI doesn't update after login)
- **Mitigation:** Use `<CascadingAuthenticationState>` in App.razor
- **Reference:** [Blazor Authentication State](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/server/#expose-the-authentication-state-as-a-cascading-parameter)

**Risk 4: Development Friction (Login Every Time)**
- **Impact:** Slows down development (must login every browser refresh)
- **Mitigation:** Use long-lived cookies in development (7 days), disable authentication in local config
- **Alternative:** Add `DISABLE_AUTH=true` environment variable for development

---

## Open Questions (Decide Before/During Implementation)

1. **Password Storage:**
   - Q: Add password field to Customer entity or use separate Identity BC?
   - Options:
     - A) Add `PasswordHash` to Customer entity (simple, good for demo)
     - B) Create separate Identity BC with ASP.NET Core Identity (production-ready, more complex)
     - C) Use external IdP (Auth0, Azure AD) - future consideration
   - **Recommendation:** Option A for Cycle 19

2. **Password Hashing:**
   - Q: Which library to use?
   - Options:
     - A) `BCrypt.Net-Next` (simple, industry standard)
     - B) ASP.NET Core Identity `PasswordHasher<T>` (more features, tied to Identity)
   - **Recommendation:** Option A (BCrypt.Net-Next)

3. **Registration Page:**
   - Q: Include in Cycle 19 or defer?
   - Options:
     - A) Defer to Cycle 20 (focus on authentication only)
     - B) Add basic registration page (email, password, first/last name)
   - **Recommendation:** Defer to Cycle 20 (use seeded customers for demo)

4. **"Remember Me" Feature:**
   - Q: Include in Cycle 19?
   - Options:
     - A) Yes - add checkbox to login form, extend cookie expiration
     - B) No - always expire after browser close
   - **Recommendation:** Defer to Cycle 20 (not critical for demo)

---

## References

**Related Cycles:**
- [Cycle 16: Customer Experience BC (BFF + Blazor)](./cycle-16-customer-experience.md) — SSE infrastructure
- [Cycle 17: Customer Identity Integration](./cycle-17-customer-identity-integration.md) — Customer CRUD
- [Cycle 18: Customer Experience Phase 2](./cycle-18-customer-experience-phase-2.md) — Real-time updates

**Skills:**
- `docs/skills/bff-realtime-patterns.md` — SSE, EventBroadcaster, Blazor
- `docs/skills/efcore-wolverine-integration.md` — Customer Identity BC patterns

**External References:**
- [ASP.NET Core Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)
- [Blazor Authentication & Authorization](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/)
- [MudBlazor Authorization](https://mudblazor.com/features/authorization)
- [BCrypt.Net](https://github.com/BcryptNet/bcrypt.net)

**APIs:**
- Customer Identity API: `http://localhost:5235`
- Storefront API: `http://localhost:5237`
- Storefront Web: `http://localhost:5238`

---

## Success Metrics

**Development Velocity:**
- Complete cycle in 2-3 sessions (target: 2026-02-25 to 2026-02-28)

**Quality:**
- All integration tests pass
- Manual testing checklist 100% complete
- Zero authentication bypass vulnerabilities

**User Experience:**
- Login flow feels smooth (<2 seconds)
- Protected routes redirect clearly
- Authentication state visible in UI

**Technical Debt:**
- Zero hardcoded GUIDs remaining
- All authentication logic centralized
- No inline `// TODO` comments

---

**Status:** 🚀 In Progress
**Started:** 2026-02-25
**Created:** 2026-02-25
**Author:** Erik Shafer / Claude AI Assistant
