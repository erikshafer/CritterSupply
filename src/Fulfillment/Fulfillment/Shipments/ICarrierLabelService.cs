namespace Fulfillment.Shipments;

/// <summary>
/// Abstraction for carrier label generation.
/// Extracted to allow test stubs that simulate carrier API failures (Slice 22).
/// </summary>
public interface ICarrierLabelService
{
    /// <summary>
    /// Generates a shipping label and returns the tracking number.
    /// Throws on carrier API failure.
    /// </summary>
    Task<CarrierLabelResult> GenerateLabelAsync(
        string carrier,
        string service,
        decimal billableWeightLbs,
        CancellationToken ct);
}

/// <summary>
/// Result of a successful carrier label generation.
/// </summary>
public sealed record CarrierLabelResult(
    string TrackingNumber,
    string? LabelZpl);

/// <summary>
/// Default implementation that generates stub tracking numbers.
/// In production, this would call the carrier's API.
/// </summary>
public sealed class StubCarrierLabelService : ICarrierLabelService
{
    public Task<CarrierLabelResult> GenerateLabelAsync(
        string carrier,
        string service,
        decimal billableWeightLbs,
        CancellationToken ct)
    {
        var trackingNumber = $"1Z{carrier.ToUpperInvariant()[..3]}{Guid.NewGuid():N}"[..24];
        return Task.FromResult(new CarrierLabelResult(trackingNumber, null));
    }
}
