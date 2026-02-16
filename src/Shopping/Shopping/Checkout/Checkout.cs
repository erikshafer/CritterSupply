using JasperFx.Events;

namespace Shopping.Checkout;

public sealed record Checkout(
    Guid Id,
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<CheckoutLineItem> Items,
    DateTimeOffset StartedAt,
    Guid? ShippingAddressId,
    string? ShippingMethod,
    decimal? ShippingCost,
    string? PaymentMethodToken,
    CheckoutStatus Status)
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
            CheckoutStatus.InProgress);

    public Checkout Apply(ShippingAddressSelected @event) =>
        this with { ShippingAddressId = @event.AddressId };

    public Checkout Apply(ShippingMethodSelected @event) =>
        this with
        {
            ShippingMethod = @event.ShippingMethod,
            ShippingCost = @event.ShippingCost
        };

    public Checkout Apply(PaymentMethodProvided @event) =>
        this with { PaymentMethodToken = @event.PaymentMethodToken };

    public Checkout Apply(CheckoutCompleted @event) =>
        this with { Status = CheckoutStatus.Completed };

    public decimal Subtotal => Items.Sum(i => i.Quantity * i.UnitPrice);

    public decimal Total => Subtotal + (ShippingCost ?? 0m);
}
