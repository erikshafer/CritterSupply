using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Shopping.Checkout;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart;

public sealed record InitiateCheckout(
    Guid CartId)
{
    public class InitiateCheckoutValidator : AbstractValidator<InitiateCheckout>
    {
        public InitiateCheckoutValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
        }
    }
}

public static class InitiateCheckoutHandler
{
    public static ProblemDetails Before(
        InitiateCheckout command,
        Cart? cart)
    {
        if (cart is null)
            return new ProblemDetails { Detail = "Cart not found", Status = 404 };

        if (cart.IsTerminal)
            return new ProblemDetails
            {
                Detail = "Cannot initiate checkout for a cart that has been abandoned, cleared, or already checked out",
                Status = 400
            };

        if (!cart.Items.Any())
            return new ProblemDetails
            {
                Detail = "Cannot checkout an empty cart",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/carts/{cartId}/checkout")]
    public static (CheckoutInitiated, IStartStream, CreationResponse) Handle(
        InitiateCheckout command,
        [WriteAggregate] Cart cart)
    {
        var checkoutId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        // Terminal event for Cart stream
        var cartEvent = new CheckoutInitiated(
            cart.Id,
            checkoutId,
            cart.CustomerId,
            cart.Items.Values.ToList(),
            now);

        // Start new Checkout stream
        var checkoutEvent = new CheckoutStarted(
            checkoutId,
            cart.Id,
            cart.CustomerId,
            cart.Items.Values.ToList(),
            now);

        var startCheckout = MartenOps.StartStream<Checkout.Checkout>(checkoutId, checkoutEvent);

        return (cartEvent, startCheckout, new CreationResponse($"/api/checkouts/{checkoutId}"));
    }
}
