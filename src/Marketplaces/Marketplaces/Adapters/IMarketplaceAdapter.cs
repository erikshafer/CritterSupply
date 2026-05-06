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
/// <para>
/// <see cref="OrphanedExternalSubmissionId"/> is populated only when a multi-step
/// submission flow partially succeeded — i.e. an external resource was created on
/// the marketplace platform before a later step failed, leaving a stale draft that
/// must be cleaned up later. Today only eBay's create-offer / publish-offer flow
/// can produce this (orphaned UNPUBLISHED offer); see
/// <c>SweepOrphanedEbayDraftsHandler</c> for the cleanup mechanism.
/// </para>
/// </summary>
public sealed record SubmissionResult(
    bool IsSuccess,
    string? ExternalSubmissionId,
    string? ErrorMessage = null,
    string? OrphanedExternalSubmissionId = null);

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

    /// <summary>
    /// Deletes an orphaned draft resource on the marketplace platform that was
    /// created during a partial submission flow (e.g. eBay create-offer succeeded
    /// but the subsequent publish-offer failed, leaving an UNPUBLISHED offer).
    /// <para>
    /// Adapters whose submission flow cannot produce orphaned drafts (Amazon, Walmart,
    /// stubs) should treat this as a no-op and return <c>true</c>. Implementations that
    /// can produce orphans (eBay) should call the appropriate platform DELETE endpoint
    /// and treat "already gone" responses (HTTP 404) as success — the cleanup goal is
    /// idempotent removal.
    /// </para>
    /// </summary>
    /// <param name="externalSubmissionId">
    /// The orphan identifier originally captured in
    /// <see cref="SubmissionResult.OrphanedExternalSubmissionId"/>.
    /// </param>
    Task<bool> DeleteOrphanedDraftAsync(
        string externalSubmissionId,
        CancellationToken ct = default);
}
