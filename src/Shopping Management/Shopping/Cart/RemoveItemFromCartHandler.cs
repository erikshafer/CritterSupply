using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart;

public static class RemoveItemFromCartHandler
{
    public static ProblemDetails Before(
        RemoveItemFromCart command,
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

        if (!cart.Items.ContainsKey(command.Sku))
            return new ProblemDetails 
            { 
                Detail = $"Item {command.Sku} not found in cart", 
                Status = 404 
            };

        return WolverineContinue.NoProblems;
    }

    [WolverineDelete("/api/carts/{cartId}/items/{sku}")]
    public static ItemRemoved Handle(
        RemoveItemFromCart command,
        [WriteAggregate] Cart cart)
    {
        return new ItemRemoved(command.Sku, DateTimeOffset.UtcNow);
    }
}
