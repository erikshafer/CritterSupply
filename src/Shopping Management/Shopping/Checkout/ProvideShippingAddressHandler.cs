using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Checkout;

public static class ProvideShippingAddressHandler
{
    public static ProblemDetails Before(
        ProvideShippingAddress command,
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

    [WolverinePost("/api/checkouts/{checkoutId}/shipping-address")]
    public static ShippingAddressProvided Handle(
        ProvideShippingAddress command,
        [WriteAggregate] Checkout checkout)
    {
        return new ShippingAddressProvided(
            command.AddressLine1,
            command.AddressLine2,
            command.City,
            command.StateOrProvince,
            command.PostalCode,
            command.Country,
            DateTimeOffset.UtcNow);
    }
}
