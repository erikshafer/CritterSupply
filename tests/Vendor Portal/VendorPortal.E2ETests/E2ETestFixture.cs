using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Marten;
using Testcontainers.PostgreSql;
using VendorIdentity.Api.Auth;
using VendorIdentity.Identity;
using Wolverine;

// VendorLoginEndpoint: used as WebApplicationFactory anchor for VendorIdentity.Api
using VendorLoginEndpoint = VendorIdentity.Api.Auth.VendorLoginEndpoint;

namespace VendorPortal.E2ETests;

/// <summary>
/// E2E test fixture that starts real Kestrel servers for VendorIdentity.Api,
/// VendorPortal.Api, and a static file host serving VendorPortal.Web (Blazor WASM),
/// backed by TestContainers PostgreSQL instances.
///
/// Architecture:
///   Playwright Browser (Chromium)
///         │
///         ▼
///   VendorPortal.Web (WASM static files served by thin ASP.NET host, random port)
///         │ (cross-origin HTTP + WebSocket)
///         ├──────────────────────────┐
///         ▼                          ▼
///   VendorPortal.Api              VendorIdentity.Api
///   (Kestrel, random port)        (Kestrel, random port)
///   ├── Marten (vendorportal)     ├── EF Core (vendoridentity)
///   ├── SignalR hub               ├── JWT issuance
///   ├── Wolverine (local only)    └── Demo account seeding
///   └── JWT validation
///         │                          │
///         └── Shared PostgreSQL ─────┘
///             (TestContainers)
///
/// Key constraints:
///   - All services use REAL Kestrel (not TestServer) — Playwright requires TCP ports.
///   - VendorIdentity.Api is a real server (not stubbed) — E2E tests need real JWTs.
///   - CORS is opened to allow the WASM host's random-port origin.
///   - RabbitMQ transports are disabled — Wolverine runs in local-only mode.
/// </summary>
public sealed class E2ETestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("vendor_e2e_test_db")
        .WithName($"vendor-e2e-postgres-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private VendorIdentityApiKestrelFactory? _identityFactory;
    private VendorPortalApiKestrelFactory? _portalApiFactory;
    private WasmStaticFileHost? _wasmHost;

    /// <summary>Base URL of VendorPortal.Web WASM host — what Playwright navigates to.</summary>
    public string WasmBaseUrl { get; private set; } = string.Empty;

    /// <summary>Base URL of VendorPortal.Api — for direct API calls in test hooks.</summary>
    public string PortalApiBaseUrl { get; private set; } = string.Empty;

    /// <summary>Base URL of VendorIdentity.Api — for direct API calls in test hooks.</summary>
    public string IdentityApiBaseUrl { get; private set; } = string.Empty;

    /// <summary>Direct access to VendorPortal.Api host for SignalR hub context injection.</summary>
    public IHost PortalApiHost { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Console.WriteLine("[E2ETestFixture] 🚀 Starting E2E test infrastructure...");

        // Step 1: Start TestContainers PostgreSQL
        Console.WriteLine("[E2ETestFixture] Starting PostgreSQL TestContainer...");
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();
        Console.WriteLine($"[E2ETestFixture] ✅ PostgreSQL started: {connectionString}");

        // Step 2: Start VendorIdentity.Api (EF Core — JWT issuer)
        Console.WriteLine("[E2ETestFixture] Starting VendorIdentity.Api...");
        _identityFactory = new VendorIdentityApiKestrelFactory(connectionString);
        _identityFactory.StartKestrel();
        IdentityApiBaseUrl = _identityFactory.ServerAddress;
        Console.WriteLine($"[E2ETestFixture] ✅ VendorIdentity.Api listening: {IdentityApiBaseUrl}");

        // Step 2a: Manually invoke VendorIdentitySeedData (WebApplicationFactory doesn't execute startup code)
        using (var scope = _identityFactory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<VendorIdentityDbContext>();
            await dbContext.Database.MigrateAsync();

            Console.WriteLine("[E2ETestFixture] Seeding VendorIdentity test data...");
            await VendorIdentitySeedData.SeedAsync(dbContext);

            // Verify seed data was created
            var tenantCount = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(dbContext.Tenants);
            var userCount = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(dbContext.Users);
            Console.WriteLine($"[E2ETestFixture] ✅ VendorIdentity seed data: {tenantCount} tenants, {userCount} users");
        }

        // Step 3: Start VendorPortal.Api (Marten — analytics, change requests, SignalR)
        Console.WriteLine("[E2ETestFixture] Starting VendorPortal.Api...");
        _portalApiFactory = new VendorPortalApiKestrelFactory(connectionString, IdentityApiBaseUrl);
        _portalApiFactory.StartKestrel();
        PortalApiBaseUrl = _portalApiFactory.ServerAddress;
        PortalApiHost = _portalApiFactory.Services.GetRequiredService<IHost>();
        Console.WriteLine($"[E2ETestFixture] ✅ VendorPortal.Api listening: {PortalApiBaseUrl}");

        // Step 3a: Manually invoke VendorPortalSeedData (WebApplicationFactory doesn't execute startup code)
        Console.WriteLine("[E2ETestFixture] Seeding VendorPortal test data...");
        await VendorPortalSeedData.SeedAsync(_portalApiFactory.Services.GetRequiredService<IDocumentStore>());

        // Verify seed data was created
        var store = _portalApiFactory.Services.GetRequiredService<IDocumentStore>();
        await using var session = store.LightweightSession();
        var account = await session.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(
            Guid.Parse("00000000-0000-0000-0000-000000000001"));
        Console.WriteLine($"[E2ETestFixture] ✅ VendorPortal seed data: VendorAccount exists = {account != null}");
        if (account != null)
            Console.WriteLine($"[E2ETestFixture]    Organization: {account.OrganizationName}, Contact: {account.ContactEmail}");

        // Step 4: Start WASM static file host serving VendorPortal.Web with test API URLs
        Console.WriteLine("[E2ETestFixture] Starting WASM static file host...");
        _wasmHost = new WasmStaticFileHost(IdentityApiBaseUrl, PortalApiBaseUrl);
        await _wasmHost.StartAsync();
        WasmBaseUrl = _wasmHost.BaseUrl;
        Console.WriteLine($"[E2ETestFixture] ✅ WASM host listening: {WasmBaseUrl}");

        Console.WriteLine("[E2ETestFixture] 🎉 E2E test infrastructure ready!");
    }

    public async Task DisposeAsync()
    {
        if (_wasmHost != null) await _wasmHost.DisposeAsync();
        if (_portalApiFactory != null) await _portalApiFactory.DisposeAsync();
        if (_identityFactory != null) await _identityFactory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Cleans all Marten document/event data from the VendorPortal test database.
    /// Call in DataHooks.AfterScenario for complete test isolation.
    /// Note: VendorIdentity EF Core seed data (tenant + users) is NOT cleaned — it's shared.
    /// </summary>
    public async Task CleanMartenDataAsync()
    {
        var store = _portalApiFactory?.Services.GetRequiredService<IDocumentStore>();
        if (store != null)
        {
            await store.Advanced.Clean.DeleteAllDocumentsAsync();
            await store.Advanced.Clean.DeleteAllEventDataAsync();
        }
    }

    /// <summary>
    /// Gets a JWT access token for the specified user by calling the VendorIdentity login endpoint.
    /// Use this to seed data via direct API calls without going through the UI.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(string email, string password)
    {
        using var client = new HttpClient { BaseAddress = new Uri(IdentityApiBaseUrl) };
        var loginRequest = new { Email = email, Password = password };
        var response = await client.PostAsJsonAsync("/api/vendor-identity/auth/login", loginRequest);
        response.EnsureSuccessStatusCode();

        var loginResponse = await response.Content.ReadFromJsonAsync<VendorLoginResponse>();
        return loginResponse?.AccessToken ?? throw new InvalidOperationException("Login failed to return access token");
    }

    /// <summary>
    /// Creates an HttpClient configured to call VendorPortal.Api with JWT authentication.
    /// Use this to seed data via direct API calls, bypassing UI navigation issues.
    /// </summary>
    public HttpClient CreateAuthenticatedPortalApiClient(string accessToken)
    {
        var client = new HttpClient { BaseAddress = new Uri(PortalApiBaseUrl) };
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private sealed record VendorLoginResponse(
        string AccessToken,
        string Email,
        string FirstName,
        string LastName,
        string Role,
        string TenantName);
}


/// <summary>
/// WebApplicationFactory for VendorIdentity.Api that binds to a real Kestrel TCP port.
/// Runs the real EF Core service with auto-seeded demo accounts.
/// Uses VendorLoginEndpoint as the anchor type (Program is ambiguous across assemblies).
/// </summary>
internal sealed class VendorIdentityApiKestrelFactory(string connectionString)
    : WebApplicationFactory<VendorLoginEndpoint>
{
    public string ServerAddress { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // CRITICAL: Set environment to Development so VendorIdentitySeedData.SeedAsync runs
        // (Program.cs lines 86-92 only seed data in Development environment)
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:postgres"] = connectionString,
                // JWT configuration for token issuance (must match VendorPortal.Api expectation)
                ["Jwt:SigningKey"] = "dev-only-signing-key-change-in-production-must-be-at-least-32-chars",
                ["Jwt:Issuer"] = "vendor-identity",
                ["Jwt:Audience"] = "vendor-portal",
                ["Jwt:AccessTokenExpiryMinutes"] = "15",
                ["Jwt:RefreshTokenExpiryDays"] = "7"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Disable external Wolverine transports (RabbitMQ)
            services.DisableAllExternalWolverineTransports();

            // Open CORS for test (random ports)
            // IMPORTANT: VendorIdentity.Api uses a NAMED policy "VendorPortalWeb" (not default policy)
            // Must override the named policy that the API actually uses in app.UseCors("VendorPortalWeb")
            services.AddCors(opts =>
            {
                opts.AddPolicy("VendorPortalWeb", policy => policy
                    .SetIsOriginAllowed(_ => true)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
            });
        });
    }

    internal void StartKestrel()
    {
        UseKestrel(0);
        CreateDefaultClient();

        var serverAddresses = Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        var rawAddress = serverAddresses?.Addresses.FirstOrDefault() ?? string.Empty;
        ServerAddress = NormalizeAddress(rawAddress);
    }

    internal static string NormalizeAddress(string rawAddress) => rawAddress
        .Replace("//[::]:", "//localhost:")
        .Replace("//0.0.0.0:", "//localhost:");
}

/// <summary>
/// WebApplicationFactory for VendorPortal.Api that binds to a real Kestrel TCP port.
/// Uses TestContainers Postgres for Marten and validates JWTs from the test VendorIdentity.Api.
/// Uses VendorPortalHub as the anchor type (Program is ambiguous across assemblies).
/// </summary>
internal sealed class VendorPortalApiKestrelFactory(string connectionString, string identityApiUrl)
    : WebApplicationFactory<VendorPortal.Api.Hubs.VendorPortalHub>
{
    public string ServerAddress { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // CRITICAL: Set environment to Development so VendorPortalSeedData.SeedAsync runs
        // (Program.cs line 195 only seeds data in Development environment)
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:postgres"] = connectionString,
                ["ApiClients:VendorIdentityApiUrl"] = identityApiUrl,
                // JWT configuration for token validation (must match VendorIdentity.Api issuance)
                ["Jwt:SigningKey"] = "dev-only-signing-key-change-in-production-must-be-at-least-32-chars",
                ["Jwt:Issuer"] = "vendor-identity",
                ["Jwt:Audience"] = "vendor-portal"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Override Marten connection to use TestContainers Postgres
            services.ConfigureMarten(opts => opts.Connection(connectionString));

            // Disable external Wolverine transports (RabbitMQ)
            services.DisableAllExternalWolverineTransports();

            // Open CORS for test (random ports)
            // IMPORTANT: VendorPortal.Api uses a NAMED policy "VendorPortalWeb" (not default policy)
            // Must override the named policy that the API actually uses in app.UseCors("VendorPortalWeb")
            services.AddCors(opts =>
            {
                opts.AddPolicy("VendorPortalWeb", policy => policy
                    .SetIsOriginAllowed(_ => true)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
            });
        });
    }

    internal void StartKestrel()
    {
        UseKestrel(0);
        CreateDefaultClient();

        var serverAddresses = Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        var rawAddress = serverAddresses?.Addresses.FirstOrDefault() ?? string.Empty;
        ServerAddress = VendorIdentityApiKestrelFactory.NormalizeAddress(rawAddress);
    }
}

/// <summary>
/// Lightweight ASP.NET Core host that serves the compiled Blazor WASM static files
/// from VendorPortal.Web's build output, with a test-specific appsettings.json
/// that points at the test API server URLs.
///
/// Blazor WASM apps fetch appsettings.json via HTTP from their own origin at startup.
/// This host intercepts that request and returns the test configuration dynamically.
/// </summary>
internal sealed class WasmStaticFileHost : IAsyncDisposable
{
    private readonly string _identityApiUrl;
    private readonly string _portalApiUrl;
    private WebApplication? _app;

    public string BaseUrl { get; private set; } = string.Empty;

    public WasmStaticFileHost(string identityApiUrl, string portalApiUrl)
    {
        _identityApiUrl = identityApiUrl;
        _portalApiUrl = portalApiUrl;
    }

    public async Task StartAsync()
    {
        // Locate the VendorPortal.Web wwwroot output directory
        // In test builds, the WASM files are in the VendorPortal.Web project's wwwroot
        var wasmRoot = FindWasmRoot();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // Random port

        // Open CORS for cross-origin API calls from WASM
        builder.Services.AddCors(opts =>
        {
            opts.AddDefaultPolicy(policy => policy
                .SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });

        _app = builder.Build();

        _app.UseCors();

        // Intercept appsettings.json requests to inject test API URLs
        _app.MapGet("/appsettings.json", () => Results.Json(new
        {
            ApiClients = new
            {
                VendorIdentityApiUrl = _identityApiUrl,
                VendorPortalApiUrl = _portalApiUrl
            }
        }));

        // Serve static WASM files
        _app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wasmRoot),
            ServeUnknownFileTypes = true // Required for .wasm, .dll, .dat files
        });

        // SPA fallback — serve index.html for all non-file routes (Blazor client-side routing)
        _app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wasmRoot)
        });

        await _app.StartAsync();

        var addresses = _app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        BaseUrl = VendorIdentityApiKestrelFactory.NormalizeAddress(
            addresses?.Addresses.FirstOrDefault() ?? string.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null) await _app.DisposeAsync();
    }

    /// <summary>
    /// Locates the VendorPortal.Web wwwroot directory containing compiled WASM files.
    /// Walks up from the test output directory to find the bin build output.
    /// IMPORTANT: Must use bin/{Configuration}/net10.0/publish/wwwroot (not src/*/wwwroot) to get compiled _framework files.
    /// Supports both Release (CI) and Debug (local development) configurations.
    /// </summary>
    private static string FindWasmRoot()
    {
        // Strategy: Start from test output directory and look for sibling VendorPortal.Web publish output
        var current = AppContext.BaseDirectory;

        // Prefer Release in CI, but support local Debug too.
        // Check Release first so CI uses the correct build artifacts.
        var configurations = new[] { "Release", "Debug" };

        // Walk up directory tree to find repo root (has src/ directory)
        while (current != null)
        {
            var srcDir = Path.Combine(current, "src");
            if (Directory.Exists(srcDir))
            {
                // Try each configuration in priority order
                foreach (var config in configurations)
                {
                    // PRIORITY 1: Check publish output directory (has index.html + _framework)
                    var publishWwwroot = Path.Combine(current, "src", "Vendor Portal", "VendorPortal.Web", "bin", config, "net10.0", "publish", "wwwroot");
                    if (Directory.Exists(publishWwwroot) && Directory.Exists(Path.Combine(publishWwwroot, "_framework")) && File.Exists(Path.Combine(publishWwwroot, "index.html")))
                    {
                        Console.WriteLine($"✅ [FindWasmRoot] Found publish wwwroot ({config}): {publishWwwroot}");
                        return publishWwwroot;
                    }

                    // PRIORITY 2: Check bin output directory (has _framework but may be missing index.html)
                    var binWwwroot = Path.Combine(current, "src", "Vendor Portal", "VendorPortal.Web", "bin", config, "net10.0", "wwwroot");
                    if (Directory.Exists(binWwwroot) && Directory.Exists(Path.Combine(binWwwroot, "_framework")) && File.Exists(Path.Combine(binWwwroot, "index.html")))
                    {
                        Console.WriteLine($"✅ [FindWasmRoot] Found compiled wwwroot ({config}): {binWwwroot}");
                        return binWwwroot;
                    }
                }

                // PRIORITY 3: Check source directory as fallback (only if it has _framework compiled into it)
                var srcWwwroot = Path.Combine(current, "src", "Vendor Portal", "VendorPortal.Web", "wwwroot");
                if (Directory.Exists(srcWwwroot))
                {
                    var hasFramework = Directory.Exists(Path.Combine(srcWwwroot, "_framework"));
                    var hasIndexHtml = File.Exists(Path.Combine(srcWwwroot, "index.html"));
                    Console.WriteLine($"⚠️  [FindWasmRoot] Found source wwwroot (has _framework: {hasFramework}, has index.html: {hasIndexHtml}): {srcWwwroot}");
                    if (hasFramework && hasIndexHtml)
                        return srcWwwroot;
                }

                // None of the locations have all required files
                throw new InvalidOperationException(
                    $"Could not locate VendorPortal.Web/wwwroot directory with both _framework/ and index.html. " +
                    $"Tried configurations: {string.Join(", ", configurations)}. " +
                    $"Ensure the VendorPortal.Web Blazor WASM project is published before running E2E tests. " +
                    $"The PublishBlazorWasmForE2E MSBuild target in VendorPortal.E2ETests.csproj handles this automatically.");
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not locate repository root directory (expected to contain 'src/' folder).");
    }
}
