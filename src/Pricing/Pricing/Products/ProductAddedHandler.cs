using Messages.Contracts.ProductCatalog;
using Wolverine.Marten;

namespace Pricing.Products;

/// <summary>
/// Integration handler: ProductAdded from Product Catalog BC.
/// Creates ProductPrice event stream in Unpriced status.
/// This "registers" the SKU so subsequent SetPrice commands have a stream to append to.
/// Idempotency: Wolverine's transactional outbox ensures exactly-once processing.
/// If duplicate ProductAdded arrives, Marten will throw on duplicate stream ID (by design).
/// </summary>
public static class ProductAddedHandler
{
    public static IStartStream Handle(ProductAdded message)
    {
        var streamId = ProductPrice.StreamId(message.Sku);

        // Start new event stream with ProductRegistered
        // Wolverine automatically handles persistence via transactional outbox
        return MartenOps.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(
                streamId,
                message.Sku.ToUpperInvariant(),
                message.AddedAt));
    }
}
