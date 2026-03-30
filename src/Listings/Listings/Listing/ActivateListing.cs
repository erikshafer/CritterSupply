using FluentValidation;
using Marten;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Listings;

namespace Listings.Listing;

public sealed record ActivateListing(Guid ListingId);

public sealed class ActivateListingValidator : AbstractValidator<ActivateListing>
{
    public ActivateListingValidator()
    {
        RuleFor(x => x.ListingId).NotEmpty();
    }
}

public static class ActivateListingHandler
{
    public static async Task<OutgoingMessages> Handle(
        ActivateListing command,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;

        var listing = await session.Events.AggregateStreamAsync<Listing>(command.ListingId);
        if (listing is null)
            throw new InvalidOperationException($"Listing '{command.ListingId}' not found.");

        // Valid transitions to Live:
        // - Submitted → Live (normal marketplace flow)
        // - Draft → Live (OWN_WEBSITE fast path)
        var isOwnWebsiteFastPath = listing.Status == ListingStatus.Draft
            && string.Equals(listing.ChannelCode, "OWN_WEBSITE", StringComparison.OrdinalIgnoreCase);

        if (listing.Status != ListingStatus.Submitted && !isOwnWebsiteFastPath)
            throw new InvalidOperationException(
                $"Cannot activate listing in '{listing.Status}' state. " +
                "Listing must be in 'Submitted' state (or 'Draft' for OWN_WEBSITE channel).");

        var @event = new ListingActivated(command.ListingId, listing.ChannelCode, now);
        session.Events.Append(command.ListingId, @event);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ListingActivated(
            command.ListingId,
            listing.ChannelCode,
            now));

        return outgoing;
    }
}
