namespace Orders.Placement;

/// <summary>
/// Command to place a new order.
/// Created by mapping from Shopping BC's CartCheckoutCompleted integration message in PlaceOrderHandler.
/// This is a local domain command used to pass validated data into OrderDecider.Start().
/// Note: This command is not dispatched through the Wolverine message bus, so FluentValidation
/// middleware does not apply. Validation is the responsibility of PlaceOrderHandler mapping logic
/// and the upstream Shopping BC's CartCheckoutCompleted message contract.
/// </summary>
public sealed record PlaceOrder(
    Guid OrderId,
    Guid CheckoutId,
    Guid? CustomerId,
    IReadOnlyList<CheckoutLineItem> LineItems,
    ShippingAddress ShippingAddress,
    string ShippingMethod,
    decimal ShippingCost,
    string PaymentMethodToken,
    DateTimeOffset CompletedAt);
