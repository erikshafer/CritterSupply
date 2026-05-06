namespace Marketplaces.Api.Listings;

/// <summary>
/// Internal Marketplaces BC scheduled message that drives the background sweep
/// of orphaned eBay draft offers (UNPUBLISHED offers left over when
/// <c>EbayMarketplaceAdapter.SubmitListingAsync</c>'s create-offer step succeeded
/// but the publish-offer step failed).
/// <para>
/// Not a cross-BC integration message — do not add to <c>Messages.Contracts</c>.
/// The sweep handler reschedules itself after each pass; the initial schedule is
/// kicked off by <c>OrphanedEbayDraftSweepStartupService</c> at host startup.
/// </para>
/// </summary>
/// <param name="BatchSize">Maximum number of orphans to attempt per pass.</param>
public sealed record SweepOrphanedEbayDrafts(int BatchSize = 25);
