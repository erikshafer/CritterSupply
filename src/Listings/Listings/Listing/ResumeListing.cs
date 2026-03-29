using FluentValidation;
using Marten;

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
    public static async Task Handle(
        ResumeListing command,
        IDocumentSession session)
    {
        var listing = await session.Events.AggregateStreamAsync<Listing>(command.ListingId);
        if (listing is null)
            throw new InvalidOperationException($"Listing '{command.ListingId}' not found.");

        if (listing.Status != ListingStatus.Paused)
            throw new InvalidOperationException(
                $"Cannot resume listing in '{listing.Status}' state. Listing must be 'Paused' to resume.");

        var @event = new ListingResumed(command.ListingId, DateTimeOffset.UtcNow);
        session.Events.Append(command.ListingId, @event);
    }
}
