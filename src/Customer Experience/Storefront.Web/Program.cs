using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MudBlazor.Services;
using Storefront.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add authentication (cookie-based sessions)
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "CritterSupply.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Add HttpClient for Storefront.Api BFF
builder.Services.AddHttpClient("StorefrontApi", client =>
{
    client.BaseAddress = new Uri("http://localhost:5237");
});

// Add HttpClient for Customer Identity API (for authentication)
builder.Services.AddHttpClient("CustomerIdentityApi", client =>
{
    client.BaseAddress = new Uri("http://localhost:5235");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Map authentication endpoints (server-side, not interactive)
app.MapPost("/api/auth/login", async (LoginRequest request, IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
{
    try
    {
        // Call Customer Identity API
        var client = httpClientFactory.CreateClient("CustomerIdentityApi");
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = request.Email, password = request.Password });

        if (!response.IsSuccessStatusCode)
        {
            return Results.Unauthorized();
        }

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginResponse == null)
        {
            return Results.Unauthorized();
        }

        // Create claims for session
        var claims = new[]
        {
            new Claim("CustomerId", loginResponse.CustomerId.ToString()),
            new Claim(ClaimTypes.Email, loginResponse.Email),
            new Claim(ClaimTypes.Name, $"{loginResponse.FirstName} {loginResponse.LastName}"),
            new Claim(ClaimTypes.GivenName, loginResponse.FirstName),
            new Claim(ClaimTypes.Surname, loginResponse.LastName)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        // Sign in (creates cookie)
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return Results.Ok(new { success = true, firstName = loginResponse.FirstName });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Login failed: {ex.Message}");
    }
});

app.MapPost("/api/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Records for login
record LoginRequest(string Email, string Password);
record LoginResponse(Guid CustomerId, string Email, string FirstName, string LastName);
