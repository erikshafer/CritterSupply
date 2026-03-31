using Marten;
using Marketplaces.Marketplaces;
using Messages.Contracts.Marketplaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Marketplaces.Api.Marketplaces;

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

public static class DeactivateMarketplaceEndpoint
{
    /// <summary>
    /// Deactivates an active marketplace. Idempotent — deactivating an already
    /// deactivated marketplace returns 200 with the current state.
    /// Publishes <see cref="MarketplaceDeactivated"/> only when transitioning from active to inactive.
    /// </summary>
    [WolverinePost("/api/marketplaces/{channelCode}/deactivate")]
    [Authorize]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        string channelCode,
        IDocumentSession session,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();

        var marketplace = await session.LoadAsync<Marketplace>(channelCode, ct);
        if (marketplace is null)
            return (Results.NotFound(new ProblemDetails { Detail = $"Marketplace '{channelCode}' not found.", Status = 404 }), outgoing);

        var wasActive = marketplace.IsActive;

        marketplace.IsActive = false;
        marketplace.UpdatedAt = DateTimeOffset.UtcNow;

        session.Store(marketplace);
        await session.SaveChangesAsync(ct);

        // Only publish when actually transitioning from active → inactive
        if (wasActive)
        {
            outgoing.Add(new MarketplaceDeactivated(
                marketplace.Id,
                marketplace.UpdatedAt));
        }

        return (Results.Ok(marketplace), outgoing);
    }
}
