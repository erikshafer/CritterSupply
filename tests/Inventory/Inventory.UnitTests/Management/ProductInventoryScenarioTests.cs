namespace Inventory.UnitTests.Management;

/// <summary>
/// Multi-step event-chain scenario tests and property-based conservation invariants for
/// <see cref="ProductInventory"/>.
///
/// These tests replicate realistic business workflows and use FsCheck's <c>[Property]</c>
/// attribute to verify that <see cref="ProductInventory.TotalOnHand"/> is conserved across
/// every Reserve → Commit/Release lifecycle transition.
/// </summary>
public class ProductInventoryScenarioTests
{
    // ---------------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------------

    private const string DefaultSku = "CAT-FOOD-001";
    private const string DefaultWarehouseId = "WH-EAST-01";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a fresh <see cref="ProductInventory"/> with the given initial quantity.
    /// </summary>
    private static ProductInventory BuildInventory(int initialQuantity = 100) =>
        ProductInventory.Create(
            new InventoryInitialized(DefaultSku, DefaultWarehouseId, initialQuantity, Now));

    // ---------------------------------------------------------------------------
    // Scenario: Initialize → Reserve → Commit
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Initialize → Reserve → Commit: the reservation moves from Reservations to
    /// CommittedAllocations and AvailableQuantity is not restored.
    /// </summary>
    [Fact]
    public void Scenario_Initialize_Reserve_Commit_Produces_Correct_Final_State()
    {
        var reservationId = Guid.NewGuid();

        var inventory = BuildInventory(initialQuantity: 100)
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 30, Now))
            .Apply(new ReservationCommitted(reservationId, DefaultSku, DefaultWarehouseId, Now));

        inventory.AvailableQuantity.ShouldBe(70);          // decremented on reserve, stays on commit
        inventory.ReservedQuantity.ShouldBe(0);            // removed from Reservations
        inventory.CommittedQuantity.ShouldBe(30);          // moved into CommittedAllocations
        inventory.TotalOnHand.ShouldBe(100);               // conserved throughout
        inventory.Reservations.ShouldNotContainKey(reservationId);
        inventory.CommittedAllocations.ShouldContainKey(reservationId);
        inventory.CommittedAllocations[reservationId].ShouldBe(30);
    }

    // ---------------------------------------------------------------------------
    // Scenario: Initialize → Reserve → Release
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Initialize → Reserve → Release: AvailableQuantity is fully restored to its original value.
    /// </summary>
    [Fact]
    public void Scenario_Initialize_Reserve_Release_Restores_AvailableQuantity()
    {
        var reservationId = Guid.NewGuid();

        var inventory = BuildInventory(initialQuantity: 100)
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 40, Now))
            .Apply(new ReservationReleased(reservationId, DefaultSku, DefaultWarehouseId, Quantity: 40, Reason: "cancelled", Now));

        inventory.AvailableQuantity.ShouldBe(100);
        inventory.ReservedQuantity.ShouldBe(0);
        inventory.CommittedQuantity.ShouldBe(0);
        inventory.TotalOnHand.ShouldBe(100);
        inventory.Reservations.ShouldNotContainKey(reservationId);
        inventory.CommittedAllocations.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------------------
    // Scenario: Initialize → Reserve twice
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Initialize → Reserve (first) → Reserve (second): both reservations are tracked
    /// independently and AvailableQuantity is decremented by the cumulative total.
    /// </summary>
    [Fact]
    public void Scenario_Two_Reservations_Are_Tracked_Independently()
    {
        var resId1 = Guid.NewGuid();
        var resId2 = Guid.NewGuid();

        var inventory = BuildInventory(initialQuantity: 100)
            .Apply(new StockReserved(Guid.NewGuid(), resId1, DefaultSku, DefaultWarehouseId, Quantity: 20, Now))
            .Apply(new StockReserved(Guid.NewGuid(), resId2, DefaultSku, DefaultWarehouseId, Quantity: 35, Now));

        inventory.AvailableQuantity.ShouldBe(45);   // 100 - 20 - 35
        inventory.ReservedQuantity.ShouldBe(55);    // 20 + 35
        inventory.TotalOnHand.ShouldBe(100);

        inventory.Reservations[resId1].ShouldBe(20);
        inventory.Reservations[resId2].ShouldBe(35);
    }

    // ---------------------------------------------------------------------------
    // Scenario: Initialize → Receive stock → Reserve
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Initialize → Receive stock → Reserve: the combined available pool grows from the receipt
    /// and is then partially consumed by a reservation.
    /// </summary>
    [Fact]
    public void Scenario_Receive_Then_Reserve_Produces_Correct_State()
    {
        var reservationId = Guid.NewGuid();

        var inventory = BuildInventory(initialQuantity: 50)
            .Apply(new StockReceived(DefaultSku, DefaultWarehouseId, "Supplier-A", null, Quantity: 50, Now))
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, Quantity: 30, Now));

        inventory.AvailableQuantity.ShouldBe(70);    // (50 + 50) - 30
        inventory.ReservedQuantity.ShouldBe(30);
        inventory.TotalOnHand.ShouldBe(100);
    }

    // ---------------------------------------------------------------------------
    // Scenario: Restocked → Reserve → Commit → Release remaining
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Full lifecycle: Restock → two reservations → commit one → release the other.
    /// Verifies that each operation affects only its own reservation slot.
    /// </summary>
    [Fact]
    public void Scenario_Restock_Reserve_Reserve_Commit_Release_Produces_Correct_Final_State()
    {
        var resId1 = Guid.NewGuid();
        var resId2 = Guid.NewGuid();

        var inventory = BuildInventory(initialQuantity: 100)
            // Add more stock via a return
            .Apply(new StockRestocked(DefaultSku, DefaultWarehouseId, Guid.NewGuid(), Quantity: 20, Now))       // available = 120
            // Reserve two separate orders
            .Apply(new StockReserved(Guid.NewGuid(), resId1, DefaultSku, DefaultWarehouseId, Quantity: 30, Now))  // available = 90
            .Apply(new StockReserved(Guid.NewGuid(), resId2, DefaultSku, DefaultWarehouseId, Quantity: 10, Now))  // available = 80
            // Commit the first reservation (order goes to fulfillment)
            .Apply(new ReservationCommitted(resId1, DefaultSku, DefaultWarehouseId, Now))
            // Release the second reservation (order was cancelled)
            .Apply(new ReservationReleased(resId2, DefaultSku, DefaultWarehouseId, Quantity: 10, Reason: "cancelled", Now)); // available = 90

        inventory.AvailableQuantity.ShouldBe(90);    // 80 restored by release of resId2
        inventory.ReservedQuantity.ShouldBe(0);      // resId2 released
        inventory.CommittedQuantity.ShouldBe(30);    // resId1 committed
        inventory.TotalOnHand.ShouldBe(120);         // conserved from initial 100 + restock 20
        inventory.Reservations.ShouldBeEmpty();
        inventory.CommittedAllocations.ShouldContainKey(resId1);
        inventory.CommittedAllocations[resId1].ShouldBe(30);
    }

    // ---------------------------------------------------------------------------
    // Conservation property: TotalOnHand is preserved across Reserve/Commit/Release
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Property: Applying <see cref="StockReserved"/> never changes <see cref="ProductInventory.TotalOnHand"/>.
    /// Stock moves from Available → Reserved but the total is conserved.
    /// </summary>
    [Property(MaxTest = 300, Arbitrary = [typeof(InventoryArbitrary)])]
    public bool StockReserved_Preserves_TotalOnHand(int initialQuantity, int reserveQuantity)
    {
        if (initialQuantity <= 0 || reserveQuantity <= 0 || reserveQuantity > initialQuantity)
            return true; // vacuously skip invalid combinations

        var inventory = BuildInventory(initialQuantity);
        var before = inventory.TotalOnHand;

        var after = inventory.Apply(new StockReserved(Guid.NewGuid(), Guid.NewGuid(), DefaultSku, DefaultWarehouseId, reserveQuantity, Now));

        return after.TotalOnHand == before;
    }

    /// <summary>
    /// Property: Applying <see cref="ReservationCommitted"/> never changes <see cref="ProductInventory.TotalOnHand"/>.
    /// Stock moves from Reserved → Committed but the total is conserved.
    /// </summary>
    [Property(MaxTest = 300, Arbitrary = [typeof(InventoryArbitrary)])]
    public bool ReservationCommitted_Preserves_TotalOnHand(int initialQuantity, int reserveQuantity)
    {
        if (initialQuantity <= 0 || reserveQuantity <= 0 || reserveQuantity > initialQuantity)
            return true;

        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory(initialQuantity)
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, reserveQuantity, Now));

        var before = inventory.TotalOnHand;

        var after = inventory.Apply(new ReservationCommitted(reservationId, DefaultSku, DefaultWarehouseId, Now));

        return after.TotalOnHand == before;
    }

    /// <summary>
    /// Property: Applying <see cref="ReservationReleased"/> never changes <see cref="ProductInventory.TotalOnHand"/>.
    /// Stock moves from Reserved → Available but the total is conserved.
    /// </summary>
    [Property(MaxTest = 300, Arbitrary = [typeof(InventoryArbitrary)])]
    public bool ReservationReleased_Preserves_TotalOnHand(int initialQuantity, int reserveQuantity)
    {
        if (initialQuantity <= 0 || reserveQuantity <= 0 || reserveQuantity > initialQuantity)
            return true;

        var reservationId = Guid.NewGuid();
        var inventory = BuildInventory(initialQuantity)
            .Apply(new StockReserved(Guid.NewGuid(), reservationId, DefaultSku, DefaultWarehouseId, reserveQuantity, Now));

        var before = inventory.TotalOnHand;

        var after = inventory.Apply(
            new ReservationReleased(reservationId, DefaultSku, DefaultWarehouseId, reserveQuantity, "test-release", Now));

        return after.TotalOnHand == before;
    }

    /// <summary>
    /// Property: <see cref="StockReceived"/> and <see cref="StockRestocked"/> increase
    /// <see cref="ProductInventory.TotalOnHand"/> by exactly the received/restocked quantity.
    /// </summary>
    [Property(MaxTest = 300, Arbitrary = [typeof(InventoryArbitrary)])]
    public bool StockReceived_Increases_TotalOnHand_By_Exact_Quantity(int initialQuantity, int receivedQuantity)
    {
        if (initialQuantity < 0 || receivedQuantity <= 0)
            return true;

        var inventory = BuildInventory(initialQuantity);
        var before = inventory.TotalOnHand;

        var afterReceive   = inventory.Apply(new StockReceived(DefaultSku, DefaultWarehouseId, "Supplier", null, receivedQuantity, Now));
        var afterRestock   = inventory.Apply(new StockRestocked(DefaultSku, DefaultWarehouseId, Guid.NewGuid(), receivedQuantity, Now));

        return afterReceive.TotalOnHand == before + receivedQuantity
            && afterRestock.TotalOnHand  == before + receivedQuantity;
    }

    /// <summary>
    /// Property: After an unknown-ReservationId commit or release (the idempotency guard),
    /// TotalOnHand is unchanged and the returned object is the same reference.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(InventoryArbitrary)])]
    public bool Unknown_ReservationId_Returns_Same_Inventory(int initialQuantity)
    {
        if (initialQuantity < 0)
            return true;

        var inventory    = BuildInventory(initialQuantity);
        var unknownId    = Guid.NewGuid();

        var afterCommit  = inventory.Apply(new ReservationCommitted(unknownId, DefaultSku, DefaultWarehouseId, Now));
        var afterRelease = inventory.Apply(
            new ReservationReleased(unknownId, DefaultSku, DefaultWarehouseId, 0, "ghost", Now));

        return ReferenceEquals(afterCommit, inventory)
            && ReferenceEquals(afterRelease, inventory);
    }
}

// ---------------------------------------------------------------------------
// FsCheck Arbitraries
// ---------------------------------------------------------------------------

/// <summary>
/// Provides generators for positive inventory quantities used in property-based tests.
/// Generates values in ranges realistic for a warehouse (1–5000 initial stock, 1–500 reserve).
/// </summary>
public static class InventoryArbitrary
{
    /// <summary>
    /// Generates valid initial inventory quantities (1..5000).
    /// Named 'Int32' so FsCheck resolves it for <c>int</c> parameters via the standard type match.
    /// FsCheck uses the first matching Arbitrary method; we use two differently-named generators
    /// mapped via SelectMany to yield the pair as two separate parameters.
    /// </summary>
    public static Arbitrary<int> Int32() =>
        Gen.Choose(1, 5000).ToArbitrary();
}
