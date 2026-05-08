using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using Wolverine.Http;

namespace Backoffice.Api.OperationsHealth;

/// <summary>
/// Read-only summary of Wolverine dead-letter envelopes across every BC
/// schema configured for the platform.
///
/// <para>
/// <b>Why.</b> Until M46.0 the only thing observing
/// <c>wolverine_dead_letters</c> was <c>DeadLetterQueueLogSink</c> in
/// <c>Inventory.Api</c> — a per-process polled log writer. A stuck
/// reservation, an exhausted concurrency retry, or a poisoned payment
/// envelope landed in the database and the only signal was a buried
/// <c>WARN</c> in container logs. The first human signal was a customer
/// support ticket. This endpoint is the minimum viable
/// <i>operator-facing</i> surfacing: count + top message types per BC over
/// the last <i>N</i> hours.
/// </para>
///
/// <para>
/// <b>Scope.</b> Read-only by design (per UXE §7.2 of
/// <c>state-of-repo-2026-05.md</c>: "ship v1 read-only behind Backoffice
/// Identity, instrument which envelopes operators inspect, then design
/// replay v2 from real usage"). No envelope replay, no destructive action.
/// </para>
///
/// <para>
/// <b>Multi-schema query.</b> Each BC owns its own Postgres schema
/// (<c>orders</c>, <c>inventory</c>, etc.) with its own
/// <c>wolverine_dead_letters</c> table. The schemas to inspect are read
/// from <c>OperationsHealth:DeadLetterSchemas</c> in configuration; if the
/// section is absent, a built-in default covering the 18 BC schemas is
/// used. Schemas where the table does not yet exist
/// (Wolverine creates it lazily on first failure) are silently skipped —
/// the absence of a table is interpreted as "no dead letters in this BC,"
/// which is the operationally correct read.
/// </para>
/// </summary>
public static class GetDeadLetterSummary
{
    /// <summary>
    /// Default schemas to inspect when configuration does not override.
    /// Mirrors the 18 BC schemas declared across <c>src/*/Program.cs</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultSchemas = new[]
    {
        "backoffice",
        "backofficeidentity",
        "correspondence",
        "customeridentity",
        "fulfillment",
        "inventory",
        "listings",
        "marketplaces",
        "orders",
        "payments",
        "pricing",
        "productcatalog",
        "promotions",
        "returns",
        "shopping",
        "storefront",
        "vendoridentity",
        "vendorportal",
    };

    /// <summary>
    /// Per-schema dead-letter aggregation query. Parameters: <c>@cutoff</c>
    /// timestamp. Returns rows of (message_type, exception_type, count).
    /// Schema name is interpolated (validated against an allow-list before
    /// the call — see <see cref="IsValidSchemaIdentifier"/>); parameters
    /// are still bound for the cutoff.
    /// </summary>
    internal const string PerSchemaQuery = """
        SELECT message_type, exception_type, COUNT(*) AS occurrence_count
        FROM {0}.wolverine_dead_letters
        WHERE sent_at > @cutoff
        GROUP BY message_type, exception_type
        ORDER BY occurrence_count DESC
        LIMIT 100
        """;

    [WolverineGet("/api/backoffice/operations/dead-letters/summary")]
    [Authorize(Policy = "OperationsManager")]
    public static async Task<Ok<DeadLetterSummaryResponse>> Get(
        int? hours,
        IConfiguration configuration,
        ILogger<DeadLetterSummaryMarker> logger,
        CancellationToken ct)
    {
        var window = TimeSpan.FromHours(Math.Clamp(hours ?? 24, 1, 168));
        var cutoff = DateTimeOffset.UtcNow - window;

        var schemas = ResolveSchemas(configuration);
        var perSchema = new List<DeadLetterSchemaSummary>(schemas.Count);
        var totalDeadLetters = 0;
        var skippedSchemas = new List<string>();
        var swStart = Stopwatch.GetTimestamp();

        // Open a fresh standalone Postgres connection (independent of any
        // Marten session transaction) so per-schema query failures don't
        // poison the connection state for the rest of the loop.
        var connectionString = configuration.GetConnectionString("postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:postgres is required for the dead-letter summary endpoint.");

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        foreach (var schema in schemas)
        {
            // Defense in depth — schemas come from configuration. Treat any
            // value that isn't a plain Postgres identifier as poisoned.
            if (!IsValidSchemaIdentifier(schema))
            {
                logger.LogWarning(
                    "Skipping invalid schema identifier {Schema} from configuration",
                    schema);
                skippedSchemas.Add(schema);
                continue;
            }

            try
            {
                var rows = await QuerySchemaAsync(conn, schema, cutoff, ct);
                var schemaCount = rows.Sum(r => r.Count);
                totalDeadLetters += schemaCount;

                if (schemaCount > 0)
                {
                    perSchema.Add(new DeadLetterSchemaSummary(
                        schema,
                        schemaCount,
                        rows));
                }
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                // undefined_table — Wolverine has not yet created the DLQ
                // table in this schema. Operationally this means "no dead
                // letters here" — skip silently.
            }
            catch (PostgresException ex) when (ex.SqlState == "3F000")
            {
                // invalid_schema_name — the BC's schema itself doesn't
                // exist in this database (e.g., a fresh test fixture). Skip.
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to query dead letters for schema {Schema} — skipping for this poll",
                    schema);
                skippedSchemas.Add(schema);
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(swStart);

        return TypedResults.Ok(new DeadLetterSummaryResponse(
            QueriedAt: DateTimeOffset.UtcNow,
            WindowHours: (int)window.TotalHours,
            TotalDeadLetters: totalDeadLetters,
            Schemas: perSchema
                .OrderByDescending(s => s.DeadLetterCount)
                .ToList(),
            SkippedSchemas: skippedSchemas,
            QueryDurationMs: (int)elapsed.TotalMilliseconds));
    }

    private static IReadOnlyList<string> ResolveSchemas(IConfiguration configuration)
    {
        var configured = configuration
            .GetSection("OperationsHealth:DeadLetterSchemas")
            .Get<string[]>();

        return configured is { Length: > 0 } ? configured : DefaultSchemas;
    }

    /// <summary>
    /// Validates a string as a safe Postgres identifier we are willing to
    /// interpolate into a query. Restricts to lowercase letters, digits,
    /// and underscore — same alphabet our BC schema constants live in.
    /// </summary>
    internal static bool IsValidSchemaIdentifier(string schema)
    {
        if (string.IsNullOrWhiteSpace(schema) || schema.Length > 63)
        {
            return false;
        }

        foreach (var ch in schema)
        {
            var ok = (ch >= 'a' && ch <= 'z')
                  || (ch >= '0' && ch <= '9')
                  || ch == '_';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<List<DeadLetterMessageTypeCount>> QuerySchemaAsync(
        NpgsqlConnection conn,
        string schema,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = string.Format(PerSchemaQuery, schema);
        cmd.Parameters.AddWithValue("cutoff", cutoff);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<DeadLetterMessageTypeCount>();
        while (await reader.ReadAsync(ct))
        {
            var messageType = reader.IsDBNull(0) ? "unknown" : reader.GetString(0);
            var exceptionType = reader.IsDBNull(1) ? "unknown" : reader.GetString(1);
            var count = reader.GetInt64(2);
            rows.Add(new DeadLetterMessageTypeCount(messageType, exceptionType, (int)count));
        }
        return rows;
    }
}

/// <summary>
/// Marker type used purely so <see cref="ILogger{TCategoryName}"/> resolves
/// to a stable category name for this endpoint.
/// </summary>
public sealed class DeadLetterSummaryMarker;

/// <summary>
/// Top-level response for <c>GET /api/backoffice/operations/dead-letters/summary</c>.
/// </summary>
public sealed record DeadLetterSummaryResponse(
    DateTimeOffset QueriedAt,
    int WindowHours,
    int TotalDeadLetters,
    IReadOnlyList<DeadLetterSchemaSummary> Schemas,
    IReadOnlyList<string> SkippedSchemas,
    int QueryDurationMs);

/// <summary>Per-BC schema dead-letter breakdown.</summary>
public sealed record DeadLetterSchemaSummary(
    string Schema,
    int DeadLetterCount,
    IReadOnlyList<DeadLetterMessageTypeCount> TopMessageTypes);

/// <summary>One row of the per-schema breakdown.</summary>
public sealed record DeadLetterMessageTypeCount(
    string MessageType,
    string ExceptionType,
    int Count);
