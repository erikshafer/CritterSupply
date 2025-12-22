using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Checkout;

public static class CompleteCheckoutHandler
{
    public static ProblemDetails Before(
        CompleteCheckout command,
        Checkout? checkout)
    {
        if (checkout is null)
            return new ProblemDetails { Detail = "Checkout not found", Status = 404 };

        if (checkout.IsCompleted)
            return new ProblemDetails 
            { 
                Detail = "Checkout has already been completed", 
                Status = 400 
            };

        if (checkout.ShippingAddress is null)
            return new ProblemDetails 
            { 
                Detail = "Shipping address is required to complete checkout", 
                Status = 400 
            };

        if (string.IsNullOrWhiteSpace(checkout.ShippingMethod))
            return new ProblemDetails 
            { 
                Detail = "Shipping method is required to complete checkout", 
                Status = 400 
            };

        if (string.IsNullOrWhiteSpace(checkout.PaymentMethodToken))
            return new ProblemDetails 
            { 
                Detail = "Payment method is required to complete checkout", 
                Status = 400 
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/checkouts/{checkoutId}/complete")]
    public static (CheckoutCompleted, OutgoingMessages) Handle(
        CompleteCheckout command,
        [WriteAggregate] Checkout checkout)
    {
        var orderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        // Terminal event for Checkout stream
        var checkoutEvent = new CheckoutCompleted(orderId, now);

        // Prepare integration message to Orders
        var messages = new OutgoingMessages();
        messages.Add(new Messages.Contracts.Shopping.CheckoutCompleted(
            orderId,
            checkout.Id,
            checkout.CustomerId,
            checkout.Items.Select(i => new Messages.Contracts.Shopping.CheckoutLineItem(
                i.Sku,
                i.Quantity,
                i.UnitPrice)).ToList(),
            new Messages.Contracts.Shopping.ShippingAddress(
                checkout.ShippingAddress!.AddressLine1,
                checkout.ShippingAddress.AddressLine2,
                checkout.ShippingAddress.City,
                checkout.ShippingAddress.StateOrProvince,
                checkout.ShippingAddress.PostalCode,
                checkout.ShippingAddress.Country),
            checkout.ShippingMethod!,
            checkout.ShippingCost!.Value,
            checkout.PaymentMethodToken!,
            now));

        return (checkoutEvent, messages);
    }
}
