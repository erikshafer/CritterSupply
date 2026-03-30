namespace Marketplaces.Marketplaces;

/// <summary>
/// Represents an external marketplace channel (e.g., Amazon US, Walmart US, eBay US).
/// Stored as a Marten document with ChannelCode as the stable natural key (D4).
/// OWN_WEBSITE is not modelled here — it is the Listings BC's internal fast-path channel.
/// </summary>
public sealed class Marketplace
{
    /// <summary>Stable channel code — serves as the Marten document Id (D4).</summary>
    public string Id { get; init; } = default!;

    /// <summary>Human-readable display name shown in the admin UI.</summary>
    public string DisplayName { get; set; } = default!;

    /// <summary>Whether this marketplace is currently accepting listing submissions.</summary>
    public bool IsActive { get; set; }

    /// <summary>Always false for real external marketplaces (OWN_WEBSITE is handled separately).</summary>
    public bool IsOwnWebsite { get; init; } = false;

    /// <summary>Path in the Vault where API credentials are stored (e.g. "marketplace/amazon-us").</summary>
    public string? ApiCredentialVaultPath { get; set; }

    /// <summary>When this marketplace was first registered.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When this marketplace was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
