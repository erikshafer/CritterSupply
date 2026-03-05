using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
using IntegrationMessages = Messages.Contracts.Shopping;

namespace Shopping.Cart;

public sealed record InitiateCheckout(Guid CartId)
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

        if (cart.Items.Count == 0)
            return new ProblemDetails
            {
                Detail = "Cannot checkout an empty cart",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/carts/{cartId}/checkout")]
    public static (CreationResponse<Guid>, Events, OutgoingMessages) Handle(
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

        var events = new Events();
        events.Add(cartEvent);

        // Publish integration message to Orders BC
        // Orders BC will create the Checkout aggregate via CheckoutInitiatedHandler
        var integrationLineItems = cart.Items.Values
            .Select(item => new IntegrationMessages.CheckoutLineItem(
                item.Sku,
                item.Quantity,
                item.UnitPrice))
            .ToList();

        var integrationMessage = new IntegrationMessages.CheckoutInitiated(
            checkoutId,
            cart.Id,
            cart.CustomerId,
            integrationLineItems,
            now);

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationMessage);

        // CRITICAL: CreationResponse<T> must be FIRST in tuple to be used as HTTP response
        var response = new CreationResponse<Guid>($"/api/checkouts/{checkoutId}", checkoutId);
        return (response, events, outgoing);
    }
}
