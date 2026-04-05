using FluentValidation;
using Listings.ProductSummary;
using Listings.Projections;
using Marten;
using Wolverine;
using Wolverine.Marten;
using IntegrationMessages = Messages.Contracts.Listings;

namespace Listings.Listing;

public sealed record CreateListing(
    string Sku,
    string ChannelCode,
    string? InitialContent);

public sealed record CreateListingResponse(
    Guid ListingId,
    string Sku,
    string ChannelCode);

public sealed class CreateListingValidator : AbstractValidator<CreateListing>
{
    public CreateListingValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ChannelCode).NotEmpty().MaximumLength(50);
    }
}

public static class CreateListingHandler
{
    public static async Task<(CreateListingResponse, IStartStream, OutgoingMessages)> Handle(
        CreateListing command,
        IQuerySession session)
    {
        var now = DateTimeOffset.UtcNow;

        // Validate product exists in ProductSummaryView (anti-corruption layer)
        var product = await session.LoadAsync<ProductSummaryView>(command.Sku);
        if (product is null)
            throw new InvalidOperationException($"Product '{command.Sku}' not found. Product must exist in the catalog before a listing can be created.");

        // Validate product status allows listing
        if (product.Status is ProductSummaryStatus.Discontinued or ProductSummaryStatus.Deleted)
            throw new InvalidOperationException($"Product '{command.Sku}' has status '{product.Status}' and cannot be listed.");

        // Compute deterministic stream ID
        var listingId = ListingStreamId.Compute(command.Sku, command.ChannelCode);

        // Check for existing non-ended listing for same SKU+channel via ListingsActiveView
        var activeView = await session.LoadAsync<ListingsActiveView>(command.Sku);
        if (activeView is not null && activeView.ActiveListingStreamIds.Contains(listingId))
            throw new InvalidOperationException($"An active listing already exists for SKU '{command.Sku}' on channel '{command.ChannelCode}'.");

        var @event = new ListingDraftCreated(
            listingId,
            command.Sku,
            command.ChannelCode,
            product.Name,
            command.InitialContent,
            now);

        var stream = MartenOps.StartStream<Listing>(listingId, @event);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ListingCreated(
            listingId,
            command.Sku,
            command.ChannelCode,
            now));

        return (new CreateListingResponse(listingId, command.Sku, command.ChannelCode), stream, outgoing);
    }
}
