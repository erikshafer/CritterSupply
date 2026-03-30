using FluentValidation;
using Marten;
using Marketplaces.CategoryMappings;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Marketplaces.Api.CategoryMappings;

// ---------------------------------------------------------------------------
// Command + Validator + Response
// ---------------------------------------------------------------------------

public sealed record SetCategoryMappingRequest(
    string ChannelCode,
    string InternalCategory,
    string MarketplaceCategoryId,
    string? MarketplaceCategoryPath = null);

public sealed class SetCategoryMappingValidator : AbstractValidator<SetCategoryMappingRequest>
{
    public SetCategoryMappingValidator()
    {
        RuleFor(x => x.ChannelCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.InternalCategory).NotEmpty().MaximumLength(200);
        RuleFor(x => x.MarketplaceCategoryId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.MarketplaceCategoryPath).MaximumLength(500)
            .When(x => x.MarketplaceCategoryPath is not null);
    }
}

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

public static class SetCategoryMappingEndpoint
{
    /// <summary>
    /// Upserts a category mapping by composite key "{ChannelCode}:{InternalCategory}".
    /// Creates a new mapping or updates an existing one.
    /// </summary>
    [WolverinePost("/api/category-mappings")]
    [Authorize]
    public static async Task<IResult> Handle(
        SetCategoryMappingRequest command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var compositeId = $"{command.ChannelCode}:{command.InternalCategory}";
        var now = DateTimeOffset.UtcNow;

        var existing = await session.LoadAsync<CategoryMapping>(compositeId, ct);
        if (existing is not null)
        {
            existing.MarketplaceCategoryId = command.MarketplaceCategoryId;
            existing.MarketplaceCategoryPath = command.MarketplaceCategoryPath;
            existing.LastVerifiedAt = now;
            session.Store(existing);
            await session.SaveChangesAsync(ct);
            return Results.Ok(existing);
        }

        var mapping = new CategoryMapping
        {
            Id = compositeId,
            ChannelCode = command.ChannelCode,
            InternalCategory = command.InternalCategory,
            MarketplaceCategoryId = command.MarketplaceCategoryId,
            MarketplaceCategoryPath = command.MarketplaceCategoryPath,
            LastVerifiedAt = now
        };

        session.Store(mapping);
        await session.SaveChangesAsync(ct);

        return Results.Created($"/api/category-mappings/{command.ChannelCode}/{command.InternalCategory}", mapping);
    }
}
