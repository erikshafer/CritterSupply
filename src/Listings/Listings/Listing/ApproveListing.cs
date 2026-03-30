using FluentValidation;
using Marten;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Listings;

namespace Listings.Listing;

public sealed record ApproveListing(Guid ListingId);

public sealed class ApproveListingValidator : AbstractValidator<ApproveListing>
{
    public ApproveListingValidator()
    {
        RuleFor(x => x.ListingId).NotEmpty();
    }
}

public static class ApproveListingHandler
{
    public static async Task<OutgoingMessages> Handle(
        ApproveListing command,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;

        var listing = await session.Events.AggregateStreamAsync<Listing>(command.ListingId);
        if (listing is null)
            throw new InvalidOperationException($"Listing '{command.ListingId}' not found.");

        if (listing.Status != ListingStatus.ReadyForReview)
            throw new InvalidOperationException(
                $"Cannot approve listing in '{listing.Status}' state. Listing must be in 'ReadyForReview' state.");

        var @event = new ListingApproved(command.ListingId, now);
        session.Events.Append(command.ListingId, @event);

        // TODO(M37.x): Replace with ProductSummaryView ACL in Marketplaces BC
        var productSummary = await session.LoadAsync<Listings.ProductSummary.ProductSummaryView>(listing.Sku);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ListingApproved(
            command.ListingId,
            listing.Sku,
            listing.ChannelCode,
            listing.ProductName,
            productSummary?.Category,
            Price: null,
            now));

        return outgoing;
    }
}
