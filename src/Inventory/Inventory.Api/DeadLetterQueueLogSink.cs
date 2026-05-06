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

    /// <summary>
    /// SQL used to read recent dead letter envelopes. Exposed for regression
    /// testing — the column names here must stay aligned with Wolverine's
    /// actual `wolverine_dead_letters` schema. M43.1 fixed a months-old
    /// silent-bug where this query referenced columns (`explanation`, `source`)
    /// that didn't exist; PostgresException 42703 was caught by the broad
    /// `catch (Exception)` below and only logged at warning level, so the bug
    /// was invisible. Any future schema drift is now caught by
    /// `Reliability/DeadLetterQueueLogSinkSqlTests`.
    /// </summary>
    public const string PollSql = """
        SELECT id, message_type, exception_type, exception_message, source, sent_at
        FROM inventory.wolverine_dead_letters
        WHERE sent_at > @cutoff
        ORDER BY sent_at DESC
        LIMIT 50
        """;

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
        // SQL lives in `PollSql` so its schema can be regression-tested.
        cmd.CommandText = PollSql;
        cmd.Parameters.AddWithValue("cutoff", cutoff);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var count = 0;

            while (await reader.ReadAsync(ct))
            {
                var envelopeId = reader.GetGuid(0);
                var messageType = reader.IsDBNull(1) ? "unknown" : reader.GetString(1);
                var exceptionType = reader.IsDBNull(2) ? "unknown" : reader.GetString(2);
                var exceptionMessage = reader.IsDBNull(3) ? "no message" : reader.GetString(3);
                var source = reader.IsDBNull(4) ? "unknown" : reader.GetString(4);

                _logger.LogWarning(
                    "Dead letter envelope detected — EnvelopeId: {EnvelopeId}, MessageType: {MessageType}, " +
                    "Source: {Source}, ExceptionType: {ExceptionType}, ExceptionMessage: {ExceptionMessage}",
                    envelopeId, messageType, source, exceptionType, exceptionMessage);

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
