using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Listings.Listing;

public sealed record PauseListing(Guid ListingId, string Reason);

public sealed class PauseListingValidator : AbstractValidator<PauseListing>
{
    public PauseListingValidator()
    {
        RuleFor(x => x.ListingId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public static class PauseListingHandler
{
    public static ProblemDetails Before(PauseListing cmd, Listing? listing)
    {
        if (listing is null)
            return new ProblemDetails { Detail = $"Listing '{cmd.ListingId}' not found", Status = 404 };
        if (listing.Status != ListingStatus.Live)
            return new ProblemDetails { Detail = $"Cannot pause listing in '{listing.Status}' state. Listing must be 'Live' to pause.", Status = 409 };
        return WolverineContinue.NoProblems;
    }

    public static Events Handle(
        PauseListing cmd,
        [WriteAggregate] Listing listing)
    {
        var events = new Events();
        events.Add(new ListingPaused(cmd.ListingId, cmd.Reason, DateTimeOffset.UtcNow));
        return events;
    }
}
