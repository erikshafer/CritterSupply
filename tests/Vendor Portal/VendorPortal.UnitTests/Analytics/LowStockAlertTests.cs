namespace VendorPortal.UnitTests.Analytics;

/// <summary>
/// Unit tests for <see cref="LowStockAlert.BuildId"/>.
/// Verifies deterministic composite ID construction used for deduplication.
/// </summary>
public class LowStockAlertTests
{
    // ---------------------------------------------------------------------------
    // LowStockAlert.BuildId() — format and determinism
    // ---------------------------------------------------------------------------

    /// <summary>BuildId produces a non-empty string.</summary>
    [Fact]
    public void BuildId_Returns_Non_Empty_String()
    {
        var id = LowStockAlert.BuildId(Guid.NewGuid(), "DOG-FOOD-5LB");

        id.ShouldNotBeNullOrEmpty();
    }

    /// <summary>BuildId is deterministic: same inputs always produce the same ID.</summary>
    [Fact]
    public void BuildId_Is_Deterministic_For_Same_Inputs()
    {
        var vendorId = Guid.NewGuid();
        const string sku = "CAT-LITTER-20LB";

        var first = LowStockAlert.BuildId(vendorId, sku);
        var second = LowStockAlert.BuildId(vendorId, sku);

        first.ShouldBe(second);
    }

    /// <summary>BuildId format is "{VendorTenantId}:{Sku}" — the colon separator is present.</summary>
    [Fact]
    public void BuildId_Format_Contains_Colon_Separator()
    {
        var vendorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        const string sku = "BIRD-SEED-10LB";

        var id = LowStockAlert.BuildId(vendorId, sku);

        id.ShouldBe($"{vendorId}:BIRD-SEED-10LB");
    }

    // ---------------------------------------------------------------------------
    // LowStockAlert.BuildId() — uniqueness
    // ---------------------------------------------------------------------------

    /// <summary>Different vendor tenants for the same SKU produce different IDs.</summary>
    [Fact]
    public void BuildId_Differs_When_VendorTenantId_Differs()
    {
        const string sku = "FISH-FOOD-2OZ";
        var vendor1 = Guid.NewGuid();
        var vendor2 = Guid.NewGuid();

        var id1 = LowStockAlert.BuildId(vendor1, sku);
        var id2 = LowStockAlert.BuildId(vendor2, sku);

        id1.ShouldNotBe(id2);
    }

    /// <summary>Same vendor tenant with different SKUs produces different IDs.</summary>
    [Fact]
    public void BuildId_Differs_When_Sku_Differs()
    {
        var vendorId = Guid.NewGuid();

        var id1 = LowStockAlert.BuildId(vendorId, "SKU-ALPHA");
        var id2 = LowStockAlert.BuildId(vendorId, "SKU-BETA");

        id1.ShouldNotBe(id2);
    }

    /// <summary>The generated ID begins with the vendor tenant GUID string representation.</summary>
    [Fact]
    public void BuildId_Starts_With_VendorTenantId()
    {
        var vendorId = Guid.NewGuid();
        var id = LowStockAlert.BuildId(vendorId, "REPTILE-LAMP-UV");

        id.ShouldStartWith(vendorId.ToString());
    }

    /// <summary>The generated ID ends with the SKU string.</summary>
    [Fact]
    public void BuildId_Ends_With_Sku()
    {
        var id = LowStockAlert.BuildId(Guid.NewGuid(), "HAMSTER-WHEEL-SM");

        id.ShouldEndWith("HAMSTER-WHEEL-SM");
    }
}
