using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Checkout;

public static class SelectShippingMethodHandler
{
    public static ProblemDetails Before(
        SelectShippingMethod command,
        Checkout? checkout)
    {
        if (checkout is null)
            return new ProblemDetails { Detail = "Checkout not found", Status = 404 };

        if (checkout.IsCompleted)
            return new ProblemDetails 
            { 
                Detail = "Cannot modify a completed checkout", 
                Status = 400 
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/checkouts/{checkoutId}/shipping-method")]
    public static ShippingMethodSelected Handle(
        SelectShippingMethod command,
        [WriteAggregate] Checkout checkout)
    {
        return new ShippingMethodSelected(
            command.ShippingMethod,
            command.ShippingCost,
            DateTimeOffset.UtcNow);
    }
}
