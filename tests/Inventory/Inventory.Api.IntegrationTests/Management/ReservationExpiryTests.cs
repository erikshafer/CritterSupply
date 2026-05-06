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
        // Verifies the aggregate-level invariant when two reservations contend
        // for the last available units: the system must not over-reserve, and
        // at least one reservation must succeed.
        //
        // SCOPE NOTE: this test exercises the real `StockReservationRequestedHandler`
        // through `IMessageContext.InvokeAsync` (see TestFixture.ExecuteAndWaitAsync),
        // which serializes execution and surfaces handler exceptions inline. It
        // therefore does NOT engage Wolverine's `OnException<ConcurrencyException>`
        // policy chain — that path is exclusive to the background local-queue
        // executor (PublishAsync / message receipt over a transport).
        //
        // The deterministic proof that exhausted retries land in
        // `inventory.wolverine_dead_letters` (Gap #13 — resolved in M43.1)
        // therefore lives in `Reliability/ConcurrencyExhaustionDlqTests`.
        // This test only asserts what the Invoke path actually exercises:
        // the aggregate's no-over-reservation invariant under contention.

        var sku = "CONCURRENT-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 10));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var reservationId1 = Guid.NewGuid();
        var reservationId2 = Guid.NewGuid();

        // Fire two reservations for exactly the available stock concurrently.
        var task1 = _fixture.ExecuteAndWaitAsync(
            new StockReservationRequested(orderId1, sku, warehouseId, reservationId1, 10));
        var task2 = _fixture.ExecuteAndWaitAsync(
            new StockReservationRequested(orderId2, sku, warehouseId, reservationId2, 10));

        await Task.WhenAll(task1, task2);

        await using var session = _fixture.GetDocumentSession();
        var inv = await session.LoadAsync<ProductInventory>(inventoryId);

        inv.ShouldNotBeNull();

        // Aggregate invariant: at least one succeeded; never over-reserved.
        inv.ReservedQuantity.ShouldBeGreaterThan(0);
        inv.ReservedQuantity.ShouldBeLessThanOrEqualTo(10);
    }
}
