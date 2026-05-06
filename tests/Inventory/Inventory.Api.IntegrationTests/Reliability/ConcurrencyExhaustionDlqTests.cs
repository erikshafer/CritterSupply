using JasperFx;
using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Wolverine;

namespace Inventory.Api.IntegrationTests.Reliability;

/// <summary>
/// M43.1 — Gap #13 regression test.
///
/// Verifies that exhausted <see cref="ConcurrencyException"/> retries land in
/// Wolverine's dead letter envelope store (`inventory.wolverine_dead_letters`)
/// rather than being silently discarded.
///
/// The test sends a probe message whose dedicated handler unconditionally throws
/// <see cref="ConcurrencyException"/>. The Inventory `OnException&lt;ConcurrencyException&gt;`
/// policy chain (`RetryOnce → RetryWithCooldown(100ms,250ms) → MoveToErrorQueue`)
/// is therefore exercised end-to-end. Once retries exhaust, the envelope must be
/// observable in the DLQ table — same shape that
/// <see cref="DeadLetterQueueLogSink"/> reads.
///
/// This test owns its own Postgres container + Alba host so it does NOT pollute
/// the shared <see cref="TestFixture"/> with a throwing handler.
/// </summary>
public sealed class ConcurrencyExhaustionDlqTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("inventory_dlq_test_db")
        .WithName($"inventory-dlq-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private IAlbaHost _host = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        _host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts => opts.Connection(connectionString));

                services.AddAuthorization(opts =>
                {
                    opts.AddPolicy("WarehouseClerk", policy => policy.RequireAssertion(_ => true));
                    opts.AddPolicy("OperationsManager", policy => policy.RequireAssertion(_ => true));
                });

                services.DisableAllExternalWolverineTransports();

                // Register a Wolverine extension that adds the test assembly to
                // handler discovery so ConcurrencyExhaustionProbeHandler is found.
                services.AddSingleton<IWolverineExtension, ProbeHandlerDiscoveryExtension>();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync();
                await _host.DisposeAsync();
            }
            catch (ObjectDisposedException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException))
            {
            }
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrencyException_RetriesExhaust_EnvelopeMovesToDeadLetterQueue()
    {
        var probeId = Guid.NewGuid();

        // Capture the cutoff *before* sending so the DLQ query only sees the new envelope.
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-1);

        // IMessageBus is scoped — resolve it from a DI scope.
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            // Publish (not Invoke) so retries run through Wolverine's local queue
            // pipeline and the policy chain — invoke would surface the exception inline
            // on the first attempt and bypass MoveToErrorQueue.
            await bus.PublishAsync(new ConcurrencyExhaustionProbe(probeId));
        }

        // Policy: RetryOnce → RetryWithCooldown(100ms, 250ms) → MoveToErrorQueue.
        // Total worst-case before DLQ ≈ 350ms of cooldowns plus handler overhead.
        // Poll the DLQ table for up to 15s to absorb CI jitter.
        var envelope = await PollForDeadLetterAsync(cutoff, timeout: TimeSpan.FromSeconds(15));

        envelope.ShouldNotBeNull("Exhausted ConcurrencyException must land in wolverine_dead_letters (Gap #13)");
        envelope.MessageType.ShouldContain(nameof(ConcurrencyExhaustionProbe));
        envelope.ExceptionType.ShouldContain(nameof(ConcurrencyException));
        envelope.ExceptionMessage.ShouldNotBeNullOrEmpty();
    }

    private async Task<DeadLetterRow?> PollForDeadLetterAsync(DateTimeOffset cutoff, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        var store = _host.Services.GetRequiredService<IDocumentStore>();

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var session = store.LightweightSession();
                var conn = session.Connection!;
                await using var cmd = conn.CreateCommand();
                // Wolverine's PG dead letter table schema:
                //   id (uuid), message_type (text), exception_type (text),
                //   exception_message (text), sent_at (timestamptz), …
                // We query the minimum needed to prove the envelope landed
                // and to assert the underlying exception is ConcurrencyException.
                cmd.CommandText = """
                    SELECT id, message_type, exception_type, exception_message
                    FROM inventory.wolverine_dead_letters
                    WHERE message_type LIKE '%ConcurrencyExhaustionProbe%'
                      AND sent_at > @cutoff
                    ORDER BY sent_at DESC
                    LIMIT 1
                    """;
                cmd.Parameters.AddWithValue("cutoff", cutoff);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new DeadLetterRow(
                        reader.GetGuid(0),
                        reader.IsDBNull(1) ? "" : reader.GetString(1),
                        reader.IsDBNull(2) ? "" : reader.GetString(2),
                        reader.IsDBNull(3) ? "" : reader.GetString(3));
                }
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                // Table not yet created by Wolverine — keep polling.
            }

            await Task.Delay(150);
        }

        return null;
    }

    private sealed record DeadLetterRow(Guid Id, string MessageType, string ExceptionType, string ExceptionMessage);
}

/// <summary>
/// Test-only message used purely to trip the Wolverine ConcurrencyException
/// policy chain. Lives in the test assembly so production code is unaffected.
/// </summary>
public sealed record ConcurrencyExhaustionProbe(Guid ProbeId);

/// <summary>
/// Test-only handler that always throws <see cref="ConcurrencyException"/>.
/// Discovered via <see cref="ProbeHandlerDiscoveryExtension"/>.
/// </summary>
public static class ConcurrencyExhaustionProbeHandler
{
    public static void Handle(ConcurrencyExhaustionProbe _)
    {
        throw new ConcurrencyException(
            $"Simulated optimistic concurrency conflict for {nameof(ConcurrencyExhaustionProbe)} (Gap #13 test).");
    }
}

/// <summary>
/// Wolverine extension that includes the test assembly in handler discovery
/// so the probe handler is found. Registered only by
/// <see cref="ConcurrencyExhaustionDlqTests"/>; not active in any other test class.
/// </summary>
internal sealed class ProbeHandlerDiscoveryExtension : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(ConcurrencyExhaustionProbe).Assembly);
    }
}
