namespace Pricing.Products;

/// <summary>
/// Domain event: Retroactive price correction.
/// Append-only audit record — does NOT backdate event stream.
/// DOES update CurrentPriceView projection and publishes PriceUpdated integration event.
/// Does NOT trigger marketing or customer-notification events (corrections are silent fixes).
/// </summary>
public sealed record PriceCorrected(
    Guid ProductPriceId,
    string Sku,
    Money CorrectedPrice,
    Money PreviousPrice,
    string CorrectionReason,
    Guid CorrectedBy,
    DateTimeOffset CorrectedAt);
