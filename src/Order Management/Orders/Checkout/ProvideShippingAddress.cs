using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace Orders.Checkout;

public sealed record ProvideShippingAddress(
    Guid CheckoutId,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country)
{
    public class ProvideShippingAddressValidator : AbstractValidator<ProvideShippingAddress>
    {
        public ProvideShippingAddressValidator()
        {
            RuleFor(x => x.CheckoutId).NotEmpty();
            RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(200);
            RuleFor(x => x.AddressLine2).MaximumLength(200);
            RuleFor(x => x.City).NotEmpty().MaximumLength(100);
            RuleFor(x => x.StateOrProvince).NotEmpty().MaximumLength(100);
            RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
            RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
        }
    }
}

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
