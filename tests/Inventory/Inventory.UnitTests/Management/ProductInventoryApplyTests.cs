namespace Inventory.UnitTests.Management;

/// <summary>
/// Unit tests for all <see cref="ProductInventory.Apply"/> overloads:
/// <see cref="StockReserved"/>, <see cref="ReservationCommitted"/>,
/// <see cref="ReservationReleased"/>, <see cref="StockReceived"/>,
/// and <see cref="StockRestocked"/>.
///
/// Each test section covers the happy path, the idempotency/safety guard where applicable,
/// and verifies that unrelated state buckets remain unchanged.
/// </summary>
public class ProductInventoryApplyTests
{
    // ---------------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------------

    private const string DefaultSku = "CAT-FOOD-001";
    private const string DefaultWarehouseId = "WH-EAST-01";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    /// <summary>
    /// Builds a <see cref="ProductInventory"/> with sensible defaults ready for Apply() tests.
    /// </summary>
    private static ProductInventory BuildInventory(int availableQuantity = 100) =>
        ProductInventory.Create(
            new InventoryInitialized(DefaultSku, DefaultWarehouseId, availableQuantity, Now));

    // ---------------------------------------------------------------------------
    // Apply(StockReserved)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// StockReserved decrements AvailableQuantity by the event Quantity.
    /// </summary>
    [Fact]
    public void Apply_StockReserved_Decrements_AvailableQuantity()
    {
        var inventory = BuildInventory(availableQuantity: 100);
        var @event = new StockReserved(Guid.NewGuid(), Guid.NewGuid(), Quantity: 30, Now);

        var result = inventory.Apply(@event);

        result.AvailableQuantity.ShouldBe(70);
    }

    /// <summary>
    /// StockReserved adds a new entry in Reservations keyed by ReservationId.
    /// </summary>
    [Fact]
    public void Apply_StockReserved_Adds_Entry_To_Reservations()
    {
        var inventory = BuildInventory();
        var reservationId = Guid.NewGuid();
        var @event = new StockReserved(Guid.NewGuid(), reservationId, Quantity: 20, Now);

        var result = inventory.Apply(@event);

        result.Reservations.ShouldContainKey(reservationId);
        result.Reservations[reservationId].ShouldBe(20);
    }

    /// <summary>
    /// StockReserved records the mapping from ReservationId to OrderId in ReservationOrderIds.
    /// </summary>
    [Fact]
    public void Apply_StockReserved_Adds_Entry_To_ReservationOrderIds()
    {
        var inventory = BuildInventory();
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var @event = new StockReserved(orderId, reservationId, Quantity: 10, Now);

        var result = inventory.Apply(@event);

        result.ReservationOrderIds.ShouldContainKey(reservationId);
        result.ReservationOrderIds[reservationId].ShouldBe(orderId);
    }

    /// <summary>
    /// StockReserved does NOT modify CommittedAllocations.
    /// </summary>
    [Fact]
    public void Apply_StockReserved_Does_Not_Modify_CommittedAllocations()
    {
        var inventory = BuildInventory();
        var @event = new StockReserved(Guid.NewGuid(), Guid.NewGuid(), Quantity: 5, Now);

        var result = inventory.Apply(@event);

        result.CommittedAllocations.ShouldBeEmpty();
    }

    /// <summary>
    /// Applying two StockReserved events accumulates both entries in Reservations
    /// and decrements AvailableQuantity by the total reserved.
    /// </summary>
    [Fact]
    public void Apply_StockReserved_Twice_Accumulates_Both_Reservations()
    {
        var inventory = BuildInventory(availableQuantity: 100);
        var resId1 = Guid.NewGuid();
        var resId2 = Guid.NewGuid();

        var result = inventory
            .Apply(new StockReserved(Guid.NewGuid(), resId1, Quantity: 15, Now))
            .Apply(new StockReserved(Guid.NewGuid(), resId2, Quantity: 25, Now));

        result.AvailableQuantity.ShouldBe(60);
        result.Reservations.ShouldContainKey(resId1);
        result.Reservations.ShouldContainKey(resId2);
        result.Reservations[resId1].ShouldBe(15);
        result.Reservations[resId2].ShouldBe(25);
    }

    // ---------------------------------------------------------------------------
    // Apply(ReservationCommitted)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// ReservationCommitted removes the ReservationId from Reservations.
    /// </summary>
    [Fact]
    public void Apply_ReservationCommitted_Removes_From_Reservations()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory()
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, Quantity: 20, Now));

        var result = inventory.Apply(new ReservationCommitted(reservationId, Now));

        result.Reservations.ShouldNotContainKey(reservationId);
    }

    /// <summary>
    /// ReservationCommitted moves the reserved quantity into CommittedAllocations.
    /// </summary>
    [Fact]
    public void Apply_ReservationCommitted_Adds_To_CommittedAllocations()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory()
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, Quantity: 20, Now));

        var result = inventory.Apply(new ReservationCommitted(reservationId, Now));

        result.CommittedAllocations.ShouldContainKey(reservationId);
        result.CommittedAllocations[reservationId].ShouldBe(20);
    }

    /// <summary>
    /// ReservationCommitted does NOT change AvailableQuantity (stock was already decremented on reserve).
    /// </summary>
    [Fact]
    public void Apply_ReservationCommitted_Does_Not_Change_AvailableQuantity()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory(availableQuantity: 100)
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, Quantity: 30, Now));

        // AvailableQuantity is 70 after reserve
        var result = inventory.Apply(new ReservationCommitted(reservationId, Now));

        result.AvailableQuantity.ShouldBe(70);
    }

    /// <summary>
    /// Safety guard: ReservationCommitted with an unknown ReservationId returns the inventory unchanged.
    /// </summary>
    [Fact]
    public void Apply_ReservationCommitted_Returns_Unchanged_When_ReservationId_Not_Found()
    {
        var inventory = BuildInventory(availableQuantity: 100);
        var unknownId = Guid.NewGuid();

        var result = inventory.Apply(new ReservationCommitted(unknownId, Now));

        // Returns the exact same instance (ReferenceEquals guard for the 'return this' branch)
        result.ShouldBeSameAs(inventory);
    }

    /// <summary>
    /// ReservationCommitted only removes the targeted reservation, leaving others intact.
    /// </summary>
    [Fact]
    public void Apply_ReservationCommitted_Leaves_Other_Reservations_Intact()
    {
        var resId1 = Guid.NewGuid();
        var resId2 = Guid.NewGuid();
        var inventory = BuildInventory(availableQuantity: 100)
            .Apply(new StockReserved(Guid.NewGuid(), resId1, Quantity: 10, Now))
            .Apply(new StockReserved(Guid.NewGuid(), resId2, Quantity: 20, Now));

        var result = inventory.Apply(new ReservationCommitted(resId1, Now));

        result.Reservations.ShouldNotContainKey(resId1);
        result.Reservations.ShouldContainKey(resId2);
        result.Reservations[resId2].ShouldBe(20);
    }

    // ---------------------------------------------------------------------------
    // Apply(ReservationReleased)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// ReservationReleased removes the ReservationId from Reservations.
    /// </summary>
    [Fact]
    public void Apply_ReservationReleased_Removes_From_Reservations()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory()
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, Quantity: 15, Now));

        var result = inventory.Apply(
            new ReservationReleased(reservationId, Quantity: 15, Reason: "cancelled", Now));

        result.Reservations.ShouldNotContainKey(reservationId);
    }

    /// <summary>
    /// ReservationReleased increments AvailableQuantity by the previously reserved quantity
    /// (taken from the Reservations dict, not the event's Quantity field).
    /// </summary>
    [Fact]
    public void Apply_ReservationReleased_Restores_AvailableQuantity()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory(availableQuantity: 100)
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, Quantity: 40, Now));

        // AvailableQuantity is 60 after reserve
        var result = inventory.Apply(
            new ReservationReleased(reservationId, Quantity: 40, Reason: "abandoned", Now));

        result.AvailableQuantity.ShouldBe(100);
    }

    /// <summary>
    /// ReservationReleased does NOT modify CommittedAllocations.
    /// </summary>
    [Fact]
    public void Apply_ReservationReleased_Does_Not_Modify_CommittedAllocations()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory()
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, Quantity: 10, Now));

        var result = inventory.Apply(
            new ReservationReleased(reservationId, Quantity: 10, Reason: "cancelled", Now));

        result.CommittedAllocations.ShouldBeEmpty();
    }

    /// <summary>
    /// Safety guard: ReservationReleased with an unknown ReservationId returns the inventory unchanged.
    /// </summary>
    [Fact]
    public void Apply_ReservationReleased_Returns_Unchanged_When_ReservationId_Not_Found()
    {
        var inventory = BuildInventory(availableQuantity: 100);
        var unknownId = Guid.NewGuid();

        var result = inventory.Apply(
            new ReservationReleased(unknownId, Quantity: 10, Reason: "ghost", Now));

        result.ShouldBeSameAs(inventory);
    }

    /// <summary>
    /// ReservationReleased only removes the targeted reservation, leaving other reservations intact.
    /// </summary>
    [Fact]
    public void Apply_ReservationReleased_Leaves_Other_Reservations_Intact()
    {
        var resId1 = Guid.NewGuid();
        var resId2 = Guid.NewGuid();
        var inventory = BuildInventory(availableQuantity: 100)
            .Apply(new StockReserved(Guid.NewGuid(), resId1, Quantity: 10, Now))
            .Apply(new StockReserved(Guid.NewGuid(), resId2, Quantity: 20, Now));

        var result = inventory.Apply(
            new ReservationReleased(resId1, Quantity: 10, Reason: "cancelled", Now));

        result.Reservations.ShouldNotContainKey(resId1);
        result.Reservations.ShouldContainKey(resId2);
        result.Reservations[resId2].ShouldBe(20);
    }

    // ---------------------------------------------------------------------------
    // Apply(StockReceived)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// StockReceived increments AvailableQuantity by the event Quantity.
    /// </summary>
    [Fact]
    public void Apply_StockReceived_Increments_AvailableQuantity()
    {
        var inventory = BuildInventory(availableQuantity: 50);
        var @event = new StockReceived(Quantity: 75, Source: "Supplier-A", Now);

        var result = inventory.Apply(@event);

        result.AvailableQuantity.ShouldBe(125);
    }

    /// <summary>
    /// StockReceived does NOT modify Reservations.
    /// </summary>
    [Fact]
    public void Apply_StockReceived_Does_Not_Modify_Reservations()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory()
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, Quantity: 10, Now));

        var result = inventory.Apply(new StockReceived(Quantity: 50, Source: "Supplier-B", Now));

        result.Reservations.ShouldContainKey(reservationId);
        result.Reservations[reservationId].ShouldBe(10);
    }

    /// <summary>
    /// StockReceived does NOT modify CommittedAllocations.
    /// </summary>
    [Fact]
    public void Apply_StockReceived_Does_Not_Modify_CommittedAllocations()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory()
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, Quantity: 20, Now))
            .Apply(new ReservationCommitted(reservationId, Now));

        var committed = inventory.CommittedAllocations[reservationId];

        var result = inventory.Apply(new StockReceived(Quantity: 30, Source: "Transfer", Now));

        result.CommittedAllocations[reservationId].ShouldBe(committed);
    }

    /// <summary>
    /// StockReceived does NOT modify ReservationOrderIds.
    /// </summary>
    [Fact]
    public void Apply_StockReceived_Does_Not_Modify_ReservationOrderIds()
    {
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var inventory = BuildInventory()
            .Apply(new StockReserved(orderId, reservationId, Quantity: 5, Now));

        var result = inventory.Apply(new StockReceived(Quantity: 10, Source: "Warehouse", Now));

        result.ReservationOrderIds[reservationId].ShouldBe(orderId);
    }

    // ---------------------------------------------------------------------------
    // Apply(StockRestocked)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// StockRestocked increments AvailableQuantity by the event Quantity.
    /// </summary>
    [Fact]
    public void Apply_StockRestocked_Increments_AvailableQuantity()
    {
        var inventory = BuildInventory(availableQuantity: 20);
        var @event = new StockRestocked(Guid.NewGuid(), Quantity: 30, Now);

        var result = inventory.Apply(@event);

        result.AvailableQuantity.ShouldBe(50);
    }

    /// <summary>
    /// StockRestocked does NOT modify Reservations.
    /// </summary>
    [Fact]
    public void Apply_StockRestocked_Does_Not_Modify_Reservations()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory()
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, Quantity: 10, Now));

        var result = inventory.Apply(new StockRestocked(Guid.NewGuid(), Quantity: 25, Now));

        result.Reservations.ShouldContainKey(reservationId);
        result.Reservations[reservationId].ShouldBe(10);
    }

    /// <summary>
    /// StockRestocked does NOT modify CommittedAllocations.
    /// </summary>
    [Fact]
    public void Apply_StockRestocked_Does_Not_Modify_CommittedAllocations()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory()
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, Quantity: 15, Now))
            .Apply(new ReservationCommitted(reservationId, Now));

        var committed = inventory.CommittedAllocations[reservationId];

        var result = inventory.Apply(new StockRestocked(Guid.NewGuid(), Quantity: 50, Now));

        result.CommittedAllocations[reservationId].ShouldBe(committed);
    }

    /// <summary>
    /// StockRestocked preserves ReservedQuantity and CommittedQuantity unchanged.
    /// </summary>
    [Fact]
    public void Apply_StockRestocked_Does_Not_Change_Reserved_Or_Committed_Quantities()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory(availableQuantity: 100)
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, Quantity: 10, Now));

        var reservedBefore   = inventory.ReservedQuantity;
        var committedBefore  = inventory.CommittedQuantity;

        var result = inventory.Apply(new StockRestocked(Guid.NewGuid(), Quantity: 50, Now));

        result.ReservedQuantity.ShouldBe(reservedBefore);
        result.CommittedQuantity.ShouldBe(committedBefore);
    }
}
