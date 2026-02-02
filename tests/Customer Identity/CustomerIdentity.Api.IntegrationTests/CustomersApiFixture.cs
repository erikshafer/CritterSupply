using Alba;
using CustomerIdentity.AddressBook;
using JasperFx.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Wolverine;

namespace CustomerIdentity.Api.IntegrationTests;

public class TestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("customer_identity_test_db")
        .WithName($"customer-identity-postgres-test-{Guid.NewGuid():N}")
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
                // Remove the default DbContext registration
                services.RemoveAll<DbContextOptions<CustomerIdentityDbContext>>();
                services.RemoveAll<CustomerIdentityDbContext>();

                // Register DbContext with test connection string
                services.AddDbContext<CustomerIdentityDbContext>(options =>
                    options.UseNpgsql(_connectionString));

                services.DisableAllExternalWolverineTransports();
            });
        });

        // Apply migrations
        using var scope = Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CustomerIdentityDbContext>();
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

    public CustomerIdentityDbContext GetDbContext()
    {
        var scope = Host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<CustomerIdentityDbContext>();
    }

    public async Task CleanAllDataAsync()
    {
        using var scope = Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CustomerIdentityDbContext>();

        await dbContext.Addresses.ExecuteDeleteAsync();
        await dbContext.Customers.ExecuteDeleteAsync();
    }
}
