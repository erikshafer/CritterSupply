using Fulfillment.Shipments;

namespace Fulfillment.Api.IntegrationTests;

/// <summary>
/// Test stub for ICarrierLabelService that always throws to simulate carrier API failure.
/// Used for Slice 22 (label generation failure) integration test.
/// </summary>
public sealed class AlwaysFailingCarrierLabelService : ICarrierLabelService
{
    public Task<CarrierLabelResult> GenerateLabelAsync(
        string carrier,
        string service,
        decimal billableWeightLbs,
        CancellationToken ct)
    {
        throw new InvalidOperationException("Carrier API unavailable — simulated failure");
    }
}
