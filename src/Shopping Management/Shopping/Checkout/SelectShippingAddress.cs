using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Checkout;

public sealed record SelectShippingAddress(
    Guid CheckoutId,
    Guid AddressId)
{
    public class SelectShippingAddressValidator : AbstractValidator<SelectShippingAddress>
    {
        public SelectShippingAddressValidator()
        {
            RuleFor(x => x.CheckoutId).NotEmpty();
            RuleFor(x => x.AddressId).NotEmpty();
        }
    }
}

public static class SelectShippingAddressHandler
{
    public static ProblemDetails Before(
        SelectShippingAddress command,
        Checkout? checkout)
    {
        if (checkout is null)
            return new ProblemDetails { Detail = "Checkout not found", Status = 404 };

        if (checkout.Status != CheckoutStatus.InProgress)
            return new ProblemDetails
            {
                Detail = $"Cannot select address for checkout in {checkout.Status} status",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePut("/api/checkouts/{checkoutId}/shipping-address")]
    public static ShippingAddressSelected Handle(
        SelectShippingAddress command,
        [WriteAggregate] Checkout checkout)
    {
        return new ShippingAddressSelected(
            command.CheckoutId,
            command.AddressId,
            DateTimeOffset.UtcNow);
    }
}
