using Alba;
using BackofficeIdentity.Identity;
using JasperFx.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Wolverine;

namespace BackofficeIdentity.Api.IntegrationTests;

public sealed class BackofficeIdentityApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("backoffice_identity_test_db")
        .WithName($"backoffice-identity-postgres-test-{Guid.NewGuid():N}")
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
                // Remove default DbContext registration
                services.RemoveAll<DbContextOptions<BackofficeIdentityDbContext>>();
                services.RemoveAll<BackofficeIdentityDbContext>();

                // Register DbContext with test connection string
                services.AddDbContext<BackofficeIdentityDbContext>(options =>
                    options.UseNpgsql(_connectionString));

                // Disable RabbitMQ for integration tests
                services.DisableAllExternalWolverineTransports();

                // Bypass authorization for integration tests
                // Use AllowAnonymous policy that bypasses all authorization
                services.AddAuthorization(opts =>
                {
                    opts.AddPolicy("SystemAdmin", policy => policy.RequireAssertion(_ => true));
                });
            });
        });

        // Apply migrations
        using var scope = Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentityDbContext>();
        await dbContext.Database.MigrateAsync();
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

    public BackofficeIdentityDbContext GetDbContext()
    {
        var scope = Host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<BackofficeIdentityDbContext>();
    }

    public async Task CleanAllDataAsync()
    {
        using var scope = Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentityDbContext>();

        await dbContext.Users.ExecuteDeleteAsync();
    }
}
