using Marten;
using Marketplaces.Adapters;
using Marketplaces.CategoryMappings;
using Marketplaces.Marketplaces;
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
/// Guard rails:
/// - OWN_WEBSITE is skipped (Listings BC internal fast-path)
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

        // Look up category mapping
        var categoryMappingId = $"{message.ChannelCode}:{message.Category}";
        var categoryMapping = await session.LoadAsync<CategoryMapping>(categoryMappingId);
        if (categoryMapping is null)
        {
            outgoing.Add(new MarketplaceSubmissionRejected(
                message.ListingId,
                message.Sku,
                message.ChannelCode,
                $"No category mapping configured for {message.ChannelCode}:{message.Category}",
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

        // Build submission
        var submission = new ListingSubmission(
            ListingId: message.ListingId,
            Sku: message.Sku,
            ChannelCode: message.ChannelCode,
            ProductName: message.ProductName,
            Description: null,
            Category: categoryMapping.MarketplaceCategoryId,
            Price: message.Price ?? 0m);

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
