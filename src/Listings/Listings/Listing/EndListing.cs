using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
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
    public static ProblemDetails Before(EndListing cmd, Listing? listing)
    {
        if (listing is null)
            return new ProblemDetails { Detail = $"Listing '{cmd.ListingId}' not found", Status = 404 };
        if (listing.IsTerminal)
            return new ProblemDetails { Detail = $"Cannot end listing in '{listing.Status}' state. Listing is already in a terminal state.", Status = 409 };
        return WolverineContinue.NoProblems;
    }

    public static (Events, OutgoingMessages) Handle(
        EndListing cmd,
        [WriteAggregate] Listing listing)
    {
        var now = DateTimeOffset.UtcNow;

        var events = new Events();
        events.Add(new ListingEnded(
            cmd.ListingId,
            listing.Sku,
            listing.ChannelCode,
            EndedCause.ManualEnd,
            now));

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ListingEnded(
            cmd.ListingId,
            listing.Sku,
            listing.ChannelCode,
            EndedCause.ManualEnd.ToString(),
            now));

        return (events, outgoing);
    }
}
