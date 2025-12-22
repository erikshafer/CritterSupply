using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart;

public static class ClearCartHandler
{
    public static ProblemDetails Before(
        ClearCart command,
        Cart? cart)
    {
        if (cart is null)
            return new ProblemDetails { Detail = "Cart not found", Status = 404 };

        if (cart.IsTerminal)
            return new ProblemDetails 
            { 
                Detail = "Cannot clear a cart that has been abandoned, cleared, or checked out", 
                Status = 400 
            };

        return WolverineContinue.NoProblems;
    }

    [WolverineDelete("/api/carts/{cartId}")]
    public static CartCleared Handle(
        ClearCart command,
        [WriteAggregate] Cart cart)
    {
        return new CartCleared(DateTimeOffset.UtcNow, command.Reason);
    }
}
