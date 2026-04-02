using Marten;
using IntegrationProductAdded = Messages.Contracts.ProductCatalog.ProductAdded;
using IntegrationProductContentUpdated = Messages.Contracts.ProductCatalog.ProductContentUpdated;
using IntegrationProductCategoryChanged = Messages.Contracts.ProductCatalog.ProductCategoryChanged;
using IntegrationProductStatusChanged = Messages.Contracts.ProductCatalog.ProductStatusChanged;

namespace Marketplaces.Products;

/// <summary>
/// Handles ProductAdded from Product Catalog BC to create a ProductSummaryView entry.
/// Only creates — does not update if already exists (idempotency guard).
/// </summary>
public static class ProductAddedHandler
{
    public static async Task Handle(IntegrationProductAdded message, IDocumentSession session)
    {
        var existing = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (existing is not null) return;

        var status = MapStatus(message.Status);

        session.Store(new ProductSummaryView
        {
            Id = message.Sku,
            ProductName = message.Name,
            Category = message.Category,
            Status = status
        });
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
/// Handles ProductContentUpdated to update ProductName in ProductSummaryView.
/// Marketplaces BC only needs the name — description is not used in listing submissions.
/// </summary>
public static class ProductContentUpdatedHandler
{
    public static async Task Handle(IntegrationProductContentUpdated message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        view.ProductName = message.Name;
        session.Store(view);
    }
}

/// <summary>
/// Handles ProductCategoryChanged to update Category in ProductSummaryView.
/// Category changes affect the category mapping lookup during listing submission.
/// </summary>
public static class ProductCategoryChangedHandler
{
    public static async Task Handle(IntegrationProductCategoryChanged message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        view.Category = message.NewCategory;
        session.Store(view);
    }
}

/// <summary>
/// Handles ProductStatusChanged to update Status in ProductSummaryView.
/// Status transitions may affect listing eligibility (e.g., Discontinued products
/// should not have new marketplace submissions).
/// </summary>
public static class ProductStatusChangedHandler
{
    public static async Task Handle(IntegrationProductStatusChanged message, IDocumentSession session)
    {
        var view = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (view is null) return;

        view.Status = message.NewStatus switch
        {
            "Active" => ProductSummaryStatus.Active,
            "ComingSoon" => ProductSummaryStatus.ComingSoon,
            "Discontinued" => ProductSummaryStatus.Discontinued,
            _ => ProductSummaryStatus.Active
        };

        session.Store(view);
    }
}
