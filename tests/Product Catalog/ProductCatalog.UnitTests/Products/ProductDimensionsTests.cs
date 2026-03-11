namespace ProductCatalog.UnitTests.Products;

/// <summary>
/// Unit tests for the <see cref="ProductDimensions"/> value object.
/// Covers valid construction, boundary enforcement, and field mapping.
/// </summary>
public class ProductDimensionsTests
{
    // ---------------------------------------------------------------------------
    // ProductDimensions.Create() — valid inputs
    // ---------------------------------------------------------------------------

    /// <summary>Valid positive dimensions can be created.</summary>
    [Fact]
    public void Create_ValidPositiveDimensions_Succeeds()
    {
        var dimensions = ProductDimensions.Create(10.5m, 8.0m, 3.0m, 4.25m);

        dimensions.Length.ShouldBe(10.5m);
        dimensions.Width.ShouldBe(8.0m);
        dimensions.Height.ShouldBe(3.0m);
        dimensions.Weight.ShouldBe(4.25m);
    }

    /// <summary>Very small positive values (e.g. 0.01) are accepted.</summary>
    [Fact]
    public void Create_VerySmallPositiveValues_Succeeds()
    {
        var dimensions = ProductDimensions.Create(0.01m, 0.01m, 0.01m, 0.01m);

        dimensions.Length.ShouldBe(0.01m);
        dimensions.Width.ShouldBe(0.01m);
        dimensions.Height.ShouldBe(0.01m);
        dimensions.Weight.ShouldBe(0.01m);
    }

    /// <summary>Large dimensions (e.g. a dog crate) are accepted.</summary>
    [Fact]
    public void Create_LargeDimensions_Succeeds()
    {
        var dimensions = ProductDimensions.Create(48m, 30m, 36m, 25.5m);

        dimensions.Length.ShouldBe(48m);
        dimensions.Weight.ShouldBe(25.5m);
    }

    // ---------------------------------------------------------------------------
    // ProductDimensions.Create() — invalid inputs
    // ---------------------------------------------------------------------------

    /// <summary>A zero length is rejected.</summary>
    [Fact]
    public void Create_ZeroLength_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => ProductDimensions.Create(0m, 5m, 5m, 1m));
    }

    /// <summary>A negative length is rejected.</summary>
    [Fact]
    public void Create_NegativeLength_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => ProductDimensions.Create(-1m, 5m, 5m, 1m));
    }

    /// <summary>A zero width is rejected.</summary>
    [Fact]
    public void Create_ZeroWidth_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => ProductDimensions.Create(5m, 0m, 5m, 1m));
    }

    /// <summary>A negative width is rejected.</summary>
    [Fact]
    public void Create_NegativeWidth_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => ProductDimensions.Create(5m, -2m, 5m, 1m));
    }

    /// <summary>A zero height is rejected.</summary>
    [Fact]
    public void Create_ZeroHeight_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => ProductDimensions.Create(5m, 5m, 0m, 1m));
    }

    /// <summary>A negative height is rejected.</summary>
    [Fact]
    public void Create_NegativeHeight_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => ProductDimensions.Create(5m, 5m, -3m, 1m));
    }

    /// <summary>A zero weight is rejected.</summary>
    [Fact]
    public void Create_ZeroWeight_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => ProductDimensions.Create(5m, 5m, 5m, 0m));
    }

    /// <summary>A negative weight is rejected.</summary>
    [Fact]
    public void Create_NegativeWeight_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => ProductDimensions.Create(5m, 5m, 5m, -0.5m));
    }
}
