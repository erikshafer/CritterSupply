using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopping.Clients;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart;

public sealed record AddItemToCart(
    Guid CartId,
    string Sku,
    int Quantity)
{
    public class AddItemToCartValidator : AbstractValidator<AddItemToCart>
    {
        public AddItemToCartValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Quantity).GreaterThan(0);
        }
    }
}

public static class AddItemToCartHandler
{
    // Command handler for internal use (tests, sagas, etc.)
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

    public static async Task<(Events, OutgoingMessages)> Handle(
        AddItemToCart command,
        IPricingClient pricingClient,
        [WriteAggregate] Cart cart,
        CancellationToken ct)
    {
        // Fetch server-authoritative price from Pricing BC
        var price = await pricingClient.GetPriceAsync(command.Sku, ct);

        // Note: For command handler use, price validation must be done by caller
        // HTTP endpoint has ValidateAsync that checks this
        var @event = new ItemAdded(
            command.Sku,
            command.Quantity,
            price!.BasePrice,
            DateTimeOffset.UtcNow);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Shopping.ItemAdded(
            cart.Id,
            cart.CustomerId ?? Guid.Empty,
            command.Sku,
            command.Quantity,
            price.BasePrice,
            DateTimeOffset.UtcNow));

        return ([@event], outgoing);
    }
}

// HTTP POST endpoint in separate class to enable ValidateAsync
public static class AddItemToCartHttpEndpoint
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

        return WolverineContinue.NoProblems;
    }

    public static async Task<ProblemDetails> ValidateAsync(
        AddItemToCart command,
        IPricingClient pricingClient,
        CancellationToken ct)
    {
        // Fetch server-authoritative price from Pricing BC
        var price = await pricingClient.GetPriceAsync(command.Sku, ct);

        if (price is null)
        {
            return new ProblemDetails
            {
                Detail = $"Price not available for SKU: {command.Sku}. Product may not be priced yet.",
                Status = 400
            };
        }

        if (price.Status != "Published")
        {
            return new ProblemDetails
            {
                Detail = $"Product {command.Sku} is not available for purchase (Status: {price.Status}).",
                Status = 400
            };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/carts/{cartId}/items")]
    [Authorize]
    public static async Task<(Events, OutgoingMessages)> Handle(
        AddItemToCart command,
        IPricingClient pricingClient,
        [WriteAggregate] Cart cart,
        CancellationToken ct)
    {
        // Fetch price again (validated in ValidateAsync, so guaranteed to exist and be published)
        var price = await pricingClient.GetPriceAsync(command.Sku, ct);

        var @event = new ItemAdded(
            command.Sku,
            command.Quantity,
            price!.BasePrice,
            DateTimeOffset.UtcNow);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Shopping.ItemAdded(
            cart.Id,
            cart.CustomerId ?? Guid.Empty,
            command.Sku,
            command.Quantity,
            price.BasePrice,
            DateTimeOffset.UtcNow));

        return ([@event], outgoing);
    }
}
