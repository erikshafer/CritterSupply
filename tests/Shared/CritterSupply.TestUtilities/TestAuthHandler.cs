using System.Security.Claims;
using System.Text.Encodings.Web;
using Alba;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CritterSupply.TestUtilities;

/// <summary>
/// Fake authentication handler for integration tests.
/// Authenticates requests that include an <c>Authorization</c> header using configurable
/// claims from <see cref="TestAuthOptions"/>. Requests without the header receive
/// <see cref="AuthenticateResult.NoResult"/> so that <c>[Authorize]</c> endpoints
/// correctly return 401 for unauthenticated callers.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly TestAuthOptions _testOptions;

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<TestAuthOptions> testOptions)
        : base(options, logger, encoder)
    {
        _testOptions = testOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // If no Authorization header is present, do not authenticate.
        // This allows [Authorize] endpoints to correctly return 401 for unauthenticated requests.
        // Endpoints with [AllowAnonymous] are unaffected by this check.
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _testOptions.UserId),
            new("sub", _testOptions.UserId),
            new(ClaimTypes.Name, _testOptions.UserName),
        };

        foreach (var role in _testOptions.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            // Also add as "role" claim (JWT convention used by some BCs)
            claims.Add(new Claim("role", role));
        }

        if (_testOptions.TenantId is not null)
        {
            claims.Add(new Claim("tenant_id", _testOptions.TenantId));
        }

        foreach (var (type, value) in _testOptions.AdditionalClaims)
        {
            claims.Add(new Claim(type, value));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Configuration options for <see cref="TestAuthHandler"/>.
/// Register via <c>services.Configure&lt;TestAuthOptions&gt;()</c> or use the
/// <see cref="TestAuthExtensions"/> helper methods.
/// </summary>
public sealed class TestAuthOptions
{
    /// <summary>User ID claim value. Defaults to a stable GUID.</summary>
    public string UserId { get; set; } = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    /// <summary>User name claim value.</summary>
    public string UserName { get; set; } = "test-admin";

    /// <summary>Roles to include in the identity (both ClaimTypes.Role and "role" claims).</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>Optional tenant ID claim.</summary>
    public string? TenantId { get; set; }

    /// <summary>Any additional claims to include in the identity.</summary>
    public List<(string Type, string Value)> AdditionalClaims { get; set; } = [];
}

/// <summary>
/// Extension methods for registering <see cref="TestAuthHandler"/> in test fixtures.
/// </summary>
public static class TestAuthExtensions
{
    /// <summary>
    /// Replaces the application's authentication configuration with a test handler
    /// registered for the specified authentication schemes.
    /// All authorization policies are re-registered with default settings so they
    /// evaluate against the test identity rather than rejecting unauthenticated requests.
    /// </summary>
    /// <param name="services">The service collection from the test host builder.</param>
    /// <param name="roles">Roles the test identity should have.</param>
    /// <param name="schemes">Named authentication schemes to register (e.g. "Backoffice", "Vendor").</param>
    public static void AddTestAuthentication(
        this IServiceCollection services,
        string[] roles,
        params string[] schemes)
    {
        // Remove existing authentication registrations
        var authServices = services.Where(s =>
            s.ServiceType.Namespace == "Microsoft.AspNetCore.Authentication" ||
            s.ServiceType.FullName?.Contains("Authentication") == true)
            .ToList();
        foreach (var service in authServices)
        {
            services.Remove(service);
        }

        // Configure test auth options with the specified roles
        services.Configure<TestAuthOptions>(opts =>
        {
            opts.Roles = [.. roles];
        });

        // Register test authentication handler for each scheme
        var defaultScheme = schemes.Length > 0 ? schemes[0] : "Test";
        var authBuilder = services.AddAuthentication(defaultScheme);

        foreach (var scheme in schemes)
        {
            authBuilder.AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(scheme, _ => { });
        }

        // Re-register authorization with default settings so policies evaluate
        // against the test identity's claims (roles provided via TestAuthOptions)
        services.AddAuthorization();
    }

    /// <summary>
    /// Adds a default <c>Authorization: Bearer test-token</c> header to every Alba scenario.
    /// Call this after creating the <see cref="IAlbaHost"/> so that <see cref="TestAuthHandler"/>
    /// recognises each scenario request as authenticated. Requests made via raw
    /// <c>HttpClient</c> (e.g., <c>Server.CreateClient()</c>) are unaffected and will
    /// correctly receive 401 from <c>[Authorize]</c> endpoints.
    /// </summary>
    public static void AddDefaultAuthHeader(this IAlbaHost host)
    {
        host.BeforeEach(ctx =>
        {
            if (!ctx.Request.Headers.ContainsKey("Authorization"))
            {
                ctx.Request.Headers["Authorization"] = "Bearer test-token";
            }
        });
    }
}
