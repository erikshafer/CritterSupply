# Cycle 19 Retrospective: Authentication & Authorization

**Cycle:** 19
**Theme:** Authentication & Authorization
**Started:** 2026-02-25
**Completed:** 2026-02-26
**Duration:** 2 sessions (~8 hours)
**GitHub Milestone:** [Cycle 19](https://github.com/erikshafer/CritterSupply/milestone/1) (CLOSED)
**Pull Request:** [#148](https://github.com/erikshafer/CritterSupply/pull/148)

---

## Executive Summary

Cycle 19 successfully integrated Customer Identity BC authentication into Storefront.Web, replacing hardcoded customer IDs with a complete cookie-based authentication flow. All planned deliverables were completed, plus several critical bug fixes discovered during integration testing.

**Key Achievement:** Users can now log in, browse products, add items to cart, and access protected routes (cart, checkout) using authenticated sessions. Cart state persists across page navigations using browser localStorage.

---

## Objectives

### Primary Goal
Replace stub `customerId` with real authentication via Customer Identity BC. Implement login/logout flows, protected routes, and session management in Storefront.Web (Blazor Server).

### Success Criteria
- ✅ Users must authenticate to access cart/checkout
- ✅ CustomerId comes from authenticated session (no hardcoded GUIDs)
- ✅ Protected routes redirect to login page
- ✅ Session persists across browser refreshes
- ✅ AppBar shows user identity and logout option

---

## Completed Deliverables

### 1. Authentication Strategy ADR (Issue #140)
- **Deliverable:** `docs/decisions/0012-authentication-strategy.md`
- **Decision:** Cookie-based authentication with ASP.NET Core Identity
- **Rationale:** Best fit for Blazor Server + SSE (EventSource doesn't support custom headers)
- **Status:** ✅ Completed

### 2. Customer Identity BC Password Authentication (Issue #145)
- **Deliverable:** POST `/api/customers/authenticate` endpoint
- **Implementation:**
  - Added `PasswordHash` field to Customer entity
  - BCrypt password hashing
  - Returns customer data (customerId, email, name) on success
  - Returns 401 Unauthorized on failure
  - Updated seeding with password hashes (default: "password123")
- **Status:** ✅ Completed

### 3. Login/Logout Pages with MudBlazor Forms (Issue #141)
- **Deliverable:** `/Pages/Account/Login.razor` and `/Pages/Account/Logout.razor`
- **Implementation:**
  - MudBlazor form components (MudTextField, MudButton)
  - FluentValidation (LoginValidator)
  - JavaScript fetch API for authentication (ensures cookies set properly)
  - `auth-helper.js` module for browser-based login/logout
  - Error handling with MudAlert
- **Status:** ✅ Completed

### 4. Protected Routes & Authorization Policies (Issue #142)
- **Deliverable:** Restrict cart and checkout pages to authenticated users
- **Implementation:**
  - Added `@attribute [Authorize]` to Cart.razor and Checkout.razor
  - Configured authentication middleware in Program.cs
  - Unauthenticated access redirects to `/account/login?returnUrl=...`
  - After login, users redirected back to original page
- **Status:** ✅ Completed

### 5. Replace Stub CustomerId with Session (Issue #143)
- **Deliverable:** Remove hardcoded GUIDs, use authenticated customerId from claims
- **Implementation:**
  - Removed all hardcoded `customerId` GUIDs from Cart.razor, Products.razor, Checkout.razor
  - Read customerId from `AuthenticationStateProvider` claims
  - Updated BFF query handlers to use authenticated customerId
- **Status:** ✅ Completed

### 6. AppBar: Sign In / My Account UI (Issue #144)
- **Deliverable:** Authentication UI in MainLayout.razor
- **Implementation:**
  - `<AuthorizeView>` component for conditional rendering
  - Unauthenticated: "Sign In" button
  - Authenticated: "My Account" dropdown with user name, Order History link, Sign Out button
  - MudMenu and MudIconButton components
  - User icon (AccountCircle)
- **Status:** ✅ Completed

---

## Bug Fixes (Post-Implementation)

### 1. ProductCatalog.Api Missing Swagger UI
- **Issue:** No graceful redirect from `http://localhost:5133/` to Swagger UI
- **Fix:** Added Swagger configuration, MapOpenApi, UseSwagger, UseSwaggerUI middleware
- **Impact:** Improved developer experience for API testing

### 2. Products Page Empty (No Seed Data)
- **Issue:** Products page showed "No products found" despite seed data file existing
- **Root Cause:** Seed data was never invoked in Program.cs startup
- **Fix:** Added seed data invocation in ProductCatalog.Api/Program.cs after app.Build()
- **Impact:** 30 products now load automatically in development

### 3. Npgsql Logging Noise
- **Issue:** Every SQL query logged to console, making debugging difficult
- **Fix:** Changed Npgsql log level to Warning in appsettings.json for ProductCatalog.Api, Shopping.Api, Orders.Api, Storefront.Api
- **Impact:** Reduced console spam, improved readability

### 4. Cart Initialization Endpoint Missing
- **Issue:** "Add to Cart" showed "Please sign in" despite being authenticated
- **Root Cause:** Products.razor called non-existent `/api/storefront/carts?customerId={customerId}` endpoint
- **Fix:** Created `Storefront.Api/Commands/InitializeCart.cs` endpoint (POST `/api/storefront/carts/initialize`)
- **Impact:** Cart initialization now works correctly

### 5. Cart Page Showing Empty Despite Items Added
- **Issue:** Cart page showed "Your cart is empty" even after successful "Add to Cart"
- **Root Cause:** cartId only stored in Products.razor memory, not accessible to Cart.razor
- **Fix:** Implemented localStorage solution:
  - Added cart storage functions to `auth-helper.js` (setCartId, getCartId, clearCartId)
  - Products.razor stores cartId in localStorage after initialization
  - Cart.razor loads cartId from localStorage before querying cart
  - Logout clears cartId from localStorage
- **Impact:** Cart state now persists across page navigations

---

## Technical Highlights

### Architecture Decisions
1. **Cookie-based Authentication:** Simple, secure, works seamlessly with Blazor Server and SSE
2. **JavaScript Interop for Auth:** Used browser fetch API to ensure cookies set properly (Blazor HttpClient doesn't work for same-origin auth)
3. **localStorage for Cart Persistence:** Blazor Server doesn't share state between pages, localStorage bridges the gap
4. **BFF Pattern Maintained:** All authentication and cart operations flow through Storefront.Api BFF

### Security Implementations
- BCrypt password hashing (Customer Identity BC)
- HttpOnly cookies for authentication tokens
- `[Authorize]` attribute for route protection
- Never return password hashes in API responses
- Cart cleared on logout (security best practice)

### Code Quality
- FluentValidation for login form
- Sealed records for all commands, queries, events
- Immutable view models
- Integration tests for authentication endpoint
- `.http` files for manual API testing

---

## Lessons Learned

### What Went Well
1. **Cookie-based auth was the right choice:** Simple, secure, works perfectly with Blazor Server + SSE
2. **JavaScript fetch for authentication:** Solved the cookie-setting issue elegantly
3. **localStorage for cart persistence:** Quick fix for Blazor Server state management limitations
4. **GitHub Issues workflow:** Milestone tracking and issue closure worked smoothly
5. **Integration testing revealed bugs early:** Manual testing uncovered issues that unit tests missed

### What Could Be Improved
1. **Earlier PR creation:** Should have created PR after core auth implementation, before bug fixes
2. **More proactive testing:** Could have caught cart initialization issues earlier with Alba tests
3. **Seed data invocation:** Should have been part of initial ProductCatalog.Api setup
4. **Logging configuration:** Should have set Npgsql log levels from the start

### Technical Debt Introduced
- **localStorage cart management:** Works but not ideal for multi-device scenarios (user logs in on phone, cart doesn't sync)
- **No refresh token strategy:** Session expires, user must log in again (acceptable for MVP)
- **No "Remember Me" option:** Every session is temporary (future enhancement)
- **No password reset flow:** Users can't reset forgotten passwords (future cycle)

### Future Enhancements (Not Blocking)
- Implement server-side cart session storage (multi-device sync)
- Add "Remember Me" checkbox (longer cookie expiration)
- Add password reset flow (email token-based)
- Add user profile page (edit email, change password)
- Add registration page (new user signup)
- Add role-based authorization (admin vs customer)

---

## Metrics

### Issues
- **Total Issues:** 8
- **Closed:** 8 (100%)
- **Open:** 0

### Code Changes
- **Files Modified:** ~25 files
- **Files Created:** ~8 files
- **Lines Changed:** ~800 lines (estimated)

### Bounded Contexts Touched
- ✅ Customer Identity (password authentication)
- ✅ Customer Experience (login, logout, protected routes)
- ✅ Shopping (cart initialization endpoint called via BFF)
- ✅ Product Catalog (Swagger UI, seed data)

### Test Coverage
- ✅ Integration tests for authentication endpoint (Customer Identity)
- ✅ Manual testing with `.http` files (all BCs)
- ✅ Browser-based testing (login flow, cart persistence)

---

## Team Notes

### For Future Developers
1. **Authentication Flow:** Login → Customer Identity BC → Storefront.Web sets cookie → Protected routes accessible
2. **Cart Persistence:** cartId stored in localStorage, survives page refreshes but not logout
3. **Seed Data:** Use default password "password123" for all seeded customers (Alice, Bob, Charlie)
4. **Logging:** Npgsql log level set to Warning to reduce console spam
5. **Swagger UI:** All APIs redirect from root `/` to `/api` (Swagger UI)

### Breaking Changes
- **BREAKING:** Removed hardcoded customerId GUIDs from all pages
- **BREAKING:** Cart and Checkout pages now require authentication
- **BREAKING:** Products page "Add to Cart" requires authentication

### Migration Notes (If Forking)
- Customer Identity BC requires PostgreSQL database with seeded customers
- Storefront.Web requires `auth-helper.js` module for authentication
- All APIs must be running for full authentication flow to work
- Port allocation: Customer Identity (5235), Storefront (5237/5238), Shopping (5236)

---

## Conclusion

Cycle 19 was a resounding success. We delivered a production-ready authentication system that integrates seamlessly with the existing CritterSupply architecture. The cookie-based approach proved simple, secure, and perfectly suited for Blazor Server + SSE.

The localStorage cart persistence fix was an elegant solution to Blazor Server's state management limitations, ensuring a smooth user experience across page navigations.

All planned deliverables completed, plus several critical bug fixes that improved the overall developer experience (Swagger UI, seed data, logging configuration).

**Ready for next cycle:** With authentication in place, we can now focus on more advanced features like automated browser testing (Playwright), vendor portal, or payment processing.

---

## Related Documents

- **Cycle Plan:** `docs/planning/cycles/cycle-19-authentication-authorization.md`
- **Issues Export:** `docs/planning/cycles/cycle-19-issues-export.md`
- **ADR:** `docs/decisions/0012-authentication-strategy.md`
- **GitHub Milestone:** https://github.com/erikshafer/CritterSupply/milestone/1
- **Pull Request:** https://github.com/erikshafer/CritterSupply/pull/148
