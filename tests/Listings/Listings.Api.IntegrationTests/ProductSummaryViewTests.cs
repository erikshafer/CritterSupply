using Listings.ProductSummary;
using Marten;
using Shouldly;
using Wolverine.Tracking;

namespace Listings.Api.IntegrationTests;

/// <summary>
/// Tests that Product Catalog integration events correctly maintain the ProductSummaryView.
/// Uses direct Wolverine handler invocation to simulate incoming integration messages.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ProductSummaryViewTests
{
    private readonly TestFixture _fixture;

    public ProductSummaryViewTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProductAdded_CreatesProductSummaryView()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var message = new Messages.Contracts.ProductCatalog.ProductAdded(
            Sku: "SUM-001",
            Name: "Test Dog Bowl",
            Category: "Dogs",
            AddedAt: DateTimeOffset.UtcNow,
            Status: "Active",
            Brand: "PetSupreme",
            HasDimensions: true);

        // Act
        await _fixture.ExecuteAndWaitAsync(message);

        // Assert
        using var session = _fixture.GetDocumentSession();
        var view = await session.LoadAsync<ProductSummaryView>("SUM-001");
        view.ShouldNotBeNull();
        view.Name.ShouldBe("Test Dog Bowl");
        view.Category.ShouldBe("Dogs");
        view.Status.ShouldBe(ProductSummaryStatus.Active);
        view.Brand.ShouldBe("PetSupreme");
        view.HasDimensions.ShouldBeTrue();
    }

    [Fact]
    public async Task ProductStatusChanged_ToDiscontinued_UpdatesSummaryView()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        // Seed an Active product first
        using (var session = _fixture.GetDocumentSession())
        {
            session.Store(new ProductSummaryView
            {
                Id = "SUM-DISC-001",
                Name = "Soon Discontinued Product",
                Category = "Dogs",
                Status = ProductSummaryStatus.Active,
                ImageUrls = []
            });
            await session.SaveChangesAsync();
        }

        var message = new Messages.Contracts.ProductCatalog.ProductStatusChanged(
            Sku: "SUM-DISC-001",
            PreviousStatus: "Active",
            NewStatus: "Discontinued",
            OccurredAt: DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteAndWaitAsync(message);

        // Assert
        using var querySession = _fixture.GetDocumentSession();
        var view = await querySession.LoadAsync<ProductSummaryView>("SUM-DISC-001");
        view.ShouldNotBeNull();
        view.Status.ShouldBe(ProductSummaryStatus.Discontinued);
    }

    [Fact]
    public async Task ProductDeleted_SetsStatusDeleted_InSummaryView()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        using (var session = _fixture.GetDocumentSession())
        {
            session.Store(new ProductSummaryView
            {
                Id = "SUM-DEL-001",
                Name = "Deletable Product",
                Category = "Cats",
                Status = ProductSummaryStatus.Active,
                ImageUrls = []
            });
            await session.SaveChangesAsync();
        }

        var message = new Messages.Contracts.ProductCatalog.ProductDeleted(
            Sku: "SUM-DEL-001",
            OccurredAt: DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteAndWaitAsync(message);

        // Assert
        using var querySession = _fixture.GetDocumentSession();
        var view = await querySession.LoadAsync<ProductSummaryView>("SUM-DEL-001");
        view.ShouldNotBeNull();
        view.Status.ShouldBe(ProductSummaryStatus.Deleted);
    }

    [Fact]
    public async Task ProductContentUpdated_UpdatesNameAndDescription()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        using (var session = _fixture.GetDocumentSession())
        {
            session.Store(new ProductSummaryView
            {
                Id = "SUM-UPD-001",
                Name = "Original Name",
                Description = "Original Description",
                Category = "Dogs",
                Status = ProductSummaryStatus.Active,
                ImageUrls = []
            });
            await session.SaveChangesAsync();
        }

        var message = new Messages.Contracts.ProductCatalog.ProductContentUpdated(
            Sku: "SUM-UPD-001",
            Name: "Updated Name",
            Description: "Updated Description",
            OccurredAt: DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteAndWaitAsync(message);

        // Assert
        using var querySession = _fixture.GetDocumentSession();
        var view = await querySession.LoadAsync<ProductSummaryView>("SUM-UPD-001");
        view.ShouldNotBeNull();
        view.Name.ShouldBe("Updated Name");
        view.Description.ShouldBe("Updated Description");
    }
}
