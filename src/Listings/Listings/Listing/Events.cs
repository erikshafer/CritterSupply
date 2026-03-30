namespace Listings.Listing;

/// <summary>
/// Raised when a new listing draft is created for a product on a specific channel.
/// </summary>
public sealed record ListingDraftCreated(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string ProductName,
    string? InitialContent,
    DateTimeOffset OccurredAt);

/// <summary>
/// Raised when a listing is submitted for review.
/// </summary>
public sealed record ListingSubmittedForReview(
    Guid ListingId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Raised when a listing is approved during the review process.
/// </summary>
public sealed record ListingApproved(
    Guid ListingId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Raised when a listing transitions to Live on its channel.
/// </summary>
public sealed record ListingActivated(
    Guid ListingId,
    string ChannelCode,
    DateTimeOffset OccurredAt);

/// <summary>
/// Raised when a live listing is temporarily paused.
/// </summary>
public sealed record ListingPaused(
    Guid ListingId,
    string Reason,
    DateTimeOffset OccurredAt);

/// <summary>
/// Raised when a paused listing resumes (goes back to Live).
/// </summary>
public sealed record ListingResumed(
    Guid ListingId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Raised when a listing is ended (terminal state) through normal flow.
/// </summary>
public sealed record ListingEnded(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    EndedCause Cause,
    DateTimeOffset OccurredAt);

/// <summary>
/// Raised by the recall cascade handler to immediately force down a listing.
/// Distinct from ListingEnded because it bypasses the normal state machine.
/// </summary>
public sealed record ListingForcedDown(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string RecallReason,
    DateTimeOffset OccurredAt);

/// <summary>
/// Raised when product content changes propagate to a listing.
/// </summary>
public sealed record ListingContentUpdated(
    Guid ListingId,
    string? ProductName,
    string? Description,
    DateTimeOffset OccurredAt);

/// <summary>
/// Discriminated causes for why a listing ended.
/// </summary>
public enum EndedCause
{
    ManualEnd,
    ProductDiscontinued,
    SubmissionRejected,
    ProductDeleted
}
