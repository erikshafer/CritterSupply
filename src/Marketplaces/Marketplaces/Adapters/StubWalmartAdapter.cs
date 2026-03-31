namespace Marketplaces.Adapters;

/// <summary>
/// Stub adapter for Walmart US marketplace. Returns immediate success with
/// a generated feed ID (wmrt-{guid}). Simulates a 100ms delay.
/// </summary>
public sealed class StubWalmartAdapter : IMarketplaceAdapter
{
    public string ChannelCode => "WALMART_US";

    public async Task<SubmissionResult> SubmitListingAsync(
        ListingSubmission submission,
        CancellationToken ct = default)
    {
        await Task.Delay(100, ct);
        return new SubmissionResult(
            IsSuccess: true,
            ExternalSubmissionId: $"wmrt-{Guid.NewGuid():N}");
    }

    public Task<SubmissionStatus> CheckSubmissionStatusAsync(
        string externalSubmissionId,
        CancellationToken ct = default)
    {
        return Task.FromResult(new SubmissionStatus(
            ExternalSubmissionId: externalSubmissionId,
            IsLive: true,
            IsFailed: false));
    }

    public async Task<bool> DeactivateListingAsync(
        string externalListingId,
        CancellationToken ct = default)
    {
        await Task.Delay(100, ct);
        return true;
    }
}
