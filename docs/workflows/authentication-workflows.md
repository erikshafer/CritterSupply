# Authentication Workflows (Customer Experience BC)

**Feature:** Customer Authentication & Authorization  
**Implementation Status:** 📋 Planned (Cycle 19)  
**Priority:** High (Next Cycle)  
**Estimated Effort:** 2-3 sessions  

---

## Overview

Replace stub `customerId` in Customer Experience BC (Storefront) with real authentication integrated with Customer Identity BC. This enables secure customer sessions, protected routes, and personalized experiences.

### Key Business Rules

1. **Cookie-Based Authentication:** Use ASP.NET Core Identity cookies (simpler than JWT for web apps)
2. **Session Timeout:** 2 hours idle, 24 hours absolute
3. **Protected Routes:** Cart, Checkout, Order History require authentication
4. **Anonymous Browsing:** Product listings, product details accessible without login
5. **Persistent Cart:** Anonymous cart merged with customer cart after login
6. **Remember Me:** Optional "Remember Me" checkbox extends session to 30 days

---

## Workflows

### Workflow 1: Happy Path - Customer Registration

**Scenario:** New customer creates account to place order

```
1. Customer: Browse Product Listing (No Login Required)
   - Navigate to homepage
   - View products (anonymous browsing allowed)
   - Add items to cart (anonymous cart, stored in session/localStorage)

2. Customer: Proceed to Checkout
   - Click "Proceed to Checkout"
   - System detects: User not authenticated
   - Redirect to: /login?returnUrl=/checkout

3. Customer: Choose to Register
   - Click "Create Account" tab
   - Form displays: Email, Password, Confirm Password, First Name, Last Name

4. Customer: Submit Registration Form
   - Email: "alice@example.com"
   - Password: "SecurePass123!"
   - First Name: "Alice"
   - Last Name: "Johnson"

   POST /api/customers (to Customer Identity BC)
   Command: CreateCustomer
     - Email: "alice@example.com"
     - PasswordHash: [bcrypt hashed]
     - FirstName: "Alice"
     - LastName: "Johnson"
   Event: Customer.CustomerCreated

5. System: Auto-Login After Registration
   - Create authentication cookie (customerId stored in claims)
   - Set session timeout (2 hours idle, 24 hours absolute)
   - Redirect to: /checkout (returnUrl from step 2)

6. System: Merge Anonymous Cart
   - Query: Get anonymous cart from session (3 items)
   - Command: InitializeCart (with authenticated customerId)
   - Command: AddItemToCart (for each anonymous cart item)
   - Clear anonymous cart session

7. Customer: Complete Checkout
   - Now authenticated → customerId available for order placement
   - Proceed through checkout wizard (4 steps)
   - Place order successfully

TOTAL DURATION: 5 minutes (registration + checkout)
BUSINESS IMPACT: Seamless transition from anonymous to authenticated shopping
```

---

### Workflow 2: Customer Login (Existing Account)

**Scenario:** Returning customer logs in to view order history

```
1. Customer: Click "Sign In" Button
   - Navigate to /login

2. Customer: Enter Credentials
   - Email: "alice@example.com"
   - Password: "SecurePass123!"
   - Check "Remember Me" (optional)

3. System: Validate Credentials
   POST /api/customers/authenticate (to Customer Identity BC)
   Command: AuthenticateCustomer
     - Email: "alice@example.com"
     - Password: "SecurePass123!"
   
   - Customer Identity BC validates password (bcrypt compare)
   - If valid → Return customerId
   - If invalid → Return error: "Invalid email or password"

4. System: Create Authentication Cookie
   - Cookie name: ".AspNetCore.Cookies"
   - Claims:
     - sub (subject): customerId (Guid)
     - email: "alice@example.com"
     - name: "Alice Johnson"
   - Cookie expiry:
     - If "Remember Me" checked: 30 days
     - If NOT checked: Session cookie (2 hours idle)

5. Customer: Redirect to Homepage
   - Display: "Welcome back, Alice!"
   - Show "My Account" dropdown (replaces "Sign In" button)
   - Display cart badge with item count (if cart exists)

6. Customer: Navigate to Order History
   - Click "My Account" → "Order History"
   - GET /api/orders?customerId={customerId}
   - System reads customerId from authentication cookie claims
   - Display table of past orders

TOTAL DURATION: 30 seconds (login + redirect)
BUSINESS IMPACT: Personalized experience, easy access to account features
```

---

### Workflow 3: Protected Routes (Authorization)

**Scenario:** Unauthenticated user attempts to access cart

```
1. Anonymous User: Click "Cart" Badge
   - Navigate to /cart

2. System: Authorization Check
   - Blazor page: @attribute [Authorize]
   - System checks: Is user authenticated?
   - Result: NO (no authentication cookie)

3. System: Redirect to Login
   - Redirect to: /login?returnUrl=/cart
   - Display message: "Please sign in to view your cart"

4. User: Sign In
   - Enter credentials
   - Authentication succeeds

5. System: Redirect to Original Destination
   - Read returnUrl query parameter: /cart
   - Redirect to /cart
   - User can now view cart (authenticated)

BUSINESS IMPACT: Protects customer data, ensures only authenticated users access personalized features
```

---

### Workflow 4: Customer Logout

**Scenario:** Customer finishes shopping and logs out

```
1. Customer: Click "My Account" → "Sign Out"
   - Navigate to /logout

2. System: Clear Authentication Cookie
   POST /api/customers/logout
   - Delete ".AspNetCore.Cookies" cookie
   - Clear server-side session data

3. System: Redirect to Homepage
   - Display: "You have been signed out"
   - Show "Sign In" button (replaces "My Account" dropdown)

4. Customer: Cart Behavior After Logout
   Option A: Preserve cart in anonymous session
     - Cart items remain in localStorage
     - Customer can continue browsing/shopping anonymously
   
   Option B: Clear cart on logout (security-sensitive)
     - Cart cleared
     - Customer starts fresh if they log back in

BUSINESS RECOMMENDATION: Option A (preserve cart) for better UX, unless security requirements dictate otherwise
```

---

### Workflow 5: Edge Case - Session Timeout (Idle)

**Scenario:** Customer logged in but inactive for 2 hours

```
1. Customer: Log In at 10:00 AM
   - Authentication cookie created
   - Idle timeout: 2 hours (expires at 12:00 PM)

2. Customer: Add Items to Cart at 10:15 AM
   - Cart operations successful
   - Session still valid

3. Customer: Leave Browser Open (Inactive)
   - Time: 10:15 AM → 12:00 PM (1 hour 45 minutes pass)

4. Customer: Return at 12:30 PM (2+ Hours Idle)
   - Click "Checkout"
   - System checks authentication cookie: EXPIRED

5. System: Redirect to Login
   - Display message: "Your session has expired due to inactivity. Please sign in again."
   - Redirect to: /login?returnUrl=/checkout
   - Cart data preserved (stored in database, not session)

6. Customer: Re-Login
   - Enter credentials
   - Session restored
   - Cart still intact (loaded from database by customerId)

BUSINESS IMPACT: Security (auto-logout after inactivity) balanced with UX (cart preserved)
```

---

### Workflow 6: Edge Case - Absolute Session Timeout

**Scenario:** Customer logged in for 24 hours (absolute timeout)

```
1. Customer: Log In at 8:00 AM Monday
   - Authentication cookie created
   - Absolute timeout: 24 hours (expires at 8:00 AM Tuesday)
   - Idle timeout: 2 hours (refreshed on each activity)

2. Customer: Actively Shopping Throughout Day
   - 8:00 AM: Log in
   - 10:00 AM: Browse products (idle timeout refreshed → 12:00 PM)
   - 11:30 AM: Add items to cart (idle timeout refreshed → 1:30 PM)
   - 1:00 PM: View product details (idle timeout refreshed → 3:00 PM)
   - ...continues throughout day

3. Customer: Still Logged In at 7:00 AM Tuesday
   - Last activity: 6:00 AM (idle timeout would expire at 8:00 AM)
   - Absolute timeout: 8:00 AM Tuesday

4. Customer: Attempt Action at 8:15 AM Tuesday
   - Click "Checkout"
   - System checks: Absolute timeout exceeded (24 hours since login)
   - Cookie invalidated

5. System: Force Logout
   - Display message: "For security, your session has expired after 24 hours. Please sign in again."
   - Redirect to /login?returnUrl=/checkout

SECURITY RATIONALE: Prevents indefinitely long sessions even with continuous activity (protects against session hijacking)
```

---

### Workflow 7: Anonymous Cart Merge After Login

**Scenario:** Customer adds items to anonymous cart, then logs in

```
1. Anonymous Customer: Add Items to Cart
   - No authentication
   - Cart stored in:
     Option A: Browser localStorage (client-side only)
     Option B: Anonymous session with temporary GUID (server-side)

   Cart Contents:
     - DOG-BOWL-01 (Qty: 2)
     - CAT-TOY-05 (Qty: 1)

2. Customer: Click "Sign In"
   - Navigate to /login

3. Customer: Log In
   - Email: "alice@example.com"
   - Authentication succeeds
   - CustomerId: "customer-abc-123"

4. System: Check for Existing Customer Cart
   Query: GET /api/carts/customer/customer-abc-123
   Result: Customer already has a cart with items:
     - DOG-FOOD-01 (Qty: 1)

5. System: Merge Anonymous Cart with Customer Cart
   Strategy: Combine line items, sum quantities for duplicate SKUs

   Anonymous Cart:
     - DOG-BOWL-01 (Qty: 2)
     - CAT-TOY-05 (Qty: 1)

   Customer Cart (before merge):
     - DOG-FOOD-01 (Qty: 1)

   Merged Cart (after merge):
     - DOG-BOWL-01 (Qty: 2) [from anonymous]
     - CAT-TOY-05 (Qty: 1) [from anonymous]
     - DOG-FOOD-01 (Qty: 1) [from customer]

   Commands:
     - AddItemToCart (cartId: customer-abc-123, Sku: DOG-BOWL-01, Qty: 2)
     - AddItemToCart (cartId: customer-abc-123, Sku: CAT-TOY-05, Qty: 1)

6. System: Clear Anonymous Cart
   - Delete anonymous cart from localStorage/session
   - Redirect to homepage

7. Customer: View Cart Badge
   - Cart badge shows: 4 items (2 + 1 + 1)

BUSINESS IMPACT: Seamless shopping experience, no lost cart items during login
```

---

## Integration Flows

### Customer Experience BC Changes

**New API Endpoints:**
- `POST /api/customers/authenticate` — Login (delegates to Customer Identity BC)
- `POST /api/customers/logout` — Logout (clears authentication cookie)
- `GET /api/customers/me` — Get current user profile (from cookie claims)

**Blazor Pages Changes:**
- Add `@attribute [Authorize]` to protected pages:
  - `Cart.razor`
  - `Checkout.razor`
  - `OrderHistory.razor`
- Add `@attribute [AllowAnonymous]` to public pages:
  - `Index.razor` (homepage)
  - `ProductListing.razor`
  - `ProductDetails.razor` (future)
  - `Login.razor`
  - `Register.razor`

**AppBar.razor Changes:**
- Add authentication state check:
  ```razor
  <AuthorizeView>
      <Authorized>
          <MudMenu Label="@context.User.Identity?.Name">
              <MudMenuItem Href="/orders">Order History</MudMenuItem>
              <MudMenuItem Href="/account">My Account</MudMenuItem>
              <MudMenuItem Href="/logout">Sign Out</MudMenuItem>
          </MudMenu>
      </Authorized>
      <NotAuthorized>
          <MudButton Href="/login" Color="Color.Primary">Sign In</MudButton>
      </NotAuthorized>
  </AuthorizeView>
  ```

---

## Implementation Guidance

### Program.cs Configuration

```csharp
// Add authentication services
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(2); // Idle timeout
        options.SlidingExpiration = true; // Refresh on activity
        options.Cookie.MaxAge = TimeSpan.FromHours(24); // Absolute timeout
        options.Cookie.HttpOnly = true; // Prevent XSS attacks
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // HTTPS only
        options.Cookie.SameSite = SameSiteMode.Strict; // CSRF protection
    });

builder.Services.AddAuthorization();

// In middleware pipeline
app.UseAuthentication();
app.UseAuthorization();
```

### Login Page (Login.razor)

```razor
@page "/login"
@using System.Security.Claims
@inject ICustomerIdentityClient CustomerIdentityClient
@inject NavigationManager Navigation
@inject AuthenticationStateProvider AuthStateProvider

<MudContainer MaxWidth="MaxWidth.Small" Class="mt-8">
    <MudPaper Elevation="4" Class="pa-8">
        <MudText Typo="Typo.h4" GutterBottom>Sign In</MudText>
        
        <EditForm Model="@model" OnValidSubmit="OnValidSubmit">
            <DataAnnotationsValidator />
            
            <MudTextField @bind-Value="model.Email" Label="Email" InputType="InputType.Email" />
            <MudTextField @bind-Value="model.Password" Label="Password" InputType="InputType.Password" />
            <MudCheckBox @bind-Checked="model.RememberMe" Label="Remember Me" />
            
            @if (!string.IsNullOrEmpty(errorMessage))
            {
                <MudAlert Severity="Severity.Error">@errorMessage</MudAlert>
            }
            
            <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary" FullWidth>
                Sign In
            </MudButton>
        </EditForm>
        
        <MudText Class="mt-4">
            Don't have an account? <MudLink Href="/register">Create Account</MudLink>
        </MudText>
    </MudPaper>
</MudContainer>

@code {
    private LoginModel model = new();
    private string? errorMessage;
    
    [Parameter]
    [SupplyParameterFromQuery]
    public string? ReturnUrl { get; set; }
    
    private async Task OnValidSubmit()
    {
        errorMessage = null;
        
        // Call Customer Identity BC to validate credentials
        var result = await CustomerIdentityClient.AuthenticateAsync(model.Email, model.Password);
        
        if (result.Success)
        {
            // Create authentication cookie
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, result.CustomerId.ToString()),
                new Claim(ClaimTypes.Email, model.Email),
                new Claim(ClaimTypes.Name, result.FullName)
            };
            
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe 
                    ? DateTimeOffset.UtcNow.AddDays(30) 
                    : DateTimeOffset.UtcNow.AddHours(2)
            };
            
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, 
                principal, 
                authProperties);
            
            // Notify Blazor auth state has changed
            await ((CustomAuthStateProvider)AuthStateProvider).NotifyUserAuthentication(model.Email);
            
            // Redirect to return URL or homepage
            Navigation.NavigateTo(ReturnUrl ?? "/");
        }
        else
        {
            errorMessage = result.ErrorMessage ?? "Invalid email or password";
        }
    }
    
    public class LoginModel
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }
}
```

### Customer Identity BC Authentication Handler

```csharp
public sealed record AuthenticateCustomer(string Email, string Password);

public sealed record AuthenticationResult(
    bool Success,
    Guid CustomerId,
    string FullName,
    string? ErrorMessage
);

public static class AuthenticateCustomerHandler
{
    public static async Task<AuthenticationResult> Handle(
        AuthenticateCustomer command,
        CustomerIdentityDbContext dbContext,
        IPasswordHasher passwordHasher)
    {
        // Look up customer by email
        var customer = await dbContext.Customers
            .FirstOrDefaultAsync(c => c.Email == command.Email);
        
        if (customer is null)
            return new AuthenticationResult(false, Guid.Empty, string.Empty, "Invalid email or password");
        
        // Verify password
        if (!passwordHasher.Verify(command.Password, customer.PasswordHash))
            return new AuthenticationResult(false, Guid.Empty, string.Empty, "Invalid email or password");
        
        // Return customer info
        return new AuthenticationResult(
            true, 
            customer.Id, 
            $"{customer.FirstName} {customer.LastName}", 
            null);
    }
}
```

---

## Testing Strategy

### Integration Tests (Alba + TestContainers)

1. **Registration:**
   - Submit registration form → Customer created in DB, authentication cookie set
   - Verify cookie contains correct claims (customerId, email, name)

2. **Login:**
   - Valid credentials → Authentication cookie set, redirect to returnUrl
   - Invalid credentials → Error message displayed, no cookie set
   - "Remember Me" checked → Cookie expiry 30 days
   - "Remember Me" NOT checked → Session cookie (2 hours)

3. **Protected Routes:**
   - Unauthenticated user navigates to /cart → Redirect to /login?returnUrl=/cart
   - Authenticated user navigates to /cart → Page loads successfully

4. **Logout:**
   - Click "Sign Out" → Cookie cleared, redirect to homepage
   - Attempt to access protected route after logout → Redirect to login

5. **Anonymous Cart Merge:**
   - Add items to anonymous cart, log in → Items merged with customer cart
   - Verify no duplicate SKUs (quantities summed)

### Manual Testing Checklist

1. **Registration Flow:**
   - [ ] Create new account
   - [ ] Auto-login after registration
   - [ ] Redirect to checkout
   - [ ] Anonymous cart merged

2. **Login Flow:**
   - [ ] Valid credentials → Successful login
   - [ ] Invalid credentials → Error message
   - [ ] "Remember Me" → Cookie persists 30 days
   - [ ] returnUrl honored after login

3. **Protected Routes:**
   - [ ] /cart requires authentication
   - [ ] /checkout requires authentication
   - [ ] /orders requires authentication
   - [ ] / (homepage) allows anonymous

4. **Logout:**
   - [ ] "Sign Out" clears session
   - [ ] Protected routes redirect to login after logout

5. **Session Timeout:**
   - [ ] Idle 2 hours → Session expires
   - [ ] Absolute 24 hours → Force logout

---

## BDD Feature Files

Location: `docs/features/customer-experience/`

**authentication.feature:**

```gherkin
Feature: Customer Authentication
  As a customer
  I want to create an account and sign in
  So that I can place orders and track my order history

  Background:
    Given the Customer Identity BC is running
    And the Customer Experience BC is running

  Scenario: New customer registration
    Given I am not logged in
    When I navigate to the registration page
    And I enter the following details:
      | Field            | Value                  |
      | Email            | alice@example.com      |
      | Password         | SecurePass123!         |
      | Confirm Password | SecurePass123!         |
      | First Name       | Alice                  |
      | Last Name        | Johnson                |
    And I submit the registration form
    Then I should be automatically logged in
    And I should see "Welcome, Alice!" in the navigation bar
    And my session should be authenticated

  Scenario: Existing customer login
    Given a customer exists with email "alice@example.com" and password "SecurePass123!"
    When I navigate to the login page
    And I enter email "alice@example.com" and password "SecurePass123!"
    And I submit the login form
    Then I should be logged in successfully
    And I should see "Welcome back, Alice!" message

  Scenario: Protected route redirects to login
    Given I am not logged in
    When I navigate to "/cart"
    Then I should be redirected to "/login?returnUrl=/cart"
    And I should see a message "Please sign in to view your cart"

  Scenario: Successful login redirects to return URL
    Given I am not logged in
    And I attempted to access "/checkout" (redirected to login)
    When I log in with valid credentials
    Then I should be redirected to "/checkout"

  Scenario: Anonymous cart merged after login
    Given I am not logged in
    And I have added "DOG-BOWL-01" (Qty: 2) to my anonymous cart
    And I have added "CAT-TOY-05" (Qty: 1) to my anonymous cart
    And a customer "alice@example.com" exists with a cart containing "DOG-FOOD-01" (Qty: 1)
    When I log in as "alice@example.com"
    Then my cart should contain:
      | SKU          | Quantity |
      | DOG-BOWL-01  | 2        |
      | CAT-TOY-05   | 1        |
      | DOG-FOOD-01  | 1        |
    And my anonymous cart should be cleared

  Scenario: Customer logout
    Given I am logged in as "alice@example.com"
    When I click "Sign Out" in the navigation menu
    Then I should be logged out
    And I should see "Sign In" button (not "My Account")
    And I should not have access to protected routes
```

---

## Dependencies

**Must Be Implemented First:**
- ✅ Customer Identity BC (for authentication validation)
- ✅ Customer Experience BC (Storefront.Web Blazor app)

**Integrates With:**
- Shopping BC (for cart merge after login)
- Orders BC (for authenticated order placement)

---

## Estimated Implementation Effort

**Session 1:** Authentication Foundation
- ASP.NET Core Cookie Authentication configuration
- Login.razor + Register.razor pages
- Customer Identity BC authentication handler
- Integration tests for login/logout

**Session 2:** Protected Routes & Authorization
- `[Authorize]` attributes on Cart, Checkout, OrderHistory pages
- AuthenticationStateProvider for Blazor
- ReturnUrl support
- Integration tests for authorization

**Session 3:** Anonymous Cart Merge & Polish
- Cart merge logic after login
- Session timeout configuration (idle + absolute)
- "Remember Me" functionality
- Manual testing + bug fixes
- BDD feature files

**Total Effort:** 2-3 sessions (4-6 hours)

---

## Success Criteria

- [ ] Customers can register new accounts
- [ ] Customers can log in with valid credentials
- [ ] Invalid credentials display error message
- [ ] Protected routes redirect to login
- [ ] returnUrl honored after successful login
- [ ] "Remember Me" extends session to 30 days
- [ ] Session timeout (idle + absolute) working
- [ ] Anonymous cart merged with customer cart after login
- [ ] Customers can log out successfully
- [ ] All 10+ integration tests passing
- [ ] BDD feature file written (authentication.feature)
- [ ] Manual testing checklist completed

---

**Document Owner:** Product Owner (Erik Shafer)  
**Last Updated:** 2026-02-18  
**Status:** 🟢 Ready for Implementation (Cycle 19)
