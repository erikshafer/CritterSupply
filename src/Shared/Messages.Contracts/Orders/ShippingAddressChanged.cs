namespace Messages.Contracts.Orders;

/// <summary>
/// Integration event published by Orders BC when a customer's shipping address is changed
/// after the order has been placed but before fulfillment has begun (inventory committed).
///
/// Consumers:
/// - Fulfillment BC: re-route the in-flight fulfillment request if it has not yet been picked.
/// - Customer Experience BC: surface the address-change confirmation to the customer.
///
/// Eligibility for this change is enforced by Orders BC via <c>OrderDecider.CanChangeShippingAddress</c>.
/// Post-handoff modifications (after <c>InventoryCommitted</c>) are out of scope until the Orders
/// remaster lands a coordinated re-pick / recall flow.
/// </summary>
public sealed record ShippingAddressChanged(
    Guid OrderId,
    Guid CustomerId,
    ShippingAddress NewShippingAddress,
    string Reason,
    DateTimeOffset ChangedAt);
