using JasperFx.Events;

namespace Shopping.Cart;

public sealed record Cart(
    Guid Id,
    Guid? CustomerId,
    string? SessionId,
    DateTimeOffset InitializedAt,
    Dictionary<string, CartLineItem> Items,
    CartStatus Status)
{
    public bool IsTerminal => Status != CartStatus.Active;

    public static Cart Create(IEvent<CartInitialized> @event) =>
        new(@event.StreamId,
            @event.Data.CustomerId,
            @event.Data.SessionId,
            @event.Data.InitializedAt,
            new Dictionary<string, CartLineItem>(),
            CartStatus.Active);

    public Cart Apply(ItemAdded @event)
    {
        var updatedItems = new Dictionary<string, CartLineItem>(Items);

        if (updatedItems.TryGetValue(@event.Sku, out var existingItem))
        {
            updatedItems[@event.Sku] = existingItem with
            {
                Quantity = existingItem.Quantity + @event.Quantity
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

        if (updatedItems.TryGetValue(@event.Sku, out var existingItem))
            updatedItems[@event.Sku] = existingItem with { Quantity = @event.NewQuantity };

        return this with { Items = updatedItems };
    }

    public Cart Apply(CartCleared @event) =>
        this with
        {
            Items = new Dictionary<string, CartLineItem>(),
            Status = CartStatus.Cleared
        };

    public Cart Apply(CartAbandoned @event) =>
        this with { Status = CartStatus.Abandoned };

    public Cart Apply(CheckoutInitiated @event) =>
        this with { Status = CartStatus.CheckedOut };
}
