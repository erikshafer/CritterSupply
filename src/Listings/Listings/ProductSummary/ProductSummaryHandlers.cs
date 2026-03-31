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
/// Handles ProductAdded from Product Catalog to create a ProductSummaryView.
/// </summary>
public static class ProductAddedHandler
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

    private static ProductSummaryStatus MapStatus(string? status) => status switch
    {
        "Active" => ProductSummaryStatus.Active,
        "ComingSoon" => ProductSummaryStatus.ComingSoon,
        "Discontinued" => ProductSummaryStatus.Discontinued,
        _ => ProductSummaryStatus.Active
    };
}

/// <summary>
/// Handles ProductContentUpdated to update Name and Description in ProductSummaryView.
/// </summary>
public static class ProductContentUpdatedHandler
{
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
}

/// <summary>
/// Handles ProductCategoryChanged to update Category in ProductSummaryView.
/// </summary>
public static class ProductCategoryChangedHandler
{
    public static async Task Handle(IntegrationProductCategoryChanged message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { Category = message.NewCategory });
    }
}

/// <summary>
/// Handles ProductImagesUpdated to refresh ImageUrls in ProductSummaryView.
/// </summary>
public static class ProductImagesUpdatedHandler
{
    public static async Task Handle(IntegrationProductImagesUpdated message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { ImageUrls = message.ImageUrls });
    }
}

/// <summary>
/// Handles ProductDimensionsChanged to update HasDimensions in ProductSummaryView.
/// </summary>
public static class ProductDimensionsChangedHandler
{
    public static async Task Handle(IntegrationProductDimensionsChanged message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { HasDimensions = true });
    }
}

/// <summary>
/// Handles ProductStatusChanged to update Status in ProductSummaryView.
/// </summary>
public static class ProductStatusChangedHandler
{
    public static async Task Handle(IntegrationProductStatusChanged message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        var status = message.NewStatus switch
        {
            "Active" => ProductSummaryStatus.Active,
            "ComingSoon" => ProductSummaryStatus.ComingSoon,
            "Discontinued" => ProductSummaryStatus.Discontinued,
            _ => ProductSummaryStatus.Active
        };

        session.Store(view with { Status = status });
    }
}

/// <summary>
/// Handles ProductDeleted to set Status = Deleted in ProductSummaryView.
/// </summary>
public static class ProductDeletedHandler
{
    public static async Task Handle(IntegrationProductDeleted message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { Status = ProductSummaryStatus.Deleted });
    }
}

/// <summary>
/// Handles ProductRestored to set Status = Active in ProductSummaryView.
/// </summary>
public static class ProductRestoredHandler
{
    public static async Task Handle(IntegrationProductRestored message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { Status = ProductSummaryStatus.Active });
    }
}

/// <summary>
/// Handles ProductDiscontinued to set Status = Discontinued in ProductSummaryView.
/// Note: The recall cascade is handled separately by RecallCascadeHandler.
/// This handler only updates the product summary.
/// </summary>
public static class ProductDiscontinuedSummaryHandler
{
    public static async Task Handle(IntegrationProductDiscontinued message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        session.Store(view with { Status = ProductSummaryStatus.Discontinued });
    }
}
