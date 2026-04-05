using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Listings.Listing;

public sealed record ResumeListing(Guid ListingId);

public sealed class ResumeListingValidator : AbstractValidator<ResumeListing>
{
    public ResumeListingValidator()
    {
        RuleFor(x => x.ListingId).NotEmpty();
    }
}

public static class ResumeListingHandler
{
    public static ProblemDetails Before(ResumeListing cmd, Listing? listing)
    {
        if (listing is null)
            return new ProblemDetails { Detail = $"Listing '{cmd.ListingId}' not found", Status = 404 };
        if (listing.Status != ListingStatus.Paused)
            return new ProblemDetails { Detail = $"Cannot resume listing in '{listing.Status}' state. Listing must be 'Paused' to resume.", Status = 409 };
        return WolverineContinue.NoProblems;
    }

    public static Events Handle(
        ResumeListing cmd,
        [WriteAggregate] Listing listing)
    {
        var events = new Events();
        events.Add(new ListingResumed(cmd.ListingId, DateTimeOffset.UtcNow));
        return events;
    }
}
