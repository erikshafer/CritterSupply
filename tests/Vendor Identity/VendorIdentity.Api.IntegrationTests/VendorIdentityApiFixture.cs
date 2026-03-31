using Alba;
using CritterSupply.TestUtilities;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using VendorIdentity.Identity;
using Wolverine;

namespace VendorIdentity.Api.IntegrationTests;

public sealed class VendorIdentityApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("vendor_identity_test_db")
        .WithName($"vendor-identity-postgres-test-{Guid.NewGuid():N}")
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
                services.RemoveAll<DbContextOptions<VendorIdentityDbContext>>();
                services.RemoveAll<VendorIdentityDbContext>();

                // Register DbContext with test connection string
                services.AddDbContext<VendorIdentityDbContext>(options =>
                    options.UseNpgsql(_connectionString));

                // Disable RabbitMQ for integration tests
                services.DisableAllExternalWolverineTransports();

                // Register test authentication for the JWT Bearer scheme
                services.AddTestAuthentication(
                    roles: ["Admin"],
                    schemes: JwtBearerDefaults.AuthenticationScheme);
            });
        });

        Host.AddDefaultAuthHeader();

        // Apply migrations
        using var scope = Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VendorIdentityDbContext>();
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

    public VendorIdentityDbContext GetDbContext()
    {
        var scope = Host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<VendorIdentityDbContext>();
    }

    public async Task CleanAllDataAsync()
    {
        using var scope = Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VendorIdentityDbContext>();

        await dbContext.Invitations.ExecuteDeleteAsync();
        await dbContext.Users.ExecuteDeleteAsync();
        await dbContext.Tenants.ExecuteDeleteAsync();
    }
}
