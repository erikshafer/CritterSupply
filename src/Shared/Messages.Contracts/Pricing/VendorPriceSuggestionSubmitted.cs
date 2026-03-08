namespace Messages.Contracts.Pricing;

/// <summary>
/// Integration message published by Vendor Portal BC when a vendor suggests a price change.
/// Consumed by Pricing BC for approval workflow (Phase 2+).
/// Note: Phase 1 does not implement vendor suggestions - this contract is defined early
/// to establish the integration boundary before Vendor Portal implementation begins.
/// </summary>
public sealed record VendorPriceSuggestionSubmitted(
    Guid SuggestionId,
    string Sku,
    Guid VendorTenantId,
    decimal SuggestedPrice,
    string Currency,
    string? VendorJustification,
    DateTimeOffset SubmittedAt);
