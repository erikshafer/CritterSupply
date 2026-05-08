using Backoffice.Api.OperationsHealth;
using Npgsql;

namespace Backoffice.Api.IntegrationTests.OperationsHealth;

/// <summary>
/// Integration tests for <see cref="GetDeadLetterSummary"/> — the read-only
/// operator-facing surfacing of <c>wolverine_dead_letters</c> across BC
/// schemas, introduced in M46.0 priority D.
///
/// <para>
/// These tests verify the behaviour the PO/QA workshop called out as the
/// minimum bar for "stuck reservation / poisoned envelope is visible to a
/// human" — count + per-schema + per-message-type aggregation, with
/// graceful handling of schemas whose DLQ table doesn't yet exist (which
/// is the normal case in a fresh test fixture, since Wolverine creates the
/// table lazily on first failure).
/// </para>
/// </summary>
[Collection("Backoffice Integration Tests")]
public sealed class GetDeadLetterSummaryTests
{
    private readonly BackofficeTestFixture _fixture;

    public GetDeadLetterSummaryTests(BackofficeTestFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task Returns_Empty_When_No_Schemas_Have_Dead_Letters()
    {
        // Arrange — fresh fixture: ensure the backoffice DLQ table is empty
        // (it might exist from a prior test in the shared collection).
        await EnsureBackofficeDlqEmptyAsync();

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/backoffice/operations/dead-letters/summary");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var response = await result.ReadAsJsonAsync<DeadLetterSummaryResponse>();
        response.ShouldNotBeNull();
        response.TotalDeadLetters.ShouldBe(0);
        response.Schemas.ShouldBeEmpty();
        response.WindowHours.ShouldBe(24); // default
        // SkippedSchemas may contain schemas the test fixture has not created
        // — that's a diagnostic, not a failure. The contract for the
        // endpoint is "operator never sees a 5xx because some BC's schema
        // doesn't exist," and the 200 status above already proves that.
    }

    [Fact]
    public async Task Window_Hours_Is_Clamped_To_Sensible_Range()
    {
        // Below 1 hour → 1
        var lowResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/backoffice/operations/dead-letters/summary?hours=0");
            s.StatusCodeShouldBe(200);
        });
        var lowResponse = await lowResult.ReadAsJsonAsync<DeadLetterSummaryResponse>();
        lowResponse!.WindowHours.ShouldBe(1);

        // Above 168 (one week) → 168
        var highResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/backoffice/operations/dead-letters/summary?hours=10000");
            s.StatusCodeShouldBe(200);
        });
        var highResponse = await highResult.ReadAsJsonAsync<DeadLetterSummaryResponse>();
        highResponse!.WindowHours.ShouldBe(168);
    }

    [Fact]
    public async Task Aggregates_Across_Configured_Schemas()
    {
        // Arrange — write directly to backoffice.wolverine_dead_letters (one of
        // the default schemas) and assert the endpoint sees the rows aggregated
        // by (message_type, exception_type), in the configured time window.
        var now = DateTimeOffset.UtcNow;

        await EnsureBackofficeDlqTableAsync();
        await EnsureBackofficeDlqEmptyAsync();

        await using (var setup = await OpenConnectionAsync())
        {
            await InsertDlqRowAsync(setup,
                "Messages.Contracts.Backoffice.TestEvent",
                "System.InvalidOperationException",
                now.AddMinutes(-2));

            await InsertDlqRowAsync(setup,
                "Messages.Contracts.Backoffice.TestEvent",
                "System.InvalidOperationException",
                now.AddMinutes(-1));

            // This row is outside the default 24h window — must NOT be counted.
            await InsertDlqRowAsync(setup,
                "Messages.Contracts.Backoffice.OldEvent",
                "System.TimeoutException",
                now.AddDays(-3));
        }

        try
        {
            // Act
            var result = await _fixture.Host.Scenario(s =>
            {
                s.Get.Url("/api/backoffice/operations/dead-letters/summary?hours=24");
                s.StatusCodeShouldBe(200);
            });

            var response = await result.ReadAsJsonAsync<DeadLetterSummaryResponse>();
            response.ShouldNotBeNull();

            // Assert — only the in-window backoffice rows are counted.
            response.TotalDeadLetters.ShouldBe(2);
            var backoffice = response.Schemas.ShouldHaveSingleItem();
            backoffice.Schema.ShouldBe("backoffice");
            backoffice.DeadLetterCount.ShouldBe(2);
            var topType = backoffice.TopMessageTypes.ShouldHaveSingleItem();
            topType.MessageType.ShouldBe("Messages.Contracts.Backoffice.TestEvent");
            topType.ExceptionType.ShouldBe("System.InvalidOperationException");
            topType.Count.ShouldBe(2);
        }
        finally
        {
            await EnsureBackofficeDlqEmptyAsync();
        }
    }

    [Fact]
    public void IsValidSchemaIdentifier_RejectsInjectionAttempts()
    {
        // Defense-in-depth — schemas come from configuration but we still
        // validate before interpolating into SQL. These cases assert the
        // allow-list semantics that prevent identifier injection.
        GetDeadLetterSummary.IsValidSchemaIdentifier("orders").ShouldBeTrue();
        GetDeadLetterSummary.IsValidSchemaIdentifier("inventory_v2").ShouldBeTrue();
        GetDeadLetterSummary.IsValidSchemaIdentifier("inventory_99").ShouldBeTrue();

        GetDeadLetterSummary.IsValidSchemaIdentifier("").ShouldBeFalse();
        GetDeadLetterSummary.IsValidSchemaIdentifier(" ").ShouldBeFalse();
        GetDeadLetterSummary.IsValidSchemaIdentifier("Orders").ShouldBeFalse(); // uppercase rejected
        GetDeadLetterSummary.IsValidSchemaIdentifier("orders;DROP TABLE foo").ShouldBeFalse();
        GetDeadLetterSummary.IsValidSchemaIdentifier("orders\"; --").ShouldBeFalse();
        GetDeadLetterSummary.IsValidSchemaIdentifier(new string('a', 64)).ShouldBeFalse(); // too long
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task EnsureBackofficeDlqTableAsync()
    {
        await using var conn = await OpenConnectionAsync();
        await ExecuteAsync(conn, "CREATE SCHEMA IF NOT EXISTS backoffice");
        // We deliberately use the same minimal column set the production
        // endpoint queries (message_type, exception_type, sent_at). If a
        // Wolverine-created table already exists with extra columns, this
        // CREATE TABLE IF NOT EXISTS is a no-op and the existing schema is
        // honored — InsertDlqRowAsync introspects information_schema to
        // build the INSERT against whatever columns are actually present.
        await ExecuteAsync(conn, """
            CREATE TABLE IF NOT EXISTS backoffice.wolverine_dead_letters (
                id UUID PRIMARY KEY,
                message_type TEXT,
                exception_type TEXT,
                sent_at TIMESTAMPTZ
            )
            """);
    }

    private async Task EnsureBackofficeDlqEmptyAsync()
    {
        await using var conn = await OpenConnectionAsync();
        try
        {
            await ExecuteAsync(conn, "DELETE FROM backoffice.wolverine_dead_letters");
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01" || ex.SqlState == "3F000")
        {
            // Table or schema doesn't exist yet — equivalent to empty.
        }
    }

    /// <summary>
    /// Inserts a row into <c>backoffice.wolverine_dead_letters</c> using
    /// only the columns the endpoint cares about (id, message_type,
    /// exception_type, sent_at). Wolverine's auto-created table may carry
    /// additional NOT NULL columns (e.g. execution_time) on certain versions
    /// — we set them to a sentinel default so the insert succeeds without
    /// having to mirror Wolverine's full schema in tests.
    /// </summary>
    private static async Task InsertDlqRowAsync(
        NpgsqlConnection conn,
        string messageType,
        string exceptionType,
        DateTimeOffset sentAt)
    {
        // Discover which columns actually exist so we set defaults for any
        // NOT NULL columns the test doesn't care about.
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var probeCmd = conn.CreateCommand())
        {
            probeCmd.CommandText = """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'backoffice' AND table_name = 'wolverine_dead_letters'
                """;
            await using var reader = await probeCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        var setClauses = new List<string> { "id", "message_type", "exception_type", "sent_at" };
        var setValues = new List<string> { "@id", "@mt", "@et", "@sa" };

        if (columns.Contains("execution_time"))
        {
            setClauses.Add("execution_time");
            setValues.Add("@et_time");
        }
        if (columns.Contains("body"))
        {
            setClauses.Add("body");
            setValues.Add("@body");
        }
        if (columns.Contains("source"))
        {
            setClauses.Add("source");
            setValues.Add("@source");
        }
        if (columns.Contains("replayable"))
        {
            setClauses.Add("replayable");
            setValues.Add("@replayable");
        }

        var sql = $"INSERT INTO backoffice.wolverine_dead_letters ({string.Join(", ", setClauses)}) " +
                  $"VALUES ({string.Join(", ", setValues)})";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("mt", messageType);
        cmd.Parameters.AddWithValue("et", exceptionType);
        cmd.Parameters.AddWithValue("sa", sentAt);
        if (columns.Contains("execution_time")) cmd.Parameters.AddWithValue("et_time", sentAt);
        if (columns.Contains("body")) cmd.Parameters.AddWithValue("body", Array.Empty<byte>());
        if (columns.Contains("source")) cmd.Parameters.AddWithValue("source", "test-fixture");
        if (columns.Contains("replayable")) cmd.Parameters.AddWithValue("replayable", false);

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection conn,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        await cmd.ExecuteNonQueryAsync();
    }
}
