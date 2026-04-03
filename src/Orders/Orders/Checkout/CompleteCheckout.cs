using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine;
using Wolverine.Http;
using Messages.Contracts.CustomerIdentity;
using ShoppingContracts = Messages.Contracts.Shopping;

namespace Orders.Checkout;

/// <summary>
/// Direct Implementation pattern — compound handler [WriteAggregate] silently fails
/// to persist events when mixing route + body parameters (M32.3 discovery).
/// This handler also publishes a CartCheckoutCompleted integration message via OutgoingMessages.
/// </summary>
public static class CompleteCheckoutHandler
{
    [WolverinePost("/api/checkouts/{checkoutId}/complete")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        Guid checkoutId,
        IDocumentSession session,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<Checkout>(checkoutId, ct);
        var checkout = stream.Aggregate;

        if (checkout is null)
            return (Results.NotFound(new { detail = "Checkout not found" }), new OutgoingMessages());

        if (checkout.IsCompleted)
            return (Results.BadRequest(new { detail = "Checkout has already been completed" }), new OutgoingMessages());

        if (checkout.ShippingAddress is null)
            return (Results.BadRequest(new { detail = "Shipping address is required to complete checkout" }), new OutgoingMessages());

        if (string.IsNullOrWhiteSpace(checkout.ShippingMethod))
            return (Results.BadRequest(new { detail = "Shipping method is required to complete checkout" }), new OutgoingMessages());

        if (string.IsNullOrWhiteSpace(checkout.PaymentMethodToken))
            return (Results.BadRequest(new { detail = "Payment method is required to complete checkout" }), new OutgoingMessages());

        var orderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        // Terminal event for Checkout stream
        var checkoutEvent = new OrderCreated(orderId, now);
        stream.AppendOne(checkoutEvent);
        await session.SaveChangesAsync(ct);

        // Publish integration message to start Order saga
        var integrationMessage = new ShoppingContracts.CartCheckoutCompleted(
            orderId,
            checkout.Id,
            checkout.CustomerId,
            checkout.Items.Select(i => new ShoppingContracts.CheckoutLineItem(
                i.Sku,
                i.Quantity,
                i.UnitPrice)).ToList(),
            new AddressSnapshot(
                checkout.ShippingAddress!.AddressLine1,
                checkout.ShippingAddress.AddressLine2,
                checkout.ShippingAddress.City,
                checkout.ShippingAddress.StateOrProvince,
                checkout.ShippingAddress.PostalCode,
                checkout.ShippingAddress.Country),
            checkout.ShippingMethod!,
            checkout.ShippingCost!.Value,
            checkout.PaymentMethodToken!,
            now);

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationMessage);

        return (Results.Ok(new { orderId }), outgoing);
    }
}
