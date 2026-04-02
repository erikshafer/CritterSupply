using Marten;
using Marketplaces.Adapters;
using Marketplaces.CategoryMappings;
using Marketplaces.Marketplaces;
using Marketplaces.Products;
using Messages.Contracts.Listings;
using Messages.Contracts.Marketplaces;
using Wolverine;

namespace Marketplaces.Api.Listings;

/// <summary>
/// Consumes <see cref="ListingApproved"/> from the Listings BC via RabbitMQ.
/// Resolves the marketplace adapter by ChannelCode, submits the listing,
/// and publishes either <see cref="MarketplaceListingActivated"/> or
/// <see cref="MarketplaceSubmissionRejected"/>.
///
/// Product data (name, category, price) is sourced from the local
/// <see cref="ProductSummaryView"/> ACL document — not from the message payload.
/// This decouples Marketplaces BC from the Listings BC message enrichment.
///
/// Guard rails:
/// - OWN_WEBSITE is skipped (Listings BC internal fast-path)
/// - Missing ProductSummaryView publishes rejection
/// - Missing category on the product publishes rejection
/// - Missing category mapping publishes rejection (GR-NEW-2)
/// - Inactive marketplace publishes rejection
/// </summary>
public static class ListingApprovedHandler
{
    public static async Task<OutgoingMessages> Handle(
        ListingApproved message,
        IDocumentSession session,
        IReadOnlyDictionary<string, IMarketplaceAdapter> adapters)
    {
        var outgoing = new OutgoingMessages();
        var now = DateTimeOffset.UtcNow;

        // Guard: OWN_WEBSITE is the Listings BC's internal fast-path — skip adapter
        if (string.Equals(message.ChannelCode, "OWN_WEBSITE", StringComparison.OrdinalIgnoreCase))
            return outgoing;

        // Query local ProductSummaryView ACL for product data
        var productSummary = await session.LoadAsync<ProductSummaryView>(message.Sku);
        if (productSummary is null)
        {
            outgoing.Add(new MarketplaceSubmissionRejected(
                message.ListingId,
                message.Sku,
                message.ChannelCode,
                $"Product '{message.Sku}' not yet known to Marketplaces BC — ProductSummaryView missing",
                now));
            return outgoing;
        }

        // Guard: category must be present on the product summary
        if (string.IsNullOrWhiteSpace(productSummary.Category))
        {
            outgoing.Add(new MarketplaceSubmissionRejected(
                message.ListingId,
                message.Sku,
                message.ChannelCode,
                "Product has no category in Marketplaces ProductSummaryView — cannot determine category mapping",
                now));
            return outgoing;
        }

        // Look up category mapping using product summary's category
        var categoryMappingId = $"{message.ChannelCode}:{productSummary.Category}";
        var categoryMapping = await session.LoadAsync<CategoryMapping>(categoryMappingId);
        if (categoryMapping is null)
        {
            outgoing.Add(new MarketplaceSubmissionRejected(
                message.ListingId,
                message.Sku,
                message.ChannelCode,
                $"No category mapping configured for {message.ChannelCode}:{productSummary.Category}",
                now));
            return outgoing;
        }

        // Verify marketplace is active
        var marketplace = await session.LoadAsync<Marketplace>(message.ChannelCode);
        if (marketplace is null || !marketplace.IsActive)
        {
            outgoing.Add(new MarketplaceSubmissionRejected(
                message.ListingId,
                message.Sku,
                message.ChannelCode,
                $"Marketplace '{message.ChannelCode}' is not active or does not exist",
                now));
            return outgoing;
        }

        // Resolve adapter by channel code
        if (!adapters.TryGetValue(message.ChannelCode, out var adapter))
        {
            outgoing.Add(new MarketplaceSubmissionRejected(
                message.ListingId,
                message.Sku,
                message.ChannelCode,
                $"No adapter registered for channel '{message.ChannelCode}'",
                now));
            return outgoing;
        }

        // Build submission from local ProductSummaryView data — zero reads from message payload
        var submission = new ListingSubmission(
            ListingId: message.ListingId,
            Sku: message.Sku,
            ChannelCode: message.ChannelCode,
            ProductName: productSummary.ProductName,
            Description: null,
            Category: categoryMapping.MarketplaceCategoryId,
            Price: productSummary.BasePrice ?? 0m);

        // Submit to marketplace adapter
        var result = await adapter.SubmitListingAsync(submission);

        if (result.IsSuccess)
        {
            outgoing.Add(new MarketplaceListingActivated(
                message.ListingId,
                message.Sku,
                message.ChannelCode,
                result.ExternalSubmissionId!,
                now));
        }
        else
        {
            outgoing.Add(new MarketplaceSubmissionRejected(
                message.ListingId,
                message.Sku,
                message.ChannelCode,
                result.ErrorMessage ?? "Adapter submission failed",
                now));
        }

        return outgoing;
    }
}
