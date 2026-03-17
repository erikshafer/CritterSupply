using JasperFx.CommandLine;
using Marten;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace Pricing.Api.IntegrationTests;

/// <summary>
/// Test fixture providing PostgreSQL via TestContainers and Alba host for integration tests.
/// Uses the collection fixture pattern to ensure sequential test execution and proper resource sharing.
/// </summary>
public class TestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("pricing_test_db")
        .WithName($"pricing-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private string? _connectionString;

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _connectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_connectionString);
                });

                // Configure test authentication (bypasses JWT validation)
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, BackofficeTestAuthHandler>(
                        "Backoffice", _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = "Backoffice";
                    options.DefaultChallengeScheme = "Backoffice";
                });

                // Disable external Wolverine transports for testing
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
            catch (ObjectDisposedException)
            {
                // Ignore if already disposed during async shutdown
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException))
            {
                // Ignore cancellation/disposal exceptions during shutdown
            }
        }

        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Gets a Marten document session for direct database operations.
    /// Caller is responsible for disposing of the session.
    /// </summary>
    public IDocumentSession GetDocumentSession()
    {
        return Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
    }

    /// <summary>
    /// Gets the document store for advanced operations like cleaning data.
    /// </summary>
    public IDocumentStore GetDocumentStore()
    {
        return Host.Services.GetRequiredService<IDocumentStore>();
    }

    /// <summary>
    /// Cleans all document and event data from the database. Use between tests that need isolation.
    /// </summary>
    public async Task CleanAllDataAsync()
    {
        var store = GetDocumentStore();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
    }

    /// <summary>
    /// Executes a message through Wolverine and waits for all cascading messages to complete.
    /// This ensures all side effects are persisted before assertions.
    /// </summary>
    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host)
            .ExecuteAndWaitAsync((Func<IMessageContext, Task>)(async ctx =>
            {
                await ctx.InvokeAsync(message);
            }));
    }

    /// <summary>
    /// Makes an HTTP call with Alba while tracking Wolverine message activity.
    /// Ensures all cascaded work completes before returning control to the test.
    /// </summary>
    public async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(Action<Scenario> configuration)
    {
        IScenarioResult result = null!;

        var tracked = await Host.ExecuteAndWaitAsync(async () =>
        {
            result = await Host.Scenario(configuration);
        });

        return (tracked, result);
    }
}

/// <summary>
/// Test authentication handler that simulates Backoffice JWT authentication.
/// Always succeeds and provides PricingManager role claim.
/// </summary>
internal sealed class BackofficeTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public BackofficeTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "test-pricing-manager"),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("role", "PricingManager"),  // Role claim for PricingManager policy
        };
        var identity = new ClaimsIdentity(claims, "Backoffice");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Backoffice");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
