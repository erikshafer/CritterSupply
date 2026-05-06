namespace Marketplaces.Api.Listings;

/// <summary>
/// Marten document tracking an orphaned eBay draft offer left behind when
/// <c>EbayMarketplaceAdapter.SubmitListingAsync</c>'s create-offer step succeeded
/// but the publish-offer step failed. The offer exists on eBay's side in
/// UNPUBLISHED state and must be deleted via the background sweep
/// (<see cref="SweepOrphanedEbayDrafts"/> + <c>SweepOrphanedEbayDraftsHandler</c>)
/// to avoid stale draft accumulation.
/// <para>
/// Identity is the prefixed orphan id (e.g. <c>ebay-OFFER-ABC-123</c>) — the same
/// shape as <see cref="Adapters.SubmissionResult.OrphanedExternalSubmissionId"/>,
/// so persistence is naturally idempotent: re-detecting the same orphan upserts
/// the same document.
/// </para>
/// </summary>
public sealed class OrphanedEbayDraft
{
    /// <summary>
    /// Prefixed orphan id (e.g. <c>ebay-OFFER-ABC-123</c>). Doubles as the Marten
    /// document key for natural idempotency on re-detection.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The Listings BC listing id this orphan was created for.</summary>
    public Guid ListingId { get; set; }

    /// <summary>The SKU the orphaned offer was created for.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>The marketplace channel code (always <c>EBAY_US</c> today).</summary>
    public string ChannelCode { get; set; } = "EBAY_US";

    /// <summary>When the orphan was first detected (eBay publish-offer failure).</summary>
    public DateTimeOffset DetectedAt { get; set; }

    /// <summary>Number of cleanup attempts the sweep handler has made.</summary>
    public int CleanupAttempts { get; set; }

    /// <summary>When the most recent cleanup attempt ran (success or failure).</summary>
    public DateTimeOffset? LastCleanupAttemptAt { get; set; }

    /// <summary>
    /// Reason recorded for the last failed cleanup attempt (HTTP status, transport
    /// error, etc.). Cleared on success.
    /// </summary>
    public string? LastFailureReason { get; set; }

    /// <summary><c>true</c> once the eBay DELETE call succeeded (or returned 404).</summary>
    public bool IsCleaned { get; set; }

    /// <summary>When the orphan was successfully cleaned up.</summary>
    public DateTimeOffset? CleanedAt { get; set; }
}
