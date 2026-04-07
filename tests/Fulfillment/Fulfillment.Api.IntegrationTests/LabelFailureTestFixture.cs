using CritterSupply.TestUtilities;
using Fulfillment.Shipments;
using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace Fulfillment.Api.IntegrationTests;

/// <summary>
/// Test fixture for Slice 22 — label generation failure.
/// Registers AlwaysFailingCarrierLabelService to simulate carrier API failures.
/// </summary>
public class LabelFailureTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fulfillment_label_fail_test_db")
        .WithName($"fulfillment-label-fail-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var connectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(connectionString);
                });

                services.DisableAllExternalWolverineTransports();

                services.AddTestAuthentication(
                    roles: ["CustomerService", "WarehouseClerk", "OperationsManager", "SystemAdmin", "VendorAdmin"],
                    schemes: ["Backoffice", "Vendor"]);

                // Override with always-failing carrier label service
                services.RemoveAll<ICarrierLabelService>();
                services.AddScoped<ICarrierLabelService, AlwaysFailingCarrierLabelService>();
            });
        });

        Host.AddDefaultAuthHeader();
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
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException)) { }
        }

        await _postgres.DisposeAsync();
    }

    public IDocumentSession GetDocumentSession()
    {
        return Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
    }

    public async Task CleanAllDocumentsAsync()
    {
        var store = Host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
    }

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
}

[CollectionDefinition(Name)]
public class LabelFailureTestCollection : ICollectionFixture<LabelFailureTestFixture>
{
    public const string Name = "Label Failure Tests";
}
