namespace Inventory.UnitTests.Management;

/// <summary>
/// Unit tests for <see cref="ProductInventory.Create"/> factory method,
/// <see cref="ProductInventory.CombinedGuid"/> deterministic ID generation,
/// and computed properties (<see cref="ProductInventory.ReservedQuantity"/>,
/// <see cref="ProductInventory.CommittedQuantity"/>, <see cref="ProductInventory.TotalOnHand"/>).
/// </summary>
public class ProductInventoryCreateTests
{
    // ---------------------------------------------------------------------------
    // Shared test fixtures
    // ---------------------------------------------------------------------------

    private const string DefaultSku = "CAT-FOOD-001";
    private const string DefaultWarehouseId = "WH-EAST-01";
    private static readonly DateTimeOffset DefaultInitializedAt =
        new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Helper: returns a minimal <see cref="InventoryInitialized"/> event using default values.
    /// </summary>
    private static InventoryInitialized BuildInitializedEvent(
        string? sku = null,
        string? warehouseId = null,
        int initialQuantity = 100,
        DateTimeOffset? initializedAt = null) =>
        new(
            sku ?? DefaultSku,
            warehouseId ?? DefaultWarehouseId,
            initialQuantity,
            initializedAt ?? DefaultInitializedAt);

    /// <summary>
    /// Helper: creates a <see cref="ProductInventory"/> from a default <see cref="InventoryInitialized"/> event.
    /// </summary>
    private static ProductInventory BuildInventory(
        string? sku = null,
        string? warehouseId = null,
        int initialQuantity = 100,
        DateTimeOffset? initializedAt = null) =>
        ProductInventory.Create(BuildInitializedEvent(sku, warehouseId, initialQuantity, initializedAt));

    // ---------------------------------------------------------------------------
    // ProductInventory.Create() — field mapping
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Create sets Sku from the InventoryInitialized event.
    /// </summary>
    [Fact]
    public void Create_Sets_Sku_From_Event()
    {
        var inventory = BuildInventory(sku: "DOG-TREAT-007");

        inventory.Sku.ShouldBe("DOG-TREAT-007");
    }

    /// <summary>
    /// Create sets WarehouseId from the InventoryInitialized event.
    /// </summary>
    [Fact]
    public void Create_Sets_WarehouseId_From_Event()
    {
        var inventory = BuildInventory(warehouseId: "WH-WEST-99");

        inventory.WarehouseId.ShouldBe("WH-WEST-99");
    }

    /// <summary>
    /// Create sets AvailableQuantity from the event's InitialQuantity.
    /// </summary>
    [Fact]
    public void Create_Sets_AvailableQuantity_From_InitialQuantity()
    {
        var inventory = BuildInventory(initialQuantity: 250);

        inventory.AvailableQuantity.ShouldBe(250);
    }

    /// <summary>
    /// Create preserves a zero InitialQuantity (e.g., placeholder SKU added before stock arrives).
    /// </summary>
    [Fact]
    public void Create_Allows_Zero_InitialQuantity()
    {
        var inventory = BuildInventory(initialQuantity: 0);

        inventory.AvailableQuantity.ShouldBe(0);
    }

    /// <summary>
    /// Create sets InitializedAt from the event timestamp.
    /// </summary>
    [Fact]
    public void Create_Sets_InitializedAt_From_Event()
    {
        var timestamp = new DateTimeOffset(2025, 1, 15, 9, 30, 0, TimeSpan.Zero);
        var inventory = BuildInventory(initializedAt: timestamp);

        inventory.InitializedAt.ShouldBe(timestamp);
    }

    /// <summary>
    /// Create initializes Reservations as an empty dictionary.
    /// </summary>
    [Fact]
    public void Create_Initializes_Reservations_As_Empty()
    {
        var inventory = BuildInventory();

        inventory.Reservations.ShouldBeEmpty();
    }

    /// <summary>
    /// Create initializes CommittedAllocations as an empty dictionary.
    /// </summary>
    [Fact]
    public void Create_Initializes_CommittedAllocations_As_Empty()
    {
        var inventory = BuildInventory();

        inventory.CommittedAllocations.ShouldBeEmpty();
    }

    /// <summary>
    /// Create initializes ReservationOrderIds as an empty dictionary.
    /// </summary>
    [Fact]
    public void Create_Initializes_ReservationOrderIds_As_Empty()
    {
        var inventory = BuildInventory();

        inventory.ReservationOrderIds.ShouldBeEmpty();
    }

    /// <summary>
    /// Create initializes PickedAllocations as an empty dictionary.
    /// </summary>
    [Fact]
    public void Create_Initializes_PickedAllocations_As_Empty()
    {
        var inventory = BuildInventory();

        inventory.PickedAllocations.ShouldBeEmpty();
    }

    /// <summary>
    /// Create initializes HasPendingBackorders as false.
    /// </summary>
    [Fact]
    public void Create_Initializes_HasPendingBackorders_As_False()
    {
        var inventory = BuildInventory();

        inventory.HasPendingBackorders.ShouldBeFalse();
    }

    /// <summary>
    /// Create assigns Id equal to InventoryStreamId.Compute(sku, warehouseId).
    /// </summary>
    [Fact]
    public void Create_Sets_Id_To_InventoryStreamId_Of_Sku_And_WarehouseId()
    {
        var inventory = BuildInventory(sku: DefaultSku, warehouseId: DefaultWarehouseId);

        var expectedId = InventoryStreamId.Compute(DefaultSku, DefaultWarehouseId);
        inventory.Id.ShouldBe(expectedId);
    }

    // ---------------------------------------------------------------------------
    // ProductInventory.CombinedGuid() — determinism and uniqueness
    // ---------------------------------------------------------------------------

    /// <summary>
    /// CombinedGuid returns a non-empty GUID.
    /// </summary>
    [Fact]
    public void CombinedGuid_Returns_Non_Empty_Guid()
    {
        var id = ProductInventory.CombinedGuid(DefaultSku, DefaultWarehouseId);

        id.ShouldNotBe(Guid.Empty);
    }

    /// <summary>
    /// CombinedGuid is deterministic: same SKU + warehouse always yields the same GUID.
    /// </summary>
    [Fact]
    public void CombinedGuid_Is_Deterministic_For_Same_Inputs()
    {
        var first  = ProductInventory.CombinedGuid("BIRD-SEED-001", "WH-SOUTH-02");
        var second = ProductInventory.CombinedGuid("BIRD-SEED-001", "WH-SOUTH-02");

        first.ShouldBe(second);
    }

    /// <summary>
    /// CombinedGuid produces different GUIDs for different SKUs (same warehouse).
    /// </summary>
    [Fact]
    public void CombinedGuid_Differs_When_Sku_Differs()
    {
        var idA = ProductInventory.CombinedGuid("SKU-ALPHA", DefaultWarehouseId);
        var idB = ProductInventory.CombinedGuid("SKU-BETA",  DefaultWarehouseId);

        idA.ShouldNotBe(idB);
    }

    /// <summary>
    /// CombinedGuid produces different GUIDs for different warehouses (same SKU).
    /// </summary>
    [Fact]
    public void CombinedGuid_Differs_When_WarehouseId_Differs()
    {
        var idA = ProductInventory.CombinedGuid(DefaultSku, "WH-EAST-01");
        var idB = ProductInventory.CombinedGuid(DefaultSku, "WH-WEST-02");

        idA.ShouldNotBe(idB);
    }

    /// <summary>
    /// CombinedGuid treats "SKU:WH" and a different split differently — the colon separator
    /// means "A:BC" and "AB:C" must not collide.
    /// </summary>
    [Fact]
    public void CombinedGuid_Does_Not_Collide_On_Different_Splits()
    {
        var idA = ProductInventory.CombinedGuid("A",  "BC");
        var idB = ProductInventory.CombinedGuid("AB", "C");

        idA.ShouldNotBe(idB);
    }

    /// <summary>
    /// Property: CombinedGuid is always deterministic for any non-null strings.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool CombinedGuid_Is_Always_Deterministic(string sku, string warehouseId)
    {
        // FsCheck may generate null strings; guard against them.
        if (sku is null || warehouseId is null)
            return true; // vacuously true — not the scenario under test

        return ProductInventory.CombinedGuid(sku, warehouseId)
            == ProductInventory.CombinedGuid(sku, warehouseId);
    }

    // ---------------------------------------------------------------------------
    // Computed properties — initial state (all empty dicts)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// ReservedQuantity is 0 when Reservations is empty.
    /// </summary>
    [Fact]
    public void ReservedQuantity_Is_Zero_When_No_Reservations()
    {
        var inventory = BuildInventory();

        inventory.ReservedQuantity.ShouldBe(0);
    }

    /// <summary>
    /// CommittedQuantity is 0 when CommittedAllocations is empty.
    /// </summary>
    [Fact]
    public void CommittedQuantity_Is_Zero_When_No_Committed_Allocations()
    {
        var inventory = BuildInventory();

        inventory.CommittedQuantity.ShouldBe(0);
    }

    /// <summary>
    /// TotalOnHand equals AvailableQuantity when Reservations and CommittedAllocations are empty.
    /// </summary>
    [Fact]
    public void TotalOnHand_Equals_AvailableQuantity_When_Collections_Are_Empty()
    {
        var inventory = BuildInventory(initialQuantity: 75);

        inventory.TotalOnHand.ShouldBe(75);
    }

    /// <summary>
    /// ReservedQuantity sums all values across multiple reservation entries.
    /// </summary>
    [Fact]
    public void ReservedQuantity_Sums_All_Reservation_Values()
    {
        var reservations = new Dictionary<Guid, int>
        {
            [Guid.NewGuid()] = 10,
            [Guid.NewGuid()] = 25,
            [Guid.NewGuid()] = 5
        };

        var inventory = new ProductInventory(
            Guid.NewGuid(), DefaultSku, DefaultWarehouseId,
            AvailableQuantity: 60,
            Reservations: reservations,
            CommittedAllocations: new Dictionary<Guid, int>(),
            ReservationOrderIds: new Dictionary<Guid, Guid>(),
            PickedAllocations: new Dictionary<Guid, int>(),
            HasPendingBackorders: false,
            QuarantinedQuantity: 0,
            InitializedAt: DefaultInitializedAt);

        inventory.ReservedQuantity.ShouldBe(40);  // 10 + 25 + 5
    }

    /// <summary>
    /// CommittedQuantity sums all values across multiple committed allocation entries.
    /// </summary>
    [Fact]
    public void CommittedQuantity_Sums_All_CommittedAllocation_Values()
    {
        var committed = new Dictionary<Guid, int>
        {
            [Guid.NewGuid()] = 8,
            [Guid.NewGuid()] = 12
        };

        var inventory = new ProductInventory(
            Guid.NewGuid(), DefaultSku, DefaultWarehouseId,
            AvailableQuantity: 80,
            Reservations: new Dictionary<Guid, int>(),
            CommittedAllocations: committed,
            ReservationOrderIds: new Dictionary<Guid, Guid>(),
            PickedAllocations: new Dictionary<Guid, int>(),
            HasPendingBackorders: false,
            QuarantinedQuantity: 0,
            InitializedAt: DefaultInitializedAt);

        inventory.CommittedQuantity.ShouldBe(20);  // 8 + 12
    }

    /// <summary>
    /// TotalOnHand equals AvailableQuantity + ReservedQuantity + CommittedQuantity.
    /// </summary>
    [Fact]
    public void TotalOnHand_Equals_Sum_Of_All_Quantity_Buckets()
    {
        var reservations = new Dictionary<Guid, int> { [Guid.NewGuid()] = 15 };
        var committed    = new Dictionary<Guid, int> { [Guid.NewGuid()] = 10 };

        var inventory = new ProductInventory(
            Guid.NewGuid(), DefaultSku, DefaultWarehouseId,
            AvailableQuantity: 50,
            Reservations: reservations,
            CommittedAllocations: committed,
            ReservationOrderIds: new Dictionary<Guid, Guid>(),
            PickedAllocations: new Dictionary<Guid, int>(),
            HasPendingBackorders: false,
            QuarantinedQuantity: 0,
            InitializedAt: DefaultInitializedAt);

        // 50 available + 15 reserved + 10 committed = 75
        inventory.TotalOnHand.ShouldBe(75);
    }
}
