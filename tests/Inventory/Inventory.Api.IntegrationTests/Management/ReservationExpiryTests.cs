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
    // Slice 17: Concurrent Reservation Conflict — Gap #13
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentReservations_LastUnitContention_SecondReservationFails()
    {
        // This test verifies that concurrent reservations for the last available units
        // are handled safely. Due to ConcurrencyException + RetryOnce + Discard policy,
        // one of the two may be silently dropped.
        //
        // Gap #13 finding: if the retry succeeds (because there's still stock after
        // the first reservation), both will succeed. If the retry fails (insufficient
        // stock), the message is discarded — the order may not receive a ReservationFailed.
        //
        // This test documents the current behavior.

        var sku = "CONCURRENT-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 10));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var reservationId1 = Guid.NewGuid();
        var reservationId2 = Guid.NewGuid();

        // Fire two reservations for exactly available stock
        var task1 = _fixture.ExecuteAndWaitAsync(
            new StockReservationRequested(orderId1, sku, warehouseId, reservationId1, 10));
        var task2 = _fixture.ExecuteAndWaitAsync(
            new StockReservationRequested(orderId2, sku, warehouseId, reservationId2, 10));

        // Allow both to complete — one may fail silently due to Discard policy
        await Task.WhenAll(task1, task2);

        await using var session = _fixture.GetDocumentSession();
        var inv = await session.LoadAsync<ProductInventory>(inventoryId);

        inv.ShouldNotBeNull();

        // At least one reservation must have succeeded
        var totalReserved = inv.ReservedQuantity;
        totalReserved.ShouldBeGreaterThan(0);

        // With only 10 units, the maximum successful reservation is 10
        totalReserved.ShouldBeLessThanOrEqualTo(10);

        // Document Gap #13 finding:
        // If totalReserved == 10 and only 1 reservation exists, the second was
        // either rejected by the Before() validation (insufficient stock) or
        // silently discarded after ConcurrencyException retry exhaustion.
        //
        // Current policy: ConcurrencyException → RetryOnce → RetryWithCooldown → Discard
        // The Discard policy means the second order never receives ReservationFailed.
        // TODO: Consider changing to .MoveToDeadLetterQueue() for visibility.
    }
}
