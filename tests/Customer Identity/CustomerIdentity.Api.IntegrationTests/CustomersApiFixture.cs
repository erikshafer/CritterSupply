using Alba;
using CritterSupply.TestUtilities;
using CustomerIdentity.AddressBook;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
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

                // Replace authentication: keep Cookie (real) + replace Backoffice JWT (test).
                // Cookie auth is needed for login/logout/session tests.
                // Backoffice scheme is needed for [Authorize(Policy = "CustomerService")] endpoints.
                var authServices = services.Where(s =>
                    s.ServiceType.Namespace == "Microsoft.AspNetCore.Authentication" ||
                    s.ServiceType.FullName?.Contains("Authentication") == true)
                    .ToList();
                foreach (var service in authServices)
                {
                    services.Remove(service);
                }

                services.Configure<TestAuthOptions>(opts =>
                {
                    opts.Roles = ["CustomerService", "OperationsManager", "SystemAdmin"];
                });

                services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(options =>
                    {
                        options.Cookie.Name = "CritterSupply.Auth";
                        options.Cookie.HttpOnly = true;
                        options.Cookie.SameSite = SameSiteMode.Lax;
                        options.ExpireTimeSpan = TimeSpan.FromDays(7);
                        options.SlidingExpiration = true;
                        options.Events.OnRedirectToLogin = context =>
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        };
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Backoffice", _ => { });

                services.AddAuthorization();
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

    public async Task CleanAddressesAsync()
    {
        using var scope = Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CustomerIdentityDbContext>();

        await dbContext.Addresses.ExecuteDeleteAsync();
    }
}
