using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Backoffice.Web.Auth;
using Backoffice.Web.Hub;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Backoffice.Web.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// GOTCHA: In Blazor WASM, HttpClient base address defaults to the app's own URL.
// For cross-origin API calls, named clients with explicit base addresses are required.
var identityApiUrl = builder.Configuration["ApiClients:BackofficeIdentityApiUrl"]
    ?? "http://localhost:5249";
var backofficeApiUrl = builder.Configuration["ApiClients:BackofficeApiUrl"]
    ?? "http://localhost:5243";

builder.Services.AddHttpClient("BackofficeIdentityApi", client =>
{
    client.BaseAddress = new Uri(identityApiUrl);
});

builder.Services.AddHttpClient("BackofficeApi", client =>
{
    client.BaseAddress = new Uri(backofficeApiUrl);
});

builder.Services.AddMudServices();

// Auth state — in-memory JWT storage (NOT localStorage — XSS risk)
// GOTCHA: In Blazor WASM, Scoped == Singleton (single browser tab lifetime).
// TokenRefreshService is Singleton (background timer) so its dependencies must also be
// Singleton. BackofficeAuthService is safe as Singleton because NavigationManager is
// Singleton in WASM (no server-side HttpContext / per-circuit scoping).
builder.Services.AddSingleton<BackofficeAuthState>();
builder.Services.AddSingleton<BackofficeAuthService>();
builder.Services.AddSingleton<TokenRefreshService>();
builder.Services.AddSingleton<SessionExpiredService>();

// SignalR hub service (singleton — manages persistent connection)
builder.Services.AddSingleton<BackofficeHubService>();

// Custom AuthenticationStateProvider (reads from BackofficeAuthState)
// Authorization policies matching ADR 0031 (Backoffice RBAC Model)
// GOTCHA: In Blazor WASM, policies must be registered client-side for <AuthorizeView Policy="..." />
// These match the role names in JWT claims issued by BackofficeIdentity.Api
builder.Services.AddAuthorizationCore(opts =>
{
    opts.AddPolicy("CustomerService", policy => policy.RequireRole("customer-service", "system-admin"));
    opts.AddPolicy("Executive", policy => policy.RequireRole("executive", "system-admin"));
    opts.AddPolicy("OperationsManager", policy => policy.RequireRole("operations-manager", "system-admin"));
    opts.AddPolicy("WarehouseClerk", policy => policy.RequireRole("warehouse-clerk", "system-admin"));
    opts.AddPolicy("PricingManager", policy => policy.RequireRole("pricing-manager", "system-admin"));
    opts.AddPolicy("ProductManager", policy => policy.RequireRole("product-manager", "system-admin"));
    opts.AddPolicy("CopyWriter", policy => policy.RequireRole("copy-writer", "system-admin"));
    opts.AddPolicy("SystemAdmin", policy => policy.RequireRole("system-admin"));
});
builder.Services.AddScoped<AuthenticationStateProvider, BackofficeAuthStateProvider>();

var app = builder.Build();

// Start background token refresh (WASM pattern — no IHostedService support)
var refreshService = app.Services.GetRequiredService<TokenRefreshService>();
refreshService.Start();

await app.RunAsync();
