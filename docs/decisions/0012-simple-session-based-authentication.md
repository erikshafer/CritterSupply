# ADR 0012: Simple Session-Based Authentication (Dev-Friendly)

**Status:** ✅ Accepted

**Date:** 2026-02-24

**Context:**

CritterSupply currently uses a hardcoded `customerId` GUID in `Cart.razor` and `Checkout.razor`. To complete the Customer Experience end-to-end flow, we need authentication that:

1. **Demonstrates session-based auth patterns** — Shows how BFF integrates with identity BC
2. **Zero friction for developers** — No OAuth setup, no secret management, no "create an Auth0 account" steps
3. **Demo-friendly** — Stakeholders can explore the system without login walls blocking core features
4. **Reference architecture value** — Pragmatic patterns over production hardening

**Key constraints:**
- CritterSupply is a **reference architecture**, not a production-ready e-commerce platform
- Local development must remain frictionless (no external dependencies)
- Demo presentations (e.g., Thursday Critter Stack talks) should focus on event sourcing/sagas/BFF, not auth complexity
- Must be upgradeable to production-ready auth (ASP.NET Core Identity, OAuth) in future cycles

**Decision:**

Implement **simple session-based authentication** using ASP.NET Core cookie middleware with a **development-mode-first approach**:

1. **No ASP.NET Core Identity framework** — Avoid heavyweight abstractions (`UserManager<T>`, `SignInManager<T>`)
2. **Email + password login** — Password field exists but validation is lenient in dev mode (just check email exists)
3. **Claims-based sessions** — Store `CustomerId` in `ClaimsPrincipal` (idiomatic ASP.NET Core pattern)
4. **Cookie persistence** — Sessions survive browser refreshes
5. **Seeded test users** — Alice, Bob, Charlie pre-seeded via EF Core migration

**Rationale:**

**Why simple session cookies over ASP.NET Core Identity?**
- ✅ **10–20 lines of code** vs 200+ lines with Identity scaffolding
- ✅ **Zero ceremony** — No password hashers, no user stores, no role managers
- ✅ **Clear reference architecture value** — Shows session-based auth pattern without framework bloat
- ✅ **Migration path** — Can upgrade to Identity later (Issue #XX) if needed

**Why show password field but not validate strictly in dev mode?**
- ✅ **Realistic UX** — Login form looks production-like during demos
- ✅ **Simplified validation** — Just check email exists, accept any password (or empty password)
- ✅ **Easy upgrade path** — Add bcrypt hashing later without changing UI

**Why seed test users?**
- ✅ **Frictionless onboarding** — Clone repo → `dotnet run` → login as `alice@critter.test`
- ✅ **Multi-user demos** — Switch between Alice/Bob/Charlie to show cart isolation

**Why NOT OAuth (Auth0, Google, etc.)?**
- ❌ Requires external service setup (breaks "clone and run" promise)
- ❌ Adds complexity without demonstrating core architecture patterns
- ❌ Demo friction (need internet, credentials, callback URL setup)

**Consequences:**

**Positive:**
- ✅ Developer experience: `git clone` → `docker-compose up` → `dotnet run` → login immediately
- ✅ Demo-friendly: No auth walls, no "create account" flow, no forgotten passwords
- ✅ Clear migration path: Add `IPasswordHasher`, add OAuth, swap to Identity framework (all non-breaking)
- ✅ Demonstrates session-based auth (cookies, claims, `[Authorize]` policies)

**Negative:**
- ⚠️ **Not production-ready** — Password validation is lenient (accepts any password in dev mode)
- ⚠️ **No password hashing** — Passwords stored in plaintext (acceptable for reference architecture with seeded test data)
- ⚠️ **No account registration** — Users are pre-seeded (registration flow out of scope)

**Trade-offs accepted:**
- **Security vs simplicity** — We choose simplicity because this is a learning/demo codebase
- **Production patterns vs dev UX** — We choose dev UX but keep migration path open
- **Completeness vs time-to-demo** — Minimal auth gets us to Thursday's presentation

**Alternatives Considered:**

### Alternative 1: ASP.NET Core Identity Framework

**Rejected because:**
- ❌ Too heavyweight for reference architecture goals
- ❌ Requires `UserManager<T>`, `SignInManager<T>`, `IPasswordHasher<T>` ceremony
- ❌ More code to explain to students/developers
- ✅ Could revisit in future cycle (Issue #XX) if demonstrating Identity patterns becomes a goal

### Alternative 2: Username-Only Login (No Password Field)

**Rejected because:**
- ❌ Looks unrealistic during demos ("why is there no password field?")
- ❌ Doesn't demonstrate upgrade path to production auth
- ✅ Could work for internal tools, but e-commerce reference architecture should feel closer to real-world

### Alternative 3: JWT Tokens (Stateless Auth)

**Rejected because:**
- ❌ Overkill for server-side Blazor (no SPA/mobile app consumers)
- ❌ Requires token refresh logic, expiration handling
- ❌ Doesn't fit "session-based" auth pattern (cookies more idiomatic for Blazor Server)
- ✅ Could add later if building mobile app or React SPA

**Implementation Details:**

**1. Add Password Column to Customer Table**

```csharp
// Migration: AddPasswordToCustomer
migrationBuilder.AddColumn<string>(
    name: "Password",
    table: "Customers",
    nullable: true); // Nullable for now, can tighten later
```

**2. Seed Test Users**

```csharp
// In migration Up():
migrationBuilder.InsertData(
    table: "Customers",
    columns: new[] { "Id", "FirstName", "LastName", "Email", "Password" },
    values: new object[,]
    {
        { Guid.NewGuid(), "Alice", "Anderson", "alice@critter.test", "password" },
        { Guid.NewGuid(), "Bob", "Builder", "bob@critter.test", "password" },
        { Guid.NewGuid(), "Charlie", "Chen", "charlie@critter.test", "password" }
    });
```

**3. Login Endpoint (Customer Identity API)**

```csharp
// POST /api/auth/login
app.MapPost("/api/auth/login", async (LoginRequest req, CustomerIdentityDbContext db, HttpContext ctx) =>
{
    var customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == req.Email);

    // Dev mode: Just check email exists (ignore password validation)
    if (customer == null) return Results.Unauthorized();

    // Production upgrade path: Add password hash check here
    // if (!PasswordHasher.Verify(req.Password, customer.PasswordHash)) return Results.Unauthorized();

    var claims = new[] {
        new Claim("CustomerId", customer.Id.ToString()),
        new Claim(ClaimTypes.Email, customer.Email),
        new Claim(ClaimTypes.Name, $"{customer.FirstName} {customer.LastName}")
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(new ClaimsPrincipal(identity));

    return Results.Ok(new { customerId = customer.Id });
});
```

**4. Logout Endpoint**

```csharp
// POST /api/auth/logout
app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync();
    return Results.Ok();
});
```

**5. Blazor Login Page (Storefront.Web)**

```razor
@* Pages/Login.razor *@
@page "/login"
@inject ICustomerIdentityClient CustomerIdentityClient
@inject NavigationManager Navigation
@inject ISnackbar Snackbar

<MudContainer MaxWidth="MaxWidth.Small">
    <MudPaper Elevation="3" Class="pa-6 mt-8">
        <MudText Typo="Typo.h4" GutterBottom>Sign In</MudText>

        <MudTextField @bind-Value="email" Label="Email" Variant="Variant.Outlined" />
        <MudTextField @bind-Value="password" Label="Password" InputType="InputType.Password" Variant="Variant.Outlined" />

        <MudButton Color="Color.Primary" Variant="Variant.Filled" FullWidth OnClick="HandleLogin">
            Sign In
        </MudButton>
    </MudPaper>
</MudContainer>

@code {
    private string email = "";
    private string password = "";

    private async Task HandleLogin()
    {
        var result = await CustomerIdentityClient.LoginAsync(email, password);
        if (result.IsSuccess)
        {
            Navigation.NavigateTo("/");
        }
        else
        {
            Snackbar.Add("Invalid email or password", Severity.Error);
        }
    }
}
```

**6. Protected Routes**

```csharp
// Program.cs (Storefront.Web)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
    });

builder.Services.AddAuthorization();

// In app pipeline:
app.UseAuthentication();
app.UseAuthorization();
```

```razor
@* Cart.razor / Checkout.razor *@
@attribute [Authorize]

@code {
    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    private Guid customerId;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthState!;
        var customerIdClaim = authState.User.FindFirst("CustomerId")?.Value;
        customerId = Guid.Parse(customerIdClaim!);
    }
}
```

**Future Upgrade Path (Out of Scope for Cycle 19):**

1. **Add password hashing** — Use `Microsoft.AspNetCore.Identity.PasswordHasher<Customer>`
2. **Add account registration** — `/api/auth/register` endpoint
3. **Migrate to ASP.NET Core Identity** — Swap to `UserManager<T>` / `SignInManager<T>`
4. **Add OAuth providers** — Google, GitHub sign-in
5. **Add email verification** — Send confirmation emails on registration
6. **Add password reset** — Forgot password flow

**References:**

- Issue [#57](https://github.com/erikshafer/CritterSupply/issues/57) — Replace stub customerId with authentication
- [Customer Identity BC](../../src/Customer%20Identity/Customers/) — EF Core integration
- [Storefront.Web](../../src/Customer%20Experience/Storefront.Web/) — Blazor UI
- [efcore-wolverine-integration.md](../skills/efcore-wolverine-integration.md) — EF Core patterns
- [ADR 0002](./0002-ef-core-for-customer-identity.md) — Why EF Core for Customer Identity BC

**Testing Strategy:**

1. **Manual testing** — Use `.http` files with seeded users
2. **Integration tests** — Alba tests for `/api/auth/login` and `/api/auth/logout`
3. **UI testing** — Manual Blazor testing (automated UI tests deferred to Cycle 20)

**Acceptance Criteria:**

- ✅ Users can log in with `alice@critter.test` (any password accepted)
- ✅ Session persists across browser refreshes
- ✅ Cart and Checkout pages require authentication
- ✅ AppBar shows "Sign In" (when logged out) or "My Account" (when logged in)
- ✅ Logout clears session and redirects to home page
- ✅ `customerId` comes from claims (no hardcoded GUIDs)

**Success Metrics:**

- Zero developer friction: `git clone` → `dotnet run` → login in < 30 seconds
- Demo-ready: Stakeholders can explore cart/checkout without account creation
- Code clarity: < 50 lines of auth code (excluding UI)
