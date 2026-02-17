using JasperFx.Events;
using ShoppingContracts = Messages.Contracts.Shopping;

namespace Orders.Checkout;

public sealed record Checkout(
    Guid Id,
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<ShoppingContracts.CheckoutLineItem> Items,
    DateTimeOffset StartedAt,
    ShippingAddress? ShippingAddress,
    string? ShippingMethod,
    decimal? ShippingCost,
    string? PaymentMethodToken,
    bool IsCompleted)
{
    public static Checkout Create(IEvent<CheckoutStarted> @event) =>
        new(@event.StreamId,
            @event.Data.CartId,
            @event.Data.CustomerId,
            @event.Data.Items,
            @event.Data.StartedAt,
            null,
            null,
            null,
            null,
            false);

    public Checkout Apply(ShippingAddressProvided @event) =>
        this with
        {
            ShippingAddress = new ShippingAddress(
                @event.AddressLine1,
                @event.AddressLine2,
                @event.City,
                @event.StateOrProvince,
                @event.PostalCode,
                @event.Country)
        };

    public Checkout Apply(ShippingMethodSelected @event) =>
        this with
        {
            ShippingMethod = @event.ShippingMethod,
            ShippingCost = @event.ShippingCost
        };

    public Checkout Apply(PaymentMethodProvided @event) =>
        this with { PaymentMethodToken = @event.PaymentMethodToken };

    public Checkout Apply(CheckoutCompleted @event) =>
        this with { IsCompleted = true };

    public decimal Subtotal => Items.Sum(i => i.Quantity * i.UnitPrice);

    public decimal Total => Subtotal + (ShippingCost ?? 0m);
}
