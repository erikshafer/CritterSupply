using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using VendorPortal.Web.Auth;
using VendorPortal.Web.Hub;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<VendorPortal.Web.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// GOTCHA: In Blazor WASM, HttpClient base address defaults to the app's own URL.
// For cross-origin API calls, named clients with explicit base addresses are required.
var identityApiUrl = builder.Configuration["ApiClients:VendorIdentityApiUrl"]
    ?? "http://localhost:5240";
var portalApiUrl = builder.Configuration["ApiClients:VendorPortalApiUrl"]
    ?? "http://localhost:5239";

builder.Services.AddHttpClient("VendorIdentityApi", client =>
{
    client.BaseAddress = new Uri(identityApiUrl);
});

builder.Services.AddHttpClient("VendorPortalApi", client =>
{
    client.BaseAddress = new Uri(portalApiUrl);
});

builder.Services.AddMudServices();

// Auth state — in-memory JWT storage (NOT localStorage — XSS risk)
// GOTCHA: In Blazor WASM, Scoped == Singleton (single browser tab lifetime).
// TokenRefreshService is Singleton (background timer) so its dependencies must also be
// Singleton. VendorAuthService is safe as Singleton because NavigationManager is
// Singleton in WASM (no server-side HttpContext / per-circuit scoping).
builder.Services.AddSingleton<VendorAuthState>();
builder.Services.AddSingleton<VendorAuthService>();
builder.Services.AddSingleton<TokenRefreshService>();

// Custom AuthenticationStateProvider (reads from VendorAuthState)
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, VendorAuthStateProvider>();

// Hub service
builder.Services.AddSingleton<VendorHubService>();

var app = builder.Build();

await app.RunAsync();
