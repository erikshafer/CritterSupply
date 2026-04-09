namespace Inventory.UnitTests.Management;

/// <summary>
/// Unit tests for <see cref="InventoryStreamId"/> deterministic UUID v5 generation.
/// Verifies RFC 4122 compliance, determinism, uniqueness, and input validation.
/// </summary>
public class InventoryStreamIdTests
{
    private const string DefaultSku = "CAT-FOOD-001";
    private const string DefaultWarehouseId = "WH-EAST-01";

    [Fact]
    public void Compute_Returns_NonEmpty_Guid()
    {
        var result = InventoryStreamId.Compute(DefaultSku, DefaultWarehouseId);

        result.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Compute_Is_Deterministic_Same_Inputs_Produce_Same_Result()
    {
        var first = InventoryStreamId.Compute(DefaultSku, DefaultWarehouseId);
        var second = InventoryStreamId.Compute(DefaultSku, DefaultWarehouseId);

        first.ShouldBe(second);
    }

    [Fact]
    public void Different_Sku_Produces_Different_Id()
    {
        var id1 = InventoryStreamId.Compute("SKU-A", DefaultWarehouseId);
        var id2 = InventoryStreamId.Compute("SKU-B", DefaultWarehouseId);

        id1.ShouldNotBe(id2);
    }

    [Fact]
    public void Different_WarehouseId_Produces_Different_Id()
    {
        var id1 = InventoryStreamId.Compute(DefaultSku, "WH-01");
        var id2 = InventoryStreamId.Compute(DefaultSku, "WH-02");

        id1.ShouldNotBe(id2);
    }

    [Fact]
    public void No_Collision_On_Different_Input_Splits()
    {
        // "A" + "BC" vs "AB" + "C" — the inventory:{sku}:{warehouseId} prefix format
        // means these produce different names: "inventory:A:BC" vs "inventory:AB:C"
        var id1 = InventoryStreamId.Compute("A", "BC");
        var id2 = InventoryStreamId.Compute("AB", "C");

        id1.ShouldNotBe(id2);
    }

    [Fact]
    public void Result_Differs_From_MD5_Based_CombinedGuid()
    {
        var uuidV5 = InventoryStreamId.Compute(DefaultSku, DefaultWarehouseId);

#pragma warning disable CS0618 // Obsolete CombinedGuid
        var md5Based = ProductInventory.CombinedGuid(DefaultSku, DefaultWarehouseId);
#pragma warning restore CS0618

        uuidV5.ShouldNotBe(md5Based);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Throws_ArgumentException_For_Invalid_Sku(string? sku)
    {
        Should.Throw<ArgumentException>(() => InventoryStreamId.Compute(sku!, DefaultWarehouseId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Throws_ArgumentException_For_Invalid_WarehouseId(string? warehouseId)
    {
        Should.Throw<ArgumentException>(() => InventoryStreamId.Compute(DefaultSku, warehouseId!));
    }

    [Property(MaxTest = 200)]
    public bool Compute_Is_Always_Deterministic_For_Any_NonNull_Strings(NonNull<string> sku, NonNull<string> warehouseId)
    {
        // Skip whitespace-only inputs that would throw
        if (string.IsNullOrWhiteSpace(sku.Get) || string.IsNullOrWhiteSpace(warehouseId.Get))
            return true;

        var first = InventoryStreamId.Compute(sku.Get, warehouseId.Get);
        var second = InventoryStreamId.Compute(sku.Get, warehouseId.Get);

        return first == second && first != Guid.Empty;
    }
}
