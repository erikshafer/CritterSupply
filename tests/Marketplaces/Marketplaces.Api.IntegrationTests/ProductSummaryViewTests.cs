using Marketplaces.Products;
using Messages.Contracts.ProductCatalog;

namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Integration tests for the ProductSummaryView ACL handlers.
/// Verifies that Product Catalog integration events correctly populate
/// and update the local ProductSummaryView document in Marketplaces BC.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ProductSummaryViewTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ProductSummaryViewTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _fixture.CleanAllDocumentsAsync();
    }

    // -------------------------------------------------------------------------
    // ProductAdded — creates the view
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProductSummaryView_CreatedWhenProductAdded()
    {
        var message = new ProductAdded(
            Sku: "TEST-SKU-001",
            Name: "Test Dog Bowl",
            Category: "Dogs",
            AddedAt: DateTimeOffset.UtcNow,
            Status: "Active");

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var view = await session.LoadAsync<ProductSummaryView>("TEST-SKU-001");

        view.ShouldNotBeNull();
        view.ProductName.ShouldBe("Test Dog Bowl");
        view.Category.ShouldBe("Dogs");
        view.Status.ShouldBe(ProductSummaryStatus.Active);
    }

    // -------------------------------------------------------------------------
    // ProductCategoryChanged — updates category
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProductSummaryView_UpdatedWhenCategoryChanged()
    {
        // Arrange: create the view first
        await SeedProductSummaryAsync("CAT-TOY-002", "Cat Toy", "Cats");

        var message = new ProductCategoryChanged(
            Sku: "CAT-TOY-002",
            PreviousCategory: "Cats",
            NewCategory: "Small Animals",
            OccurredAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var view = await session.LoadAsync<ProductSummaryView>("CAT-TOY-002");

        view.ShouldNotBeNull();
        view.Category.ShouldBe("Small Animals");
        view.ProductName.ShouldBe("Cat Toy"); // unchanged
    }

    // -------------------------------------------------------------------------
    // ProductContentUpdated — updates product name
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProductSummaryView_UpdatedWhenContentUpdated()
    {
        // Arrange: create the view first
        await SeedProductSummaryAsync("BIRD-CAGE-001", "Bird Cage", "Birds");

        var message = new ProductContentUpdated(
            Sku: "BIRD-CAGE-001",
            Name: "Premium Bird Cage XL",
            Description: "Extra large cage for parrots",
            OccurredAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var view = await session.LoadAsync<ProductSummaryView>("BIRD-CAGE-001");

        view.ShouldNotBeNull();
        view.ProductName.ShouldBe("Premium Bird Cage XL");
        view.Category.ShouldBe("Birds"); // unchanged
    }

    // -------------------------------------------------------------------------
    // ProductStatusChanged — updates status
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProductSummaryView_UpdatedWhenStatusChanged()
    {
        // Arrange: create the view first
        await SeedProductSummaryAsync("FISH-TANK-001", "Fish Tank", "Fish & Aquatics");

        var message = new ProductStatusChanged(
            Sku: "FISH-TANK-001",
            PreviousStatus: "Active",
            NewStatus: "Discontinued",
            OccurredAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var view = await session.LoadAsync<ProductSummaryView>("FISH-TANK-001");

        view.ShouldNotBeNull();
        view.Status.ShouldBe(ProductSummaryStatus.Discontinued);
        view.ProductName.ShouldBe("Fish Tank"); // unchanged
    }

    // -------------------------------------------------------------------------
    // Idempotency — ProductAdded does not overwrite existing view
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProductSummaryView_ProductAdded_IdempotentWhenAlreadyExists()
    {
        // Arrange: create the view first with specific data
        await SeedProductSummaryAsync("REPTILE-HEAT-001", "Reptile Heat Lamp", "Reptiles", 45.99m);

        // Act: send ProductAdded with different name — should be ignored
        var message = new ProductAdded(
            Sku: "REPTILE-HEAT-001",
            Name: "Different Name",
            Category: "Different Category",
            AddedAt: DateTimeOffset.UtcNow,
            Status: "Active");

        await _fixture.ExecuteAndWaitAsync(message);

        // Assert: original data preserved
        await using var session = _fixture.GetDocumentSession();
        var view = await session.LoadAsync<ProductSummaryView>("REPTILE-HEAT-001");

        view.ShouldNotBeNull();
        view.ProductName.ShouldBe("Reptile Heat Lamp");
        view.Category.ShouldBe("Reptiles");
        view.BasePrice.ShouldBe(45.99m);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task SeedProductSummaryAsync(
        string sku, string productName, string category, decimal? basePrice = null)
    {
        await using var session = _fixture.GetDocumentSession();
        session.Store(new ProductSummaryView
        {
            Id = sku,
            ProductName = productName,
            Category = category,
            BasePrice = basePrice,
            Status = ProductSummaryStatus.Active
        });
        await session.SaveChangesAsync();
    }
}
