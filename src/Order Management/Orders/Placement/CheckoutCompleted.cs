namespace Orders.Placement;

/// <summary>
/// Integration event received from Shopping context when checkout completes.
/// This message starts the Order saga.
/// </summary>
public sealed record CheckoutCompleted(
    Guid CartId,
    Guid CustomerId,
    IReadOnlyList<CheckoutLineItem> LineItems,
    ShippingAddress ShippingAddress,
    string ShippingMethod,
    string PaymentMethodToken,
    IReadOnlyList<AppliedDiscount>? AppliedDiscounts,
    DateTimeOffset CompletedAt);
