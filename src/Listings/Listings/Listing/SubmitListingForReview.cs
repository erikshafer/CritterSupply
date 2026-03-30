using FluentValidation;
using Marten;

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
    public static async Task Handle(
        SubmitListingForReview command,
        IDocumentSession session)
    {
        var listing = await session.Events.AggregateStreamAsync<Listing>(command.ListingId);
        if (listing is null)
            throw new InvalidOperationException($"Listing '{command.ListingId}' not found.");

        if (listing.Status != ListingStatus.Draft)
            throw new InvalidOperationException(
                $"Cannot submit listing for review in '{listing.Status}' state. Listing must be in 'Draft' state.");

        var @event = new ListingSubmittedForReview(command.ListingId, DateTimeOffset.UtcNow);
        session.Events.Append(command.ListingId, @event);
    }
}
