using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart;

public sealed record ClearCart(
    Guid CartId,
    string? Reason)
{
    public class ClearCartValidator : AbstractValidator<ClearCart>
    {
        public ClearCartValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
            RuleFor(x => x.Reason).MaximumLength(200);
        }
    }
}

public static class ClearCartHandler
{
    // Command handler for internal use (tests, sagas, etc.)
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

    public static CartCleared Handle(
        ClearCart command,
        [WriteAggregate] Cart cart)
    {
        return new CartCleared(DateTimeOffset.UtcNow, command.Reason);
    }
}

// HTTP DELETE endpoint in separate class to avoid Wolverine trying to apply command handler's Before method
public static class ClearCartHttpEndpoint
{
    public static ProblemDetails Before(
        Guid cartId,
        [FromQuery] string? reason,
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

        // Validate reason length if provided
        if (reason?.Length > 200)
            return new ProblemDetails
            {
                Detail = "Reason cannot exceed 200 characters",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverineDelete("/api/carts/{cartId}")]
    public static Events Handle(
        Guid cartId,
        [FromQuery] string? reason,
        [WriteAggregate] Cart cart)
    {
        var events = new Events();
        events.Add(new CartCleared(DateTimeOffset.UtcNow, reason));
        return events;
    }
}
