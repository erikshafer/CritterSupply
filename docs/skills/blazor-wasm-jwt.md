# Blazor WASM + JWT: Patterns and Gotchas

Comprehensive guide for building Blazor WebAssembly frontends with JWT authentication in CritterSupply — covering in-memory token storage, named HTTP clients, SignalR `AccessTokenProvider`, background refresh timers, RBAC, and key differences from Blazor Server.

## When to Use This Skill

**Read this skill when:**
- Building `VendorPortal.Web` or any future Blazor WASM frontend
- Implementing JWT Bearer authentication in a WASM context
- Configuring cross-origin API calls from WASM
- Wiring SignalR to a JWT-authenticated hub from WASM

**CritterSupply WASM Projects:**
- `VendorPortal.Web` (port 5241) — the reference Blazor WASM implementation
- `VendorPortal.Api` (port 5239) — the JWT-protected API and SignalR hub it talks to
- `VendorIdentity.Api` (port 5240) — the JWT issuer (login, refresh, logout)

---

## WASM vs Blazor Server: The Key Differences

| Dimension | Storefront.Web (Blazor Server) | VendorPortal.Web (Blazor WASM) |
|---|---|---|
| Hosting | Kestrel process (.NET on server) | Static files from Nginx (browser runs .NET WASM) |
| Auth mechanism | Session cookies (server-managed) | JWT in WASM memory + refresh in HttpOnly cookie |
| HttpContext | Available (server-side) | ❌ NOT available — no server-side context |
| `IHostedService` | ✅ Works | ❌ Not supported — use `System.Threading.Timer` |
| Hub connection | 2 (Blazor circuit + SignalR hub) | 1 (SignalR hub only, no circuit) |
| `AddAuthorization()` | Server pattern | Use `AddAuthorizationCore()` instead |
| Page reload | State preserved (server circuit) | ⚠️ Auth state lost — must re-login or refresh |
| HTTP client setup | Named clients work with server base | Must set `BaseAddress` explicitly (cross-origin) |
| JWT in SignalR | Cookie or header (server-to-server) | `AccessTokenProvider` lambda (query string) |

---

## Project SDK

```xml
<!-- VendorPortal.Web.csproj -->
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MudBlazor" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" />
  </ItemGroup>
</Project>
```

> **GOTCHA:** WASM uses `Microsoft.NET.Sdk.BlazorWebAssembly`, NOT `Microsoft.NET.Sdk.Web`.
> The Web SDK is for server-side projects (Blazor Server, APIs). Using the wrong SDK will either
> fail to build the WASM bundle or add unnecessary server-side dependencies.

---

## Entry Point: `Program.cs`

```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Named HttpClients for cross-origin API calls
var identityApiUrl = builder.Configuration["ApiClients:VendorIdentityApiUrl"] ?? "http://localhost:5240";
var portalApiUrl   = builder.Configuration["ApiClients:VendorPortalApiUrl"]   ?? "http://localhost:5239";

builder.Services.AddHttpClient("VendorIdentityApi", c => c.BaseAddress = new Uri(identityApiUrl));
builder.Services.AddHttpClient("VendorPortalApi",   c => c.BaseAddress = new Uri(portalApiUrl));

builder.Services.AddMudServices();

// Auth — in-memory only (no server session)
builder.Services.AddSingleton<VendorAuthState>();
builder.Services.AddScoped<VendorAuthService>();
builder.Services.AddSingleton<TokenRefreshService>();

// WASM-specific: AddAuthorizationCore (NOT AddAuthorization)
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, VendorAuthStateProvider>();

builder.Services.AddSingleton<VendorHubService>();

await builder.Build().RunAsync();
```

---

## GOTCHA Collection: Named HTTP Clients

**Problem:** The default `HttpClient` in WASM uses the app's own base address (`http://localhost:5241`). A call to `/api/vendor-portal/dashboard` will go to the WASM static file server — a 404.

**Solution:** Always register named clients with explicit `BaseAddress`:

```csharp
// WRONG — resolves to http://localhost:5241/api/...
builder.Services.AddHttpClient("VendorPortalApi");

// RIGHT — resolves to http://localhost:5239/api/...
builder.Services.AddHttpClient("VendorPortalApi", c =>
    c.BaseAddress = new Uri("http://localhost:5239"));
```

**Usage in components/services:**

```csharp
public sealed class VendorAuthService(IHttpClientFactory httpClientFactory)
{
    public async Task<bool> LoginAsync(string email, string password)
    {
        var client = httpClientFactory.CreateClient("VendorIdentityApi");
        // client.BaseAddress is now http://localhost:5240
        var response = await client.PostAsJsonAsync("/api/vendor-identity/auth/login", ...);
        // ...
    }
}
```

---

## GOTCHA Collection: Cross-Origin Cookies (Refresh Token)

The refresh token is stored in an HttpOnly cookie on `VendorIdentity.Api` (port 5240). The WASM app runs at port 5241. This is cross-origin.

**Three things must ALL be true for the cookie to work:**

1. **Server (VendorIdentity.Api):** CORS policy with `AllowCredentials()`:
```csharp
policy.WithOrigins("http://localhost:5241")
      .AllowAnyHeader()
      .AllowAnyMethod()
      .AllowCredentials(); // Without this, browser strips the cookie
```

2. **Server (VendorIdentity.Api):** Cookie must not be `Secure=true` on HTTP in development:
```csharp
new CookieOptions
{
    HttpOnly = true,
    SameSite = SameSiteMode.Strict,
    Secure = false, // dev only — set true for HTTPS production
}
```

3. **Client (WASM):** The browser `fetch` used by Blazor WASM defaults to `credentials: "same-origin"`, so cookies are **not** sent on cross-origin calls (e.g., `http://localhost:5241` → `http://localhost:5239`) unless you explicitly opt in. Configure the WASM `HttpClient`'s underlying handler to include credentials:

```csharp
// In Program.cs — configure the named client's handler to send cookies cross-origin
builder.Services.AddHttpClient("VendorIdentityApi", c => c.BaseAddress = new Uri(identityApiUrl))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // In Blazor WASM, use WebAssemblyHttpHandler and set DefaultBrowserRequestCredentials.
        // The line below is the Blazor WASM equivalent of fetch credentials: "include"
    });

// Or set it globally on the WebAssemblyHttpHandler options:
builder.Services.Configure<WebAssemblyHttpHandlerOptions>(options =>
    options.DefaultBrowserRequestCredentials = BrowserRequestCredentials.Include);
```

> **Production note:** With HTTPS (both client and server), set `Secure = true` and choose `SameSite = Lax` or `None` as appropriate for your cross-site/cross-origin requirements. Named clients do NOT automatically switch fetch credentials to `include` — this must be configured explicitly on the client side.

---

## GOTCHA Collection: `AddAuthorizationCore` vs `AddAuthorization`

```csharp
// Blazor Server (server-side, full middleware stack available)
builder.Services.AddAuthorization();

// Blazor WASM (no server pipeline — only the core policies engine)
builder.Services.AddAuthorizationCore();
```

`AddAuthorization()` pulls in ASP.NET Core middleware infrastructure that is NOT available in WASM. Using it will compile but throw at runtime.

---

## In-Memory JWT Storage Pattern

**Never** store access tokens in `localStorage` or `sessionStorage` — they are accessible to any JavaScript on the page (XSS attack vector).

**The correct pattern:** Store the access token in a C# singleton (`VendorAuthState`) that lives in WASM memory. JavaScript cannot access C# memory directly.

```csharp
// VendorAuthState.cs — singleton in WASM DI
public sealed class VendorAuthState
{
    public string? AccessToken { get; private set; }   // in WASM memory
    public bool IsAuthenticated { get; private set; }
    public event Action? OnChange;

    public void SetAuthenticated(string accessToken, ...) { ... OnChange?.Invoke(); }
    public void ClearAuthentication() { ... OnChange?.Invoke(); }
}
```

**Trade-off:** Page reload clears WASM memory → access token is lost. The HttpOnly refresh token cookie persists across reloads. Call `/api/vendor-identity/auth/refresh` on app startup to silently restore the session:

```csharp
// In Program.cs, after builder.Build()
var app = builder.Build();
var authService = app.Services.GetRequiredService<VendorAuthService>();
await authService.TryRestoreSessionAsync(); // calls /auth/refresh with cookie
await app.RunAsync();
```

> **POC limitation:** `TryRestoreSessionAsync` is not implemented in the Phase 2 POC. Users must log in after page refresh. Implement in Phase 3.

---

## Custom AuthenticationStateProvider

WASM needs a custom provider because there's no server-side `HttpContext` to derive auth state from:

```csharp
public sealed class VendorAuthStateProvider : AuthenticationStateProvider
{
    private readonly VendorAuthState _authState;

    public VendorAuthStateProvider(VendorAuthState authState)
    {
        _authState = authState;
        _authState.OnChange += () => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!_authState.IsAuthenticated || string.IsNullOrEmpty(_authState.AccessToken))
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(_authState.AccessToken);

        if (jwt.ValidTo < DateTime.UtcNow)
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        var identity = new ClaimsIdentity(jwt.Claims, "jwt");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }
}
```

Register as **scoped** (not singleton — Blazor requires `AuthenticationStateProvider` to be scoped):

```csharp
builder.Services.AddScoped<AuthenticationStateProvider, VendorAuthStateProvider>();
```

---

## Background Token Refresh: No `IHostedService`

Blazor WASM does not support `IHostedService`. Use `System.Threading.Timer` started from `MainLayout`:

```csharp
public sealed class TokenRefreshService : IAsyncDisposable
{
    private Timer? _timer;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(13); // before 15-min expiry

    public void Start()
    {
        // Idempotent: only start the timer once
        if (_timer is not null)
            return;

        _timer = new Timer(async _ => await SafeRefreshAsync(), null, RefreshInterval, RefreshInterval);
    }

    private async Task SafeRefreshAsync()
    {
        // Prevent overlapping refreshes if the timer ticks again while a refresh is in progress
        if (!await _refreshSemaphore.WaitAsync(0))
            return;

        try
        {
            await TryRefreshAsync();
        }
        catch (Exception)
        {
            // Swallow or log the exception so it doesn't crash the process or spam logs
            // _logger.LogError(ex, "Error refreshing access token.");
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    public async Task CheckAndRefreshIfNeededAsync()
    {
        // Call this on tab visibility change to handle browser throttling
        var timeLeft = _authState.TokenExpiresAt - DateTimeOffset.UtcNow;
        if (timeLeft <= TimeSpan.FromMinutes(3))
            await SafeRefreshAsync();
    }

    public ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        _refreshSemaphore.Dispose();
        return ValueTask.CompletedTask;
    }
    // ...
}
```

Start the timer from `MainLayout.OnInitialized`:

```razor
@code {
    protected override void OnInitialized()
    {
        TokenRefreshService.Start();
    }
}
```

> **GOTCHA:** Browser throttles WASM timers when the tab is not in focus. On tab focus restore,
> call `CheckAndRefreshIfNeededAsync()` to handle missed refresh cycles. In Phase 3, wire this
> to a JS interop `visibilitychange` event listener.

---

## SignalR Hub Authentication from WASM

**Problem:** WebSocket upgrade requests cannot carry an `Authorization: Bearer` header (browser restriction). Instead, JWT must be sent as a query parameter.

**Server (VendorPortal.Api):** Configure `JwtBearerEvents.OnMessageReceived`:

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(accessToken) &&
            context.HttpContext.Request.Path.StartsWithSegments("/hub/vendor-portal"))
        {
            context.Token = accessToken; // extract from query string
        }
        return Task.CompletedTask;
    }
};
```

**Client (WASM `VendorHubService`):** Use `AccessTokenProvider`:

```csharp
_connection = new HubConnectionBuilder()
    .WithUrl($"{apiUrl}/hub/vendor-portal", options =>
    {
        // Called on EVERY connection attempt — always returns the current token,
        // even after a background refresh. This is why the lambda captures _authState
        // (not the token string directly).
        options.AccessTokenProvider = () =>
            Task.FromResult<string?>(_authState.AccessToken);
    })
    .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)])
    .Build();
```

> **Why the lambda matters:** If you capture the token as a string at connection time
> (`var token = _authState.AccessToken; options.AccessTokenProvider = () => Task.FromResult(token);`),
> reconnects after a background token refresh will use the stale token. The lambda must
> capture `_authState` (the state object), not the token value.

---

## RBAC: Role-Based UI in WASM

JWT claims are available via the `ClaimsPrincipal` from `AuthenticationStateProvider`. Use them directly or via a wrapper:

```csharp
// VendorAuthState convenience properties
public bool IsAdmin => Role == "Admin";
public bool CanSubmitChangeRequests => Role is "Admin" or "CatalogManager";
```

In Razor components:

```razor
@attribute [Authorize]  // requires authentication
@inject VendorAuthState AuthState

@if (AuthState.CanSubmitChangeRequests)
{
    <MudButton>Submit Change Request</MudButton>
}
else
{
    <MudAlert Severity="Severity.Info">
        Your role (@AuthState.Role) is read-only.
    </MudAlert>
}

@if (AuthState.IsAdmin)
{
    <MudButton>Manage Users</MudButton>
}
```

> **Security note:** Client-side RBAC is UI-only. Always enforce role checks server-side in API
> endpoints. The JWT `Role` claim is cryptographically verified server-side; the client-side
> check is purely cosmetic (hides buttons from non-admins, does not prevent API calls).

---

## Hub Authentication: Dual Group Pattern

`VendorPortalHub` adds each connection to two SignalR groups on connect:

```csharp
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class VendorPortalHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User!.FindFirst("VendorTenantId")?.Value;
        var userId   = Context.User!.FindFirst("VendorUserId")?.Value;

        // vendor:{tenantId} — shared by all users in the org
        await Groups.AddToGroupAsync(Context.ConnectionId, $"vendor:{tenantId}");
        // user:{userId}   — individual, for personal notifications
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        await base.OnConnectedAsync();
    }
}
```

Server-side push (from a Wolverine handler or background service):

```csharp
// To all users in the tenant
await hubContext.Clients.Group($"vendor:{tenantId}").SendAsync("ReceiveMessage", payload);

// To a specific user only
await hubContext.Clients.Group($"user:{userId}").SendAsync("ReceiveMessage", payload);
```

---

## `index.html` vs `_Host.cshtml`

Blazor WASM uses a **static** `wwwroot/index.html`. There is no server-rendered host page.

```html
<!-- VendorPortal.Web/wwwroot/index.html -->
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <base href="/" />
    <link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
</head>
<body>
    <div id="app">Loading...</div>
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
    <script src="_framework/blazor.webassembly.js"></script>
</body>
</html>
```

> **GOTCHA:** The `<base href="/" />` tag is critical for WASM client-side routing.
> Without it, deep-link navigation (e.g., directly visiting `/dashboard`) breaks because
> relative asset paths resolve incorrectly.

---

## Configuration in WASM

WASM reads configuration from `wwwroot/appsettings.json` (NOT from a server-side `appsettings.json`):

```
VendorPortal.Web/
├── wwwroot/
│   ├── appsettings.json          ← WASM reads this
│   └── index.html
└── appsettings.json              ← NOT read by WASM at runtime
```

```json
// wwwroot/appsettings.json
{
  "ApiClients": {
    "VendorIdentityApiUrl": "http://localhost:5240",
    "VendorPortalApiUrl": "http://localhost:5239"
  }
}
```

For environment-specific configuration (`Development`, `Staging`, `Production`), create:
- `wwwroot/appsettings.Development.json`

---

## App.razor: Protected Routes

Use `AuthorizeRouteView` (not `RouteView`) to enforce authentication for `[Authorize]` pages:

```razor
<Router AppAssembly="typeof(App).Assembly">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                <RedirectToLogin />  <!-- component that navigates to /login -->
            </NotAuthorized>
        </AuthorizeRouteView>
    </Found>
</Router>
```

---

## Checklist: New Blazor WASM Feature

- [ ] SDK is `Microsoft.NET.Sdk.BlazorWebAssembly` (not `Microsoft.NET.Sdk.Web`)
- [ ] `wwwroot/index.html` present with `<base href="/" />`
- [ ] Config in `wwwroot/appsettings.json` (not project root)
- [ ] Named HTTP clients registered with explicit `BaseAddress` in `Program.cs`
- [ ] `AddAuthorizationCore()` (not `AddAuthorization()`)
- [ ] `AuthenticationStateProvider` registered as **scoped**
- [ ] Access token in WASM memory (`VendorAuthState`), never localStorage
- [ ] Refresh token in HttpOnly cookie via server CORS `AllowCredentials()`
- [ ] SignalR: `AccessTokenProvider` lambda captures state object (not token value)
- [ ] Server-side: `JwtBearerEvents.OnMessageReceived` extracts token from `access_token` query param
- [ ] Background refresh: `System.Threading.Timer` started from `MainLayout`, not `IHostedService`
- [ ] RBAC enforced server-side; UI checks are cosmetic only
- [ ] Hub: `[Authorize]` attribute on hub class with `AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme`
- [ ] Hub: dual group membership (`vendor:{tenantId}`, `user:{userId}`) set in `OnConnectedAsync`
- [ ] Hub: `VendorTenantId` from JWT claims only (NEVER query string or request body)

---

## References

- `src/Vendor Portal/VendorPortal.Web/` — reference WASM implementation
- `src/Vendor Portal/VendorPortal.Api/` — JWT-protected API + SignalR hub
- `src/Vendor Identity/VendorIdentity.Api/Auth/` — JWT login/refresh/logout
- `docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md` — why WASM over Blazor Server
- `docs/decisions/0015-jwt-for-vendor-identity.md` — why JWT over session cookies
- `docs/decisions/0025-blazor-wasm-poc-learnings.md` — POC gotchas and limitations
- `docs/skills/wolverine-signalr.md` — SignalR hub setup and authentication patterns
