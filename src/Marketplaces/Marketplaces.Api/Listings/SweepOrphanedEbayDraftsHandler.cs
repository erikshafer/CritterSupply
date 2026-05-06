using Marten;
using Marketplaces.Adapters;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Marketplaces.Api.Listings;

/// <summary>
/// Background sweep that finishes the orphaned eBay draft lifecycle.
/// <para>
/// Loads up to <c>BatchSize</c> uncleaned <see cref="OrphanedEbayDraft"/> documents,
/// invokes <see cref="IMarketplaceAdapter.DeleteOrphanedDraftAsync"/> on the EBAY_US
/// adapter for each, and updates the document with success / failure outcome. After
/// the pass completes, the handler reschedules itself for the next sweep window
/// (mirroring the <c>CheckWalmartFeedStatusHandler</c> reschedule pattern).
/// </para>
/// <para>
/// Detection ships with M38.1 (UNPUBLISHED offer flagged in
/// <see cref="EbayMarketplaceAdapter.CheckSubmissionStatusAsync"/> + the
/// <c>OrphanedExternalSubmissionId</c> populated by
/// <see cref="EbayMarketplaceAdapter.SubmitListingAsync"/> on publish failure);
/// this handler closes the lifecycle (Tier 1 C in
/// <c>docs/research/state-of-repo-2026-05.md</c>).
/// </para>
/// </summary>
public static class SweepOrphanedEbayDraftsHandler
{
    /// <summary>How long to wait between sweep passes.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    /// <summary>Channel code used to look up the eBay adapter.</summary>
    public const string EbayChannelCode = "EBAY_US";

    public static async Task Handle(
        SweepOrphanedEbayDrafts message,
        IDocumentSession session,
        IReadOnlyDictionary<string, IMarketplaceAdapter> adapters,
        IMessageBus bus,
        ILogger<SweepOrphanedEbayDrafts> logger,
        CancellationToken ct)
    {
        var batchSize = message.BatchSize > 0 ? message.BatchSize : 25;

        if (!adapters.TryGetValue(EbayChannelCode, out var adapter))
        {
            logger.LogWarning(
                "SweepOrphanedEbayDrafts skipped — no adapter registered for channel '{ChannelCode}'",
                EbayChannelCode);
            await bus.ScheduleAsync(message, SweepInterval);
            return;
        }

        // Load oldest uncleaned orphans first so retries don't starve newer ones.
        var orphans = await session.Query<OrphanedEbayDraft>()
            .Where(o => !o.IsCleaned)
            .OrderBy(o => o.DetectedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (orphans.Count == 0)
        {
            logger.LogDebug("SweepOrphanedEbayDrafts found no pending orphans to clean up");
            await bus.ScheduleAsync(message, SweepInterval);
            return;
        }

        logger.LogInformation(
            "SweepOrphanedEbayDrafts processing {Count} pending orphan(s) (batch size {BatchSize})",
            orphans.Count, batchSize);

        var now = DateTimeOffset.UtcNow;
        var cleaned = 0;
        var failed = 0;

        foreach (var orphan in orphans)
        {
            orphan.CleanupAttempts++;
            orphan.LastCleanupAttemptAt = now;

            bool deleted;
            try
            {
                deleted = await adapter.DeleteOrphanedDraftAsync(orphan.Id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "DeleteOrphanedDraftAsync threw for orphan {OrphanId} on attempt {Attempt}",
                    orphan.Id, orphan.CleanupAttempts);
                orphan.LastFailureReason = $"Adapter exception: {ex.Message}";
                session.Store(orphan);
                failed++;
                continue;
            }

            if (deleted)
            {
                orphan.IsCleaned = true;
                orphan.CleanedAt = now;
                orphan.LastFailureReason = null;
                cleaned++;
                logger.LogInformation(
                    "Orphaned eBay draft cleaned up: OrphanId={OrphanId}, ListingId={ListingId}, Sku={Sku}, Attempts={Attempts}",
                    orphan.Id, orphan.ListingId, orphan.Sku, orphan.CleanupAttempts);
            }
            else
            {
                orphan.LastFailureReason = $"Adapter returned false on attempt {orphan.CleanupAttempts}";
                failed++;
                logger.LogWarning(
                    "Orphaned eBay draft cleanup failed: OrphanId={OrphanId}, Attempts={Attempts} — will retry next sweep",
                    orphan.Id, orphan.CleanupAttempts);
            }

            session.Store(orphan);
        }

        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "SweepOrphanedEbayDrafts pass complete — cleaned {Cleaned}, failed {Failed}",
            cleaned, failed);

        await bus.ScheduleAsync(message, SweepInterval);
    }
}
