namespace Marketplaces.Adapters;

/// <summary>
/// Data required to submit a listing to a marketplace channel.
/// Includes a <see cref="ChannelExtensions"/> dictionary for channel-specific
/// attributes (spike finding #3: typed extension payload).
/// </summary>
public sealed record ListingSubmission(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string ProductName,
    string? Description,
    string? Category,
    decimal Price,
    IReadOnlyDictionary<string, string>? ChannelExtensions = null);

/// <summary>
/// Result of submitting a listing to a marketplace adapter.
/// <see cref="ExternalSubmissionId"/> carries the platform correlation ID
/// (spike finding #1: feedId, offerId, processing ID).
/// </summary>
public sealed record SubmissionResult(
    bool IsSuccess,
    string? ExternalSubmissionId,
    string? ErrorMessage = null);

/// <summary>
/// Status of a previously submitted listing on a marketplace platform.
/// Used by <see cref="IMarketplaceAdapter.CheckSubmissionStatusAsync"/> to poll
/// for async activation (spike finding #2).
/// </summary>
public sealed record SubmissionStatus(
    string ExternalSubmissionId,
    bool IsLive,
    bool IsFailed,
    string? FailureReason = null);

/// <summary>
/// Adapter interface for marketplace platform integrations.
/// Each marketplace channel (Amazon, Walmart, eBay) implements this interface.
/// Stub implementations return immediate success; real implementations call platform APIs.
///
/// Design reflects three findings from the marketplace API discovery spike:
/// 1. SubmitListingAsync returns ExternalSubmissionId (correlation ID)
/// 2. CheckSubmissionStatusAsync enables async status polling
/// 3. ListingSubmission includes ChannelExtensions for channel-specific attributes
/// </summary>
public interface IMarketplaceAdapter
{
    string ChannelCode { get; }

    Task<SubmissionResult> SubmitListingAsync(
        ListingSubmission submission,
        CancellationToken ct = default);

    Task<SubmissionStatus> CheckSubmissionStatusAsync(
        string externalSubmissionId,
        CancellationToken ct = default);

    Task<bool> DeactivateListingAsync(
        string externalListingId,
        CancellationToken ct = default);
}
