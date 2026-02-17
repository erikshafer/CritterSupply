using ShoppingContracts = Messages.Contracts.Shopping;

namespace Orders.Checkout;

public sealed record CheckoutStarted(
    Guid CheckoutId,
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<ShoppingContracts.CheckoutLineItem> Items,
    DateTimeOffset StartedAt);
