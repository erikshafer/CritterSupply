using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart;

public static class RemoveItemFromCartHandler
{
    public static ProblemDetails Before(
        Guid cartId,
        string sku,
        Cart? cart)
    {
        if (cart is null)
            return new ProblemDetails { Detail = "Cart not found", Status = 404 };

        if (cart.IsTerminal)
            return new ProblemDetails
            {
                Detail = "Cannot modify a cart that has been abandoned, cleared, or checked out",
                Status = 400
            };

        if (!cart.Items.ContainsKey(sku))
            return new ProblemDetails
            {
                Detail = $"Item {sku} not found in cart",
                Status = 404
            };

        return WolverineContinue.NoProblems;
    }

    [WolverineDelete("/api/carts/{cartId}/items/{sku}")]
    public static (Events, OutgoingMessages) Handle(
        [FromRoute] Guid cartId,
        [FromRoute] string sku,
        [WriteAggregate] Cart cart)
    {
        var @event = new ItemRemoved(sku, DateTimeOffset.UtcNow);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Shopping.ItemRemoved(
            cart.Id,
            cart.CustomerId ?? Guid.Empty, // Anonymous carts use Guid.Empty
            sku,
            DateTimeOffset.UtcNow));

        return ([@event], outgoing);
    }
}
