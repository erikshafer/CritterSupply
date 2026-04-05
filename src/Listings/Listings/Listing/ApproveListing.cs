using FluentValidation;
using Listings.ProductSummary;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
using IntegrationMessages = Messages.Contracts.Listings;

namespace Listings.Listing;

public sealed record ApproveListing(Guid ListingId);

public sealed class ApproveListingValidator : AbstractValidator<ApproveListing>
{
    public ApproveListingValidator()
    {
        RuleFor(x => x.ListingId).NotEmpty();
    }
}

public static class ApproveListingHandler
{
    public static ProblemDetails Before(ApproveListing cmd, Listing? listing)
    {
        if (listing is null)
            return new ProblemDetails { Detail = $"Listing '{cmd.ListingId}' not found", Status = 404 };
        if (listing.Status != ListingStatus.ReadyForReview)
            return new ProblemDetails { Detail = $"Cannot approve listing in '{listing.Status}' state. Must be ReadyForReview.", Status = 409 };
        return WolverineContinue.NoProblems;
    }

    public static async Task<(Events, OutgoingMessages)> Handle(
        ApproveListing cmd,
        [WriteAggregate] Listing listing,
        IQuerySession session)
    {
        var now = DateTimeOffset.UtcNow;
        var events = new Events();
        events.Add(new ListingApproved(cmd.ListingId, now));

        // TODO(M37.0): Replace with ProductSummaryView ACL in Marketplaces BC
        var productSummary = await session.LoadAsync<ProductSummaryView>(listing.Sku);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ListingApproved(
            cmd.ListingId,
            listing.Sku,
            listing.ChannelCode,
            listing.ProductName,
            productSummary?.Category,
            Price: null,
            now));

        return (events, outgoing);
    }
}
