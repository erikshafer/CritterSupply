using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart;

public sealed record RemoveCouponFromCart(Guid CartId)
{
    public class RemoveCouponFromCartValidator : AbstractValidator<RemoveCouponFromCart>
    {
        public RemoveCouponFromCartValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
        }
    }
}

public static class RemoveCouponFromCartHandler
{
    // Command handler for internal use (tests, sagas, etc.)
    public static ProblemDetails Before(
        RemoveCouponFromCart command,
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

        if (cart.AppliedCouponCode is null)
            return new ProblemDetails
            {
                Detail = "No coupon applied to cart",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static (CouponRemoved, OutgoingMessages) Handle(
        RemoveCouponFromCart command,
        [WriteAggregate] Cart cart)
    {
        var @event = new CouponRemoved(
            cart.Id,
            cart.AppliedCouponCode!, // Validated in Before()
            DateTimeOffset.UtcNow);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Shopping.CouponRemoved(
            cart.Id,
            cart.CustomerId ?? Guid.Empty,
            cart.AppliedCouponCode!,
            DateTimeOffset.UtcNow));

        return (@event, outgoing);
    }
}

// HTTP DELETE endpoint in separate class to avoid Wolverine trying to apply command handler's Before method
public static class RemoveCouponFromCartHttpEndpoint
{
    public static ProblemDetails Before(
        Guid cartId,
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

        if (cart.AppliedCouponCode is null)
            return new ProblemDetails
            {
                Detail = "No coupon applied to cart",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverineDelete("/api/carts/{cartId}/apply-coupon")]
    public static (Events, OutgoingMessages) Handle(
        Guid cartId,
        [WriteAggregate] Cart cart)
    {
        var @event = new CouponRemoved(
            cart.Id,
            cart.AppliedCouponCode!, // Validated in Before()
            DateTimeOffset.UtcNow);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Shopping.CouponRemoved(
            cart.Id,
            cart.CustomerId ?? Guid.Empty,
            cart.AppliedCouponCode!,
            DateTimeOffset.UtcNow));

        return ([@event], outgoing);
    }
}
