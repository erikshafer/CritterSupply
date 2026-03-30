using Listings.Listing;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine;
using Wolverine.Http;

namespace Listings.Api.Listings;

// ---------------------------------------------------------------------------
// Request / Response DTOs
// ---------------------------------------------------------------------------

public sealed record CreateListingRequest(string Sku, string ChannelCode, string? InitialContent);

public sealed record ListingResponse(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string ProductName,
    string? Content,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? EndedAt,
    string? EndCause,
    string? PauseReason);

// ---------------------------------------------------------------------------
// Mutation Endpoints
// ---------------------------------------------------------------------------

public static class CreateListingEndpoint
{
    [WolverinePost("/api/listings")]
    [Authorize]
    public static async Task<IResult> Handle(
        CreateListingRequest request,
        IMessageBus bus)
    {
        var command = new CreateListing(request.Sku, request.ChannelCode, request.InitialContent);
        var response = await bus.InvokeAsync<CreateListingResponse>(command);
        return Results.Created($"/api/listings/{response.ListingId}", response);
    }
}

public static class SubmitForReviewEndpoint
{
    [WolverinePost("/api/listings/{id}/submit-for-review")]
    [Authorize]
    public static async Task<IResult> Handle(
        Guid id,
        IMessageBus bus)
    {
        await bus.InvokeAsync(new SubmitListingForReview(id));
        return Results.Ok();
    }
}

public static class ApproveListingEndpoint
{
    [WolverinePost("/api/listings/{id}/approve")]
    [Authorize]
    public static async Task<IResult> Handle(
        Guid id,
        IMessageBus bus)
    {
        await bus.InvokeAsync(new ApproveListing(id));
        return Results.Ok();
    }
}

public static class ActivateListingEndpoint
{
    [WolverinePost("/api/listings/{id}/activate")]
    [Authorize]
    public static async Task<IResult> Handle(
        Guid id,
        IMessageBus bus)
    {
        await bus.InvokeAsync(new ActivateListing(id));
        return Results.Ok();
    }
}

public static class PauseListingEndpoint
{
    [WolverinePost("/api/listings/{id}/pause")]
    [Authorize]
    public static async Task<IResult> Handle(
        Guid id,
        string reason,
        IMessageBus bus)
    {
        await bus.InvokeAsync(new PauseListing(id, reason));
        return Results.Ok();
    }
}

public static class ResumeListingEndpoint
{
    [WolverinePost("/api/listings/{id}/resume")]
    [Authorize]
    public static async Task<IResult> Handle(
        Guid id,
        IMessageBus bus)
    {
        await bus.InvokeAsync(new ResumeListing(id));
        return Results.Ok();
    }
}

public static class EndListingEndpoint
{
    [WolverinePost("/api/listings/{id}/end")]
    [Authorize]
    public static async Task<IResult> Handle(
        Guid id,
        IMessageBus bus)
    {
        await bus.InvokeAsync(new EndListing(id));
        return Results.Ok();
    }
}

// ---------------------------------------------------------------------------
// Query Endpoints
// ---------------------------------------------------------------------------

public static class GetListingEndpoint
{
    [WolverineGet("/api/listings/{id}")]
    [Authorize]
    public static async Task<IResult> Handle(
        Guid id,
        IDocumentSession session,
        CancellationToken ct)
    {
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(id, token: ct);
        if (listing is null)
            return Results.NotFound();

        return Results.Ok(ToResponse(listing));
    }

    internal static ListingResponse ToResponse(Listing.Listing l) => new(
        ListingId: l.Id,
        Sku: l.Sku,
        ChannelCode: l.ChannelCode,
        ProductName: l.ProductName,
        Content: l.Content,
        Status: l.Status.ToString(),
        CreatedAt: l.CreatedAt,
        ActivatedAt: l.ActivatedAt,
        EndedAt: l.EndedAt,
        EndCause: l.EndCause?.ToString(),
        PauseReason: l.PauseReason);
}

public static class ListListingsEndpoint
{
    [WolverineGet("/api/listings")]
    [Authorize]
    public static async Task<IResult> Handle(
        string? sku,
        IDocumentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return Results.BadRequest(new { error = "sku query parameter is required" });

        // Query all listing snapshots for the given SKU
        var listings = await session
            .Query<Listing.Listing>()
            .Where(l => l.Sku == sku)
            .ToListAsync(ct);

        var responses = listings
            .Select(GetListingEndpoint.ToResponse)
            .ToList();

        return Results.Ok(responses);
    }
}
