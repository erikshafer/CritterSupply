using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Alba;
using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace VendorPortal.Api.IntegrationTests;

/// <summary>
/// Test fixture providing PostgreSQL via TestContainers and an Alba host for
/// VendorPortal.Api integration tests.
/// Uses the collection fixture pattern to ensure sequential test execution and
/// proper resource sharing across tests in the same collection.
/// </summary>
public sealed class TestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("vendor_portal_test")
        .WithName($"vendor-portal-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    // Test JWT configuration — matches appsettings.json values used in testing
    private const string TestJwtSigningKey = "dev-only-signing-key-change-in-production-must-be-at-least-32-chars";
    private const string TestJwtIssuer = "vendor-identity";
    private const string TestJwtAudience = "vendor-portal";

    private string? _connectionString;

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _connectionString = _postgres.GetConnectionString();

        // Required for WebApplicationFactory usage with Alba and JasperFx command-line processing.
        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Override Marten to use the test container connection string.
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_connectionString);
                });

                // Disable RabbitMQ and any other external Wolverine transports.
                // Tests exercise handlers directly without real message broker infrastructure.
                services.DisableAllExternalWolverineTransports();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (Host != null)
        {
            try
            {
                await Host.StopAsync();
                await Host.DisposeAsync();
            }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException)) { }
        }

        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Returns a Marten document session for direct database assertions.
    /// Caller is responsible for disposing the session.
    /// </summary>
    public IDocumentSession GetDocumentSession()
        => Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();

    /// <summary>Returns the Marten document store for advanced operations.</summary>
    public IDocumentStore GetDocumentStore()
        => Host.Services.GetRequiredService<IDocumentStore>();

    /// <summary>
    /// Deletes all Marten documents. Call at the start of each test that needs a clean slate.
    /// </summary>
    public async Task CleanAllDocumentsAsync()
    {
        var store = GetDocumentStore();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
    }

    /// <summary>
    /// Sends a message directly through Wolverine and waits for all cascaded work to finish.
    /// Uses Alba's TrackActivity to ensure handler side-effects are fully persisted.
    /// </summary>
    public async Task ExecuteMessageAsync<T>(T message, int timeoutSeconds = 15, CancellationToken ct = default)
        where T : class
    {
        await Host.TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host)
            .ExecuteAndWaitAsync((Func<IMessageContext, Task>)(async ctx =>
            {
                await ctx.InvokeAsync(message, ct);
            }));
    }

    /// <summary>
    /// Sends a message through Wolverine and returns the tracked session for hub message assertions.
    /// Use this when you need to verify what Wolverine published (e.g., SignalR hub messages).
    /// </summary>
    public Task<ITrackedSession> TrackMessageAsync<T>(T message, int timeoutSeconds = 15, CancellationToken ct = default)
        where T : class
    {
        return Host.TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host)
            .ExecuteAndWaitAsync((Func<IMessageContext, Task>)(async ctx =>
            {
                await ctx.InvokeAsync(message, ct);
            }));
    }

    /// <summary>
    /// Creates a signed JWT Bearer token for use in authenticated endpoint tests.
    /// Uses the same signing key and parameters as the production configuration.
    /// </summary>
    /// <param name="tenantId">The vendor tenant ID to embed in claims.</param>
    /// <param name="userId">The vendor user ID to embed in claims. Defaults to a new Guid.</param>
    /// <param name="tenantStatus">Tenant status claim (Active, Suspended, Terminated).</param>
    /// <param name="role">Role claim (Admin, ProductManager, Analyst).</param>
    public string CreateTestJwt(
        Guid tenantId,
        Guid? userId = null,
        string tenantStatus = "Active",
        string role = "Admin")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("VendorTenantId", tenantId.ToString()),
            new Claim("VendorUserId", (userId ?? Guid.NewGuid()).ToString()),
            new Claim("VendorTenantStatus", tenantStatus),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Sub, (userId ?? Guid.NewGuid()).ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: TestJwtIssuer,
            audience: TestJwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
