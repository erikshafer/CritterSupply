namespace Messages.Contracts.Listings;

/// <summary>
/// Integration message published when a new listing is created.
/// </summary>
public sealed record ListingCreated(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    DateTimeOffset OccurredAt);

/// <summary>
/// Integration message published when a listing is approved (transitions to Submitted).
/// Carries product content so that downstream consumers (Marketplaces BC) can build
/// marketplace submissions without an HTTP callback. This is a deliberate Session 7
/// tradeoff — a ProductSummaryView ACL in Marketplaces BC is the long-term solution (M37.0).
/// </summary>
public sealed record ListingApproved(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string ProductName,
    string? Category,
    decimal? Price,
    DateTimeOffset OccurredAt);

/// <summary>
/// Integration message published when a listing transitions to Live.
/// </summary>
public sealed record ListingActivated(
    Guid ListingId,
    string ChannelCode,
    DateTimeOffset OccurredAt);

/// <summary>
/// Integration message published when a listing is ended through normal flow.
/// </summary>
public sealed record ListingEnded(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string Cause,
    DateTimeOffset OccurredAt);

/// <summary>
/// Integration message published when a listing is force-downed by a recall cascade.
/// </summary>
public sealed record ListingForcedDown(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string RecallReason,
    DateTimeOffset OccurredAt);

/// <summary>
/// Integration message published when a recall cascade completes for a SKU.
/// </summary>
public sealed record ListingsCascadeCompleted(
    string Sku,
    int AffectedCount,
    DateTimeOffset OccurredAt);
