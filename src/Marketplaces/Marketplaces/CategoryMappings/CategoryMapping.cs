namespace Marketplaces.CategoryMappings;

/// <summary>
/// Maps an internal product category to a marketplace-specific category identifier.
/// Composite key: "{ChannelCode}:{InternalCategory}" — one mapping per channel+category pair.
/// Stored as a Marten document (D5: category mappings owned by Marketplaces BC).
/// </summary>
public sealed class CategoryMapping
{
    /// <summary>Composite key in "{ChannelCode}:{InternalCategory}" format.</summary>
    public string Id { get; init; } = default!;

    /// <summary>The marketplace channel code (e.g., "AMAZON_US").</summary>
    public string ChannelCode { get; init; } = default!;

    /// <summary>The internal product category name (e.g., "Dogs", "Cats").</summary>
    public string InternalCategory { get; init; } = default!;

    /// <summary>The marketplace-specific category identifier (e.g., "AMZN-PET-DOGS-001").</summary>
    public string MarketplaceCategoryId { get; set; } = default!;

    /// <summary>Optional human-readable category path on the marketplace.</summary>
    public string? MarketplaceCategoryPath { get; set; }

    /// <summary>When this mapping was last verified against the marketplace's taxonomy.</summary>
    public DateTimeOffset LastVerifiedAt { get; set; }
}
