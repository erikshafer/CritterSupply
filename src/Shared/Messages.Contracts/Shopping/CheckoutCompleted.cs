namespace Messages.Contracts.Shopping;

public sealed record CheckoutCompleted(
    Guid OrderId,
    Guid CheckoutId,
    Guid? CustomerId,
    IReadOnlyList<CheckoutLineItem> Items,
    ShippingAddress ShippingAddress,
    string ShippingMethod,
    decimal ShippingCost,
    string PaymentMethodToken,
    DateTimeOffset CompletedAt);
