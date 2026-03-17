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

// SignalR hub service (singleton — manages persistent connection)
builder.Services.AddSingleton<BackofficeHubService>();

// Custom AuthenticationStateProvider (reads from BackofficeAuthState)
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, BackofficeAuthStateProvider>();

var app = builder.Build();

// Start background token refresh (WASM pattern — no IHostedService support)
var refreshService = app.Services.GetRequiredService<TokenRefreshService>();
refreshService.Start();

await app.RunAsync();
