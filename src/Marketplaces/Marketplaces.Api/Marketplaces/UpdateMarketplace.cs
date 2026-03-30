using FluentValidation;
using Marten;
using Marketplaces.Marketplaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Marketplaces.Api.Marketplaces;

// ---------------------------------------------------------------------------
// Command + Validator + Response
// ---------------------------------------------------------------------------

public sealed record UpdateMarketplace(
    string ChannelCode,
    string DisplayName,
    string? ApiCredentialVaultPath = null);

public sealed class UpdateMarketplaceValidator : AbstractValidator<UpdateMarketplace>
{
    public UpdateMarketplaceValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ApiCredentialVaultPath).MaximumLength(500)
            .When(x => x.ApiCredentialVaultPath is not null);
    }
}

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

public static class UpdateMarketplaceEndpoint
{
    [WolverinePut("/api/marketplaces/{channelCode}")]
    [Authorize]
    public static async Task<IResult> Handle(
        string channelCode,
        [FromBody] UpdateMarketplace command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var marketplace = await session.LoadAsync<Marketplace>(channelCode, ct);
        if (marketplace is null)
            return Results.NotFound(new ProblemDetails { Detail = $"Marketplace '{channelCode}' not found.", Status = 404 });

        marketplace.DisplayName = command.DisplayName;
        marketplace.ApiCredentialVaultPath = command.ApiCredentialVaultPath;
        marketplace.UpdatedAt = DateTimeOffset.UtcNow;

        session.Store(marketplace);
        await session.SaveChangesAsync(ct);

        return Results.Ok(marketplace);
    }
}
