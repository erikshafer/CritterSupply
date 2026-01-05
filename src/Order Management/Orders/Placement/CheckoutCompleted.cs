namespace Orders.Placement;

/// <summary>
/// Integration event received from Shopping context when checkout completes.
/// This message starts the Order saga.
/// Maps from Messages.Contracts.Shopping.CheckoutCompleted.
/// </summary>
public sealed record CheckoutCompleted(
    Guid OrderId,
    Guid CheckoutId,
    Guid? CustomerId,
    IReadOnlyList<CheckoutLineItem> LineItems,
    ShippingAddress ShippingAddress,
    string ShippingMethod,
    decimal ShippingCost,
    string PaymentMethodToken,
    DateTimeOffset CompletedAt);
