using Inventory.Management;
using Messages.Contracts.Fulfillment;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for reservation expiry (Slice 16) and concurrent reservation (Slice 17).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ReservationExpiryTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ReservationExpiryTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // Slice 16: Reservation Expiry
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExpireReservation_ActiveReservation_ExpiresAndReleases()
    {
        var sku = "EXPIRE-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        // Create a reservation
        await _fixture.ExecuteAndWaitAsync(new StockReservationRequested(orderId, sku, warehouseId, reservationId, 30));

        // Verify reservation exists
        await using (var session = _fixture.GetDocumentSession())
        {
            var inv = await session.LoadAsync<ProductInventory>(inventoryId);
            inv!.Reservations.ShouldContainKey(reservationId);
            inv.AvailableQuantity.ShouldBe(70);
        }

        // Fire the expiry directly (in production this is delayed by 30 min)
        var tracked = await _fixture.ExecuteAndWaitAsync(new ExpireReservation(reservationId, inventoryId));

        // Verify reservation expired and stock restored
        await using (var session = _fixture.GetDocumentSession())
        {
            var inv = await session.LoadAsync<ProductInventory>(inventoryId);
            inv.ShouldNotBeNull();
            inv.Reservations.ShouldNotContainKey(reservationId);
            inv.AvailableQuantity.ShouldBe(100); // restored
            inv.TotalOnHand.ShouldBe(100);
        }

        // Verify ReservationExpired event was appended
        await using (var session = _fixture.GetDocumentSession())
        {
            var events = await session.Events.FetchStreamAsync(inventoryId);
            events.ShouldContain(e => e.EventType == typeof(ReservationExpired));
        }
    }

    [Fact]
    public async Task ExpireReservation_AlreadyCommitted_IsNoOp()
    {
        var sku = "EXPIRE-COMM-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        // Reserve, then commit (moves from Reservations → CommittedAllocations)
        await _fixture.ExecuteAndWaitAsync(new StockReservationRequested(orderId, sku, warehouseId, reservationId, 25));
        await _fixture.ExecuteAndWaitAsync(new CommitReservation(inventoryId, reservationId));

        // Expiry arrives late — should be no-op
        await _fixture.ExecuteAndWaitAsync(new ExpireReservation(reservationId, inventoryId));

        await using var session = _fixture.GetDocumentSession();
        var inv = await session.LoadAsync<ProductInventory>(inventoryId);

        inv.ShouldNotBeNull();
        inv.CommittedAllocations.ShouldContainKey(reservationId); // still committed
        inv.AvailableQuantity.ShouldBe(75); // unchanged from commit
    }

    [Fact]
    public async Task ExpireReservation_AlreadyReleased_IsNoOp()
    {
        var sku = "EXPIRE-REL-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        // Reserve, then release
        await _fixture.ExecuteAndWaitAsync(new StockReservationRequested(orderId, sku, warehouseId, reservationId, 20));
        await _fixture.ExecuteAndWaitAsync(new ReleaseReservation(inventoryId, reservationId, "cancelled"));

        // Expiry arrives late — should be no-op
        await _fixture.ExecuteAndWaitAsync(new ExpireReservation(reservationId, inventoryId));

        await using var session = _fixture.GetDocumentSession();
        var inv = await session.LoadAsync<ProductInventory>(inventoryId);

        inv.ShouldNotBeNull();
        inv.Reservations.ShouldBeEmpty();
        inv.AvailableQuantity.ShouldBe(100); // fully restored, not double-restored
    }

    [Fact]
    public async Task StockReservationRequested_SchedulesExpiry()
    {
        var sku = "EXPIRE-SCHED-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));

        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        // Verify that the reservation schedules an ExpireReservation message
        var tracked = await _fixture.ExecuteAndWaitAsync(
            new StockReservationRequested(orderId, sku, warehouseId, reservationId, 10));

        // The tracked session should contain the scheduled ExpireReservation message
        tracked.Sent.SingleMessage<ExpireReservation>().ShouldNotBeNull();
    }

    // ---------------------------------------------------------------------------
    // Slice 17 — Concurrent Reservation Conflict (Gap #13 — resolved in M43.1)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentReservations_LastUnitContention_NoSilentDrop()
    {
        // Verifies that two concurrent reservations for the last available units
        // are handled safely AND that the contention does not produce a silent
        // drop via Wolverine's concurrency-exception policy chain.
        //
        // Gap #13 (resolved in M43.1): the original policy ended in `.Discard()`,
        // which meant a ConcurrencyException after retry exhaustion would silently
        // delete the message. The current policy
        // (RetryOnce → RetryWithCooldown → MoveToErrorQueue) routes exhausted
        // messages to `inventory.wolverine_dead_letters` instead.
        //
        // What this test asserts:
        //   • Aggregate invariant: never over-reserve (ReservedQuantity ≤ stock).
        //   • At least one reservation succeeded.
        //   • For typical 2-way contention (resolved by either RetryOnce or by
        //     the inline insufficient-stock check) NO envelope reaches the DLQ —
        //     i.e. the policy chain absorbs transient races without escalation.
        //
        // The deterministic proof that exhausted retries DO land in DLQ
        // (rather than being silently dropped) lives in
        // `Reliability/ConcurrencyExhaustionDlqTests`.

        var sku = "CONCURRENT-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 10));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var reservationId1 = Guid.NewGuid();
        var reservationId2 = Guid.NewGuid();

        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Fire two reservations for exactly the available stock concurrently.
        var task1 = _fixture.ExecuteAndWaitAsync(
            new StockReservationRequested(orderId1, sku, warehouseId, reservationId1, 10));
        var task2 = _fixture.ExecuteAndWaitAsync(
            new StockReservationRequested(orderId2, sku, warehouseId, reservationId2, 10));

        await Task.WhenAll(task1, task2);

        await using (var session = _fixture.GetDocumentSession())
        {
            var inv = await session.LoadAsync<ProductInventory>(inventoryId);
            inv.ShouldNotBeNull();

            // Aggregate invariant: never over-reserve.
            inv.ReservedQuantity.ShouldBeGreaterThan(0);
            inv.ReservedQuantity.ShouldBeLessThanOrEqualTo(10);
        }

        // No-silent-drop invariant: the deterministic Reliability test proves
        // exhausted retries land in DLQ. Here we additionally guard that this
        // *normal* 2-way race does not escalate to DLQ — i.e. the retry budget
        // (RetryOnce → RetryWithCooldown(100ms,250ms)) absorbs the race.
        var deadLetteredCount = await CountDeadLettersForReservationAsync(cutoff);
        deadLetteredCount.ShouldBe(
            0,
            $"Two-way reservation contention should be absorbed by the retry chain, " +
            $"not escalated to DLQ. dead-lettered={deadLetteredCount}");
    }

    private async Task<int> CountDeadLettersForReservationAsync(DateTimeOffset cutoff)
    {
        var store = _fixture.GetDocumentStore();
        try
        {
            await using var session = store.LightweightSession();
            var conn = session.Connection!;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*)
                FROM inventory.wolverine_dead_letters
                WHERE message_type LIKE '%StockReservationRequested%'
                  AND sent_at > @cutoff
                """;
            cmd.Parameters.AddWithValue("cutoff", cutoff);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result ?? 0);
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Table not yet created by Wolverine — no dead letters means none.
            return 0;
        }
    }
}
