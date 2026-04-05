using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
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
    public static ProblemDetails Before(ActivateListing cmd, Listing? listing)
    {
        if (listing is null)
            return new ProblemDetails { Detail = $"Listing '{cmd.ListingId}' not found", Status = 404 };

        // Valid transitions to Live:
        // - Submitted → Live (normal marketplace flow)
        // - Draft → Live (OWN_WEBSITE fast path)
        var isOwnWebsiteFastPath = listing.Status == ListingStatus.Draft
            && string.Equals(listing.ChannelCode, "OWN_WEBSITE", StringComparison.OrdinalIgnoreCase);

        if (listing.Status != ListingStatus.Submitted && !isOwnWebsiteFastPath)
            return new ProblemDetails
            {
                Detail = $"Cannot activate listing in '{listing.Status}' state. " +
                    "Listing must be in 'Submitted' state (or 'Draft' for OWN_WEBSITE channel).",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static (Events, OutgoingMessages) Handle(
        ActivateListing cmd,
        [WriteAggregate] Listing listing)
    {
        var now = DateTimeOffset.UtcNow;
        var events = new Events();
        events.Add(new ListingActivated(cmd.ListingId, listing.ChannelCode, now));

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ListingActivated(
            cmd.ListingId,
            listing.ChannelCode,
            now));

        return (events, outgoing);
    }
}
