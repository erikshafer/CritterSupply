using JasperFx.Events;

namespace Shopping.Cart;

public sealed record Cart(
    Guid Id,
    Guid? CustomerId,
    string? SessionId,
    DateTimeOffset InitializedAt,
    Dictionary<string, CartLineItem> Items,
    bool IsAbandoned,
    bool IsCleared,
    bool CheckoutInitiated)
{
    public static Cart Create(IEvent<CartInitialized> @event) =>
        new(@event.StreamId,
            @event.Data.CustomerId,
            @event.Data.SessionId,
            @event.Data.InitializedAt,
            new Dictionary<string, CartLineItem>(),
            false,
            false,
            false);

    public Cart Apply(ItemAdded @event)
    {
        var updatedItems = new Dictionary<string, CartLineItem>(Items);

        if (updatedItems.ContainsKey(@event.Sku))
        {
            var existing = updatedItems[@event.Sku];
            updatedItems[@event.Sku] = existing with
            {
                Quantity = existing.Quantity + @event.Quantity
            };
        }
        else
        {
            updatedItems[@event.Sku] = new CartLineItem(
                @event.Sku,
                @event.Quantity,
                @event.UnitPrice);
        }

        return this with { Items = updatedItems };
    }

    public Cart Apply(ItemRemoved @event)
    {
        var updatedItems = new Dictionary<string, CartLineItem>(Items);
        updatedItems.Remove(@event.Sku);
        return this with { Items = updatedItems };
    }

    public Cart Apply(ItemQuantityChanged @event)
    {
        var updatedItems = new Dictionary<string, CartLineItem>(Items);

        if (updatedItems.ContainsKey(@event.Sku))
        {
            var existing = updatedItems[@event.Sku];
            updatedItems[@event.Sku] = existing with { Quantity = @event.NewQuantity };
        }

        return this with { Items = updatedItems };
    }

    public Cart Apply(CartCleared @event) =>
        this with
        {
            Items = new Dictionary<string, CartLineItem>(),
            IsCleared = true
        };

    public Cart Apply(CartAbandoned @event) =>
        this with { IsAbandoned = true };

    public Cart Apply(CheckoutInitiated @event) =>
        this with { CheckoutInitiated = true };

    public bool IsTerminal => IsAbandoned || IsCleared || CheckoutInitiated;
}
