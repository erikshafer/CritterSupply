using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart;

public sealed record ChangeItemQuantity(
    Guid CartId,
    string Sku,
    int NewQuantity)
{
    public class ChangeItemQuantityValidator : AbstractValidator<ChangeItemQuantity>
    {
        public ChangeItemQuantityValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.NewQuantity).GreaterThan(0);
        }
    }
}

public static class ChangeItemQuantityHandler
{
    public static ProblemDetails Before(
        ChangeItemQuantity command,
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

    [WolverinePut("/api/carts/{cartId}/items/{sku}/quantity")]
    public static ItemQuantityChanged Handle(
        ChangeItemQuantity command,
        [WriteAggregate] Cart cart)
    {
        var oldQuantity = cart.Items[command.Sku].Quantity;

        return new ItemQuantityChanged(
            command.Sku,
            oldQuantity,
            command.NewQuantity,
            DateTimeOffset.UtcNow);
    }
}
