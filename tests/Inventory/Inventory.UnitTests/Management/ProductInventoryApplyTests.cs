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
        var @event = new StockReserved(Guid.NewGuid(), Guid.NewGuid(), DefaultSku, DefaultWarehouseId, Quantity: 30, Now);

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
        var @event = new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 20, Now);

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
        var @event = new StockReserved(orderId, reservationId, DefaultSku, DefaultWarehouseId, Quantity: 10, Now);

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
        var @event = new StockReserved(Guid.NewGuid(), Guid.NewGuid(), DefaultSku, DefaultWarehouseId, Quantity: 5, Now);

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
            .Apply(new StockReserved(Guid.NewGuid(), resId1, DefaultSku, DefaultWarehouseId, Quantity: 15, Now))
            .Apply(new StockReserved(Guid.NewGuid(), resId2, DefaultSku, DefaultWarehouseId, Quantity: 25, Now));

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
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 20, Now));

        var result = inventory.Apply(new ReservationCommitted(reservationId, DefaultSku, DefaultWarehouseId, Now));

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
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 20, Now));

        var result = inventory.Apply(new ReservationCommitted(reservationId, DefaultSku, DefaultWarehouseId, Now));

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
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 30, Now));

        // AvailableQuantity is 70 after reserve
        var result = inventory.Apply(new ReservationCommitted(reservationId, DefaultSku, DefaultWarehouseId, Now));

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

        var result = inventory.Apply(new ReservationCommitted(unknownId, DefaultSku, DefaultWarehouseId, Now));

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
            .Apply(new StockReserved(Guid.NewGuid(), resId1, DefaultSku, DefaultWarehouseId, Quantity: 10, Now))
            .Apply(new StockReserved(Guid.NewGuid(), resId2, DefaultSku, DefaultWarehouseId, Quantity: 20, Now));

        var result = inventory.Apply(new ReservationCommitted(resId1, DefaultSku, DefaultWarehouseId, Now));

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
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 15, Now));

        var result = inventory.Apply(
            new ReservationReleased(reservationId, DefaultSku, DefaultWarehouseId, Quantity: 15, Reason: "cancelled", Now));

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
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 40, Now));

        // AvailableQuantity is 60 after reserve
        var result = inventory.Apply(
            new ReservationReleased(reservationId, DefaultSku, DefaultWarehouseId, Quantity: 40, Reason: "abandoned", Now));

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
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 10, Now));

        var result = inventory.Apply(
            new ReservationReleased(reservationId, DefaultSku, DefaultWarehouseId, Quantity: 10, Reason: "cancelled", Now));

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
            new ReservationReleased(unknownId, DefaultSku, DefaultWarehouseId, Quantity: 10, Reason: "ghost", Now));

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
            .Apply(new StockReserved(Guid.NewGuid(), resId1, DefaultSku, DefaultWarehouseId, Quantity: 10, Now))
            .Apply(new StockReserved(Guid.NewGuid(), resId2, DefaultSku, DefaultWarehouseId, Quantity: 20, Now));

        var result = inventory.Apply(
            new ReservationReleased(resId1, DefaultSku, DefaultWarehouseId, Quantity: 10, Reason: "cancelled", Now));

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
        var @event = new StockReceived(DefaultSku, DefaultWarehouseId, "Supplier-A", null, Quantity: 75, Now);

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
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 10, Now));

        var result = inventory.Apply(new StockReceived(DefaultSku, DefaultWarehouseId, "Supplier-B", null, Quantity: 50, Now));

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
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 20, Now))
            .Apply(new ReservationCommitted(reservationId, DefaultSku, DefaultWarehouseId, Now));

        var committed = inventory.CommittedAllocations[reservationId];

        var result = inventory.Apply(new StockReceived(DefaultSku, DefaultWarehouseId, "Transfer", null, Quantity: 30, Now));

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
            .Apply(new StockReserved(orderId, reservationId, DefaultSku, DefaultWarehouseId, Quantity: 5, Now));

        var result = inventory.Apply(new StockReceived(DefaultSku, DefaultWarehouseId, "Warehouse", null, Quantity: 10, Now));

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
        var @event = new StockRestocked(DefaultSku, DefaultWarehouseId, Guid.NewGuid(), Quantity: 30, Now);

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
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 10, Now));

        var result = inventory.Apply(new StockRestocked(DefaultSku, DefaultWarehouseId, Guid.NewGuid(), Quantity: 25, Now));

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
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 15, Now))
            .Apply(new ReservationCommitted(reservationId, DefaultSku, DefaultWarehouseId, Now));

        var committed = inventory.CommittedAllocations[reservationId];

        var result = inventory.Apply(new StockRestocked(DefaultSku, DefaultWarehouseId, Guid.NewGuid(), Quantity: 50, Now));

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
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 10, Now));

        var reservedBefore   = inventory.ReservedQuantity;
        var committedBefore  = inventory.CommittedQuantity;

        var result = inventory.Apply(new StockRestocked(DefaultSku, DefaultWarehouseId, Guid.NewGuid(), Quantity: 50, Now));

        result.ReservedQuantity.ShouldBe(reservedBefore);
        result.CommittedQuantity.ShouldBe(committedBefore);
    }

    // ---------------------------------------------------------------------------
    // Apply(StockPicked) — S2: Committed → Picked
    // ---------------------------------------------------------------------------

    /// <summary>
    /// StockPicked removes entry from CommittedAllocations and adds to PickedAllocations.
    /// </summary>
    [Fact]
    public void Apply_StockPicked_Moves_From_Committed_To_Picked()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory(availableQuantity: 80)
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 20, Now))
            .Apply(new ReservationCommitted(reservationId, DefaultSku, DefaultWarehouseId, Now));

        var result = inventory.Apply(new StockPicked(DefaultSku, DefaultWarehouseId, reservationId, 20, Now));

        result.CommittedAllocations.ShouldNotContainKey(reservationId);
        result.PickedAllocations.ShouldContainKey(reservationId);
        result.PickedAllocations[reservationId].ShouldBe(20);
    }

    /// <summary>
    /// StockPicked does not change AvailableQuantity or TotalOnHand (item is still in building).
    /// </summary>
    [Fact]
    public void Apply_StockPicked_Preserves_TotalOnHand()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory(availableQuantity: 80)
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 20, Now))
            .Apply(new ReservationCommitted(reservationId, DefaultSku, DefaultWarehouseId, Now));

        var totalBefore = inventory.TotalOnHand;
        var availableBefore = inventory.AvailableQuantity;

        var result = inventory.Apply(new StockPicked(DefaultSku, DefaultWarehouseId, reservationId, 20, Now));

        result.TotalOnHand.ShouldBe(totalBefore);
        result.AvailableQuantity.ShouldBe(availableBefore);
        result.PickedQuantity.ShouldBe(20);
    }

    // ---------------------------------------------------------------------------
    // Apply(StockShipped) — S2: Picked removed, TotalOnHand decrements
    // ---------------------------------------------------------------------------

    /// <summary>
    /// StockShipped removes entry from PickedAllocations and ReservationOrderIds.
    /// TotalOnHand decrements (stock has left the building).
    /// </summary>
    [Fact]
    public void Apply_StockShipped_Removes_From_Picked_And_Decrements_TotalOnHand()
    {
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var inventory = BuildInventory(availableQuantity: 100)
            .Apply(new StockReserved(orderId, reservationId, DefaultSku, DefaultWarehouseId, Quantity: 20, Now))
            .Apply(new ReservationCommitted(reservationId, DefaultSku, DefaultWarehouseId, Now))
            .Apply(new StockPicked(DefaultSku, DefaultWarehouseId, reservationId, 20, Now));

        var totalBefore = inventory.TotalOnHand;

        var result = inventory.Apply(new StockShipped(DefaultSku, DefaultWarehouseId, reservationId, 20, Guid.NewGuid(), Now));

        result.PickedAllocations.ShouldNotContainKey(reservationId);
        result.ReservationOrderIds.ShouldNotContainKey(reservationId);
        result.TotalOnHand.ShouldBe(totalBefore - 20);
        result.AvailableQuantity.ShouldBe(80); // unchanged — was decremented on reserve, not ship
    }

    // ---------------------------------------------------------------------------
    // Apply(StockDiscrepancyFound) — S2: no state change
    // ---------------------------------------------------------------------------

    /// <summary>
    /// StockDiscrepancyFound does not change any aggregate state.
    /// </summary>
    [Fact]
    public void Apply_StockDiscrepancyFound_Returns_Unchanged()
    {
        var inventory = BuildInventory(availableQuantity: 100);

        var result = inventory.Apply(new StockDiscrepancyFound(
            DefaultSku, DefaultWarehouseId, 10, 7, DiscrepancyType.ShortPick, "test", Now));

        result.ShouldBeSameAs(inventory);
    }

    // ---------------------------------------------------------------------------
    // Apply(ReservationExpired) — S2: same as ReservationReleased
    // ---------------------------------------------------------------------------

    /// <summary>
    /// ReservationExpired restores AvailableQuantity and removes from Reservations.
    /// </summary>
    [Fact]
    public void Apply_ReservationExpired_Restores_AvailableQuantity()
    {
        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory(availableQuantity: 100)
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 30, Now));

        var result = inventory.Apply(new ReservationExpired(
            reservationId, DefaultSku, DefaultWarehouseId, 30, "Expired", Now));

        result.AvailableQuantity.ShouldBe(100);
        result.Reservations.ShouldNotContainKey(reservationId);
    }

    /// <summary>
    /// ReservationExpired with unknown ID returns unchanged (idempotent).
    /// </summary>
    [Fact]
    public void Apply_ReservationExpired_Unknown_Id_Returns_Unchanged()
    {
        var inventory = BuildInventory();

        var result = inventory.Apply(new ReservationExpired(
            Guid.NewGuid(), DefaultSku, DefaultWarehouseId, 10, "Expired", Now));

        result.ShouldBeSameAs(inventory);
    }

    // ---------------------------------------------------------------------------
    // Apply(BackorderRegistered/BackorderCleared) — S2
    // ---------------------------------------------------------------------------

    /// <summary>
    /// BackorderRegistered sets HasPendingBackorders to true.
    /// </summary>
    [Fact]
    public void Apply_BackorderRegistered_Sets_HasPendingBackorders_True()
    {
        var inventory = BuildInventory();

        var result = inventory.Apply(new BackorderRegistered(
            DefaultSku, DefaultWarehouseId, Guid.NewGuid(), Guid.NewGuid(), 5, Now));

        result.HasPendingBackorders.ShouldBeTrue();
    }

    /// <summary>
    /// BackorderCleared sets HasPendingBackorders to false.
    /// </summary>
    [Fact]
    public void Apply_BackorderCleared_Sets_HasPendingBackorders_False()
    {
        var inventory = BuildInventory();
        var withBackorder = inventory.Apply(new BackorderRegistered(
            DefaultSku, DefaultWarehouseId, Guid.NewGuid(), Guid.NewGuid(), 5, Now));

        var result = withBackorder.Apply(new BackorderCleared(DefaultSku, DefaultWarehouseId, Now));

        result.HasPendingBackorders.ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------
    // Apply(CycleCountInitiated/CycleCountCompleted) — S2: no state change
    // ---------------------------------------------------------------------------

    [Fact]
    public void Apply_CycleCountInitiated_Returns_Unchanged()
    {
        var inventory = BuildInventory();
        var result = inventory.Apply(new CycleCountInitiated(DefaultSku, DefaultWarehouseId, "clerk", Now));
        result.ShouldBeSameAs(inventory);
    }

    [Fact]
    public void Apply_CycleCountCompleted_Returns_Unchanged()
    {
        var inventory = BuildInventory();
        var result = inventory.Apply(new CycleCountCompleted(DefaultSku, DefaultWarehouseId, 100, 100, "clerk", Now));
        result.ShouldBeSameAs(inventory);
    }

    // ---------------------------------------------------------------------------
    // Apply(DamageRecorded/StockWrittenOff) — S2: no state change (via InventoryAdjusted)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Apply_DamageRecorded_Returns_Unchanged()
    {
        var inventory = BuildInventory();
        var result = inventory.Apply(new DamageRecorded(DefaultSku, DefaultWarehouseId, 2, "water damage", "clerk", Now));
        result.ShouldBeSameAs(inventory);
    }

    [Fact]
    public void Apply_StockWrittenOff_Returns_Unchanged()
    {
        var inventory = BuildInventory();
        var result = inventory.Apply(new StockWrittenOff(DefaultSku, DefaultWarehouseId, 10, "recall", "ops", Now));
        result.ShouldBeSameAs(inventory);
    }

    // ---------------------------------------------------------------------------
    // Full lifecycle scenario: Reserve → Commit → Pick → Ship
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Full lifecycle: TotalOnHand is conserved until StockShipped, then decrements.
    /// </summary>
    [Fact]
    public void Scenario_Reserve_Commit_Pick_Ship_Correctly_Tracks_TotalOnHand()
    {
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        var inventory = BuildInventory(availableQuantity: 100)
            .Apply(new StockReserved(orderId, reservationId, DefaultSku, DefaultWarehouseId, 20, Now));

        inventory.TotalOnHand.ShouldBe(100); // Available 80 + Reserved 20

        inventory = inventory.Apply(new ReservationCommitted(reservationId, DefaultSku, DefaultWarehouseId, Now));
        inventory.TotalOnHand.ShouldBe(100); // Available 80 + Committed 20

        inventory = inventory.Apply(new StockPicked(DefaultSku, DefaultWarehouseId, reservationId, 20, Now));
        inventory.TotalOnHand.ShouldBe(100); // Available 80 + Picked 20

        inventory = inventory.Apply(new StockShipped(DefaultSku, DefaultWarehouseId, reservationId, 20, shipmentId, Now));
        inventory.TotalOnHand.ShouldBe(80); // Available 80 only — shipped stock is gone
        inventory.PickedAllocations.ShouldBeEmpty();
        inventory.CommittedAllocations.ShouldBeEmpty();
        inventory.AvailableQuantity.ShouldBe(80);
    }
}
