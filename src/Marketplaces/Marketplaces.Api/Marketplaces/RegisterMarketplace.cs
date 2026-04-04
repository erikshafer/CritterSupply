using FluentValidation;
using Marten;
using Marketplaces.Marketplaces;
using Messages.Contracts.Marketplaces;
using Microsoft.AspNetCore.Authorization;
using Wolverine;
using Wolverine.Http;

namespace Marketplaces.Api.Marketplaces;

// ---------------------------------------------------------------------------
// Command + Validator + Response
// ---------------------------------------------------------------------------

public sealed record RegisterMarketplace(
    string ChannelCode,
    string DisplayName,
    string? ApiCredentialVaultPath = null);

public sealed record RegisterMarketplaceResponse(
    string ChannelCode,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed class RegisterMarketplaceValidator : AbstractValidator<RegisterMarketplace>
{
    public RegisterMarketplaceValidator()
    {
        RuleFor(x => x.ChannelCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ApiCredentialVaultPath).MaximumLength(500)
            .When(x => x.ApiCredentialVaultPath is not null);
    }
}

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

public static class RegisterMarketplaceEndpoint
{
    /// <summary>
    /// Registers a new marketplace channel. Idempotent by ChannelCode (GR-NEW-3):
    /// if the channel code already exists, returns 200 with the existing document
    /// rather than 409. Publishes <see cref="MarketplaceRegistered"/> on new registrations.
    /// </summary>
    [WolverinePost("/api/marketplaces")]
    [Authorize]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        RegisterMarketplace command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();

        var existing = await session.LoadAsync<Marketplace>(command.ChannelCode, ct);
        if (existing is not null)
        {
            // Idempotent upsert — return existing document with 200, no event published
            return (Results.Ok(new RegisterMarketplaceResponse(
                existing.Id,
                existing.DisplayName,
                existing.IsActive,
                existing.CreatedAt)), outgoing);
        }

        var now = DateTimeOffset.UtcNow;
        var marketplace = new Marketplace
        {
            Id = command.ChannelCode,
            DisplayName = command.DisplayName,
            IsActive = true,
            IsOwnWebsite = false,
            ApiCredentialVaultPath = command.ApiCredentialVaultPath,
            CreatedAt = now,
            UpdatedAt = now
        };

        session.Store(marketplace);

        outgoing.Add(new MarketplaceRegistered(
            marketplace.Id,
            marketplace.DisplayName,
            now));

        return (Results.Created(
            $"/api/marketplaces/{marketplace.Id}",
            new RegisterMarketplaceResponse(
                marketplace.Id,
                marketplace.DisplayName,
                marketplace.IsActive,
                marketplace.CreatedAt)), outgoing);
    }
}
