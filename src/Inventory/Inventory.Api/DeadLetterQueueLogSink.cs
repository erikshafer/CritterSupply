using Npgsql;
using Wolverine;

namespace Inventory;

/// <summary>
/// Minimal ILogger-based observer for Wolverine dead letter queue entries.
/// Periodically queries the envelope storage for dead letter envelopes and logs
/// new entries with envelope ID, message type, and exception details.
///
/// This is a minimal observation layer — production alerting is an Operations BC concern.
/// See S4 retrospective for the explicit handoff.
/// </summary>
public sealed class DeadLetterQueueLogSink : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DeadLetterQueueLogSink> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(60);
    private DateTimeOffset _lastChecked = DateTimeOffset.UtcNow;

    public DeadLetterQueueLogSink(
        IServiceProvider services,
        ILogger<DeadLetterQueueLogSink> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Allow the application to fully start before polling
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        _logger.LogInformation(
            "DeadLetterQueueLogSink started — polling every {IntervalSeconds}s for dead letter envelopes",
            _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollDeadLettersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DeadLetterQueueLogSink poll cycle failed — will retry next interval");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private async Task PollDeadLettersAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var dataSource = scope.ServiceProvider.GetService<NpgsqlDataSource>();
        if (dataSource is null)
        {
            _logger.LogDebug("NpgsqlDataSource not available — skipping DLQ poll");
            return;
        }

        var cutoff = _lastChecked;
        _lastChecked = DateTimeOffset.UtcNow;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        // Query Wolverine's dead letter envelope table for entries since last check.
        // Table structure: wolverine stores dead letters with id, message_type, explanation, etc.
        // The table name follows Wolverine's schema convention.
        cmd.CommandText = """
            SELECT id, message_type, explanation, source, sent_at
            FROM inventory.wolverine_dead_letters
            WHERE sent_at > @cutoff
            ORDER BY sent_at DESC
            LIMIT 50
            """;
        cmd.Parameters.AddWithValue("cutoff", cutoff);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var count = 0;

            while (await reader.ReadAsync(ct))
            {
                var envelopeId = reader.GetGuid(0);
                var messageType = reader.IsDBNull(1) ? "unknown" : reader.GetString(1);
                var explanation = reader.IsDBNull(2) ? "no explanation" : reader.GetString(2);
                var source = reader.IsDBNull(3) ? "unknown" : reader.GetString(3);

                _logger.LogWarning(
                    "Dead letter envelope detected — EnvelopeId: {EnvelopeId}, MessageType: {MessageType}, " +
                    "Source: {Source}, Explanation: {Explanation}",
                    envelopeId, messageType, source, explanation);

                count++;
            }

            if (count > 0)
            {
                _logger.LogWarning(
                    "DeadLetterQueueLogSink found {Count} new dead letter envelope(s) since {Cutoff}",
                    count, cutoff);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // undefined_table
        {
            // Table doesn't exist yet — Wolverine creates it on first use.
            // This is normal during startup or in test environments.
            _logger.LogDebug("Wolverine dead letter table not yet created — skipping poll");
        }
    }
}
