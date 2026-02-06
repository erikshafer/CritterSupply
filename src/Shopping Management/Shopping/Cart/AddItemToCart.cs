using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart;

public sealed record AddItemToCart(
    Guid CartId,
    string Sku,
    int Quantity,
    decimal UnitPrice)
{
    public class AddItemToCartValidator : AbstractValidator<AddItemToCart>
    {
        public AddItemToCartValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        }
    }
}

public static class AddItemToCartHandler
{
    public static ProblemDetails Before(
        AddItemToCart command,
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

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/carts/{cartId}/items")]
    public static (ItemAdded, OutgoingMessages) Handle(
        AddItemToCart command,
        [WriteAggregate] Cart cart)
    {
        var @event = new ItemAdded(
            command.Sku,
            command.Quantity,
            command.UnitPrice,
            DateTimeOffset.UtcNow);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Shopping.ItemAdded(
            cart.Id,
            cart.CustomerId ?? Guid.Empty, // Anonymous carts use Guid.Empty
            command.Sku,
            command.Quantity,
            command.UnitPrice,
            DateTimeOffset.UtcNow));

        return (@event, outgoing);
    }
}
