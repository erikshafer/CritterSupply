namespace Messages.Contracts.Marketplaces;

/// <summary>
/// Published when a new marketplace is registered.
/// </summary>
public sealed record MarketplaceRegistered(
    string ChannelCode,
    string DisplayName,
    DateTimeOffset OccurredAt);

/// <summary>
/// Published when a marketplace is deactivated.
/// </summary>
public sealed record MarketplaceDeactivated(
    string ChannelCode,
    DateTimeOffset OccurredAt);

/// <summary>
/// Published when a listing is successfully submitted and activated on a marketplace.
/// Listings BC consumes this (in M37.x) to transition the listing to Live.
/// </summary>
public sealed record MarketplaceListingActivated(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string ExternalListingId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Published when a listing submission to a marketplace is rejected.
/// Reasons include missing category mapping, inactive marketplace, or adapter failure.
/// </summary>
public sealed record MarketplaceSubmissionRejected(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string Reason,
    DateTimeOffset OccurredAt);
