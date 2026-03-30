using FluentValidation;
using Marten;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Listings;

namespace Listings.Listing;

public sealed record EndListing(Guid ListingId);

public sealed class EndListingValidator : AbstractValidator<EndListing>
{
    public EndListingValidator()
    {
        RuleFor(x => x.ListingId).NotEmpty();
    }
}

public static class EndListingHandler
{
    public static async Task<OutgoingMessages> Handle(
        EndListing command,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;

        var listing = await session.Events.AggregateStreamAsync<Listing>(command.ListingId);
        if (listing is null)
            throw new InvalidOperationException($"Listing '{command.ListingId}' not found.");

        if (listing.IsTerminal)
            throw new InvalidOperationException(
                $"Cannot end listing in '{listing.Status}' state. Listing is already in a terminal state.");

        var @event = new ListingEnded(
            command.ListingId,
            listing.Sku,
            listing.ChannelCode,
            EndedCause.ManualEnd,
            now);

        session.Events.Append(command.ListingId, @event);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ListingEnded(
            command.ListingId,
            listing.Sku,
            listing.ChannelCode,
            EndedCause.ManualEnd.ToString(),
            now));

        return outgoing;
    }
}
