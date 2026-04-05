using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Listings.Listing;

public sealed record SubmitListingForReview(Guid ListingId);

public sealed class SubmitListingForReviewValidator : AbstractValidator<SubmitListingForReview>
{
    public SubmitListingForReviewValidator()
    {
        RuleFor(x => x.ListingId).NotEmpty();
    }
}

public static class SubmitListingForReviewHandler
{
    public static ProblemDetails Before(SubmitListingForReview cmd, Listing? listing)
    {
        if (listing is null)
            return new ProblemDetails { Detail = $"Listing '{cmd.ListingId}' not found", Status = 404 };
        if (listing.Status != ListingStatus.Draft)
            return new ProblemDetails { Detail = $"Cannot submit listing for review in '{listing.Status}' state. Must be Draft.", Status = 409 };
        return WolverineContinue.NoProblems;
    }

    public static Events Handle(
        SubmitListingForReview cmd,
        [WriteAggregate] Listing listing)
    {
        var events = new Events();
        events.Add(new ListingSubmittedForReview(cmd.ListingId, DateTimeOffset.UtcNow));
        return events;
    }
}
