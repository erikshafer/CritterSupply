using Listings.ProductSummary;
using Marten;
using IntegrationProductAdded = Messages.Contracts.ProductCatalog.ProductAdded;
using IntegrationProductContentUpdated = Messages.Contracts.ProductCatalog.ProductContentUpdated;
using IntegrationProductCategoryChanged = Messages.Contracts.ProductCatalog.ProductCategoryChanged;
using IntegrationProductImagesUpdated = Messages.Contracts.ProductCatalog.ProductImagesUpdated;
using IntegrationProductDimensionsChanged = Messages.Contracts.ProductCatalog.ProductDimensionsChanged;
using IntegrationProductStatusChanged = Messages.Contracts.ProductCatalog.ProductStatusChanged;
using IntegrationProductDeleted = Messages.Contracts.ProductCatalog.ProductDeleted;
using IntegrationProductRestored = Messages.Contracts.ProductCatalog.ProductRestored;
using IntegrationProductDiscontinued = Messages.Contracts.ProductCatalog.ProductDiscontinued;

namespace Listings.ProductSummary;

/// <summary>
/// Handlers that consume Product Catalog integration events to maintain ProductSummaryView.
/// The Listings BC NEVER calls the Product Catalog API — all product data flows through these handlers.
/// </summary>
public static class ProductSummaryHandlers
{
    public static void Handle(IntegrationProductAdded message, IDocumentSession session)
    {
        var status = MapStatus(message.Status);

        var view = new ProductSummaryView
        {
            Id = message.Sku,
            Name = message.Name,
            Category = message.Category,
            Status = status,
            Brand = message.Brand,
            HasDimensions = message.HasDimensions ?? false,
            ImageUrls = []
        };

        session.Store(view);
    }

    public static async Task Handle(IntegrationProductContentUpdated message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with
        {
            Name = message.Name,
            Description = message.Description
        });
    }

    public static async Task Handle(IntegrationProductCategoryChanged message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { Category = message.NewCategory });
    }

    public static async Task Handle(IntegrationProductImagesUpdated message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { ImageUrls = message.ImageUrls });
    }

    public static async Task Handle(IntegrationProductDimensionsChanged message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { HasDimensions = true });
    }

    public static async Task Handle(IntegrationProductStatusChanged message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { Status = MapStatus(message.NewStatus) });
    }

    public static async Task Handle(IntegrationProductDeleted message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { Status = ProductSummaryStatus.Deleted });
    }

    public static async Task Handle(IntegrationProductRestored message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { Status = ProductSummaryStatus.Active });
    }

    public static async Task Handle(IntegrationProductDiscontinued message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { Status = ProductSummaryStatus.Discontinued });
    }

    private static ProductSummaryStatus MapStatus(string? status) => status switch
    {
        "Active" => ProductSummaryStatus.Active,
        "ComingSoon" => ProductSummaryStatus.ComingSoon,
        "Discontinued" => ProductSummaryStatus.Discontinued,
        _ => ProductSummaryStatus.Active
    };
}
