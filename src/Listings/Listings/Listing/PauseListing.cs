using FluentValidation;
using Marten;

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
    public static async Task Handle(
        PauseListing command,
        IDocumentSession session)
    {
        var listing = await session.Events.AggregateStreamAsync<Listing>(command.ListingId);
        if (listing is null)
            throw new InvalidOperationException($"Listing '{command.ListingId}' not found.");

        if (listing.Status != ListingStatus.Live)
            throw new InvalidOperationException(
                $"Cannot pause listing in '{listing.Status}' state. Listing must be 'Live' to pause.");

        var @event = new ListingPaused(command.ListingId, command.Reason, DateTimeOffset.UtcNow);
        session.Events.Append(command.ListingId, @event);
    }
}
