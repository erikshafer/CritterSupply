using Marten;
using Orders.Checkout;
using Wolverine.Http;
using ShoppingContracts = Messages.Contracts.Shopping;

namespace Orders.Api.Checkout;

/// <summary>
/// Wolverine HTTP endpoint for querying checkouts.
/// </summary>
public static class GetCheckoutEndpoint
{
    /// <summary>
    /// Retrieves a checkout by its identifier.
    /// </summary>
    /// <param name="checkoutId">The checkout identifier.</param>
    /// <param name="session">The Marten query session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 with CheckoutResponse if found, 404 if not found.</returns>
    [WolverineGet("/api/checkouts/{checkoutId}")]
    public static async Task<IResult> Get(
        Guid checkoutId,
        IQuerySession session,
        CancellationToken cancellationToken)
    {
        var checkout = await session.Events.AggregateStreamAsync<Orders.Checkout.Checkout>(
            checkoutId,
            token: cancellationToken);

        return checkout is null
            ? Results.NotFound()
            : Results.Ok(CheckoutResponse.From(checkout));
    }
}

/// <summary>
/// Response DTO for checkout queries.
/// </summary>
public sealed record CheckoutResponse(
    Guid CheckoutId,
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<CheckoutLineItemResponse> Items,
    DateTimeOffset StartedAt,
    ShippingAddressResponse? ShippingAddress,
    string? ShippingMethod,
    decimal? ShippingCost,
    bool HasPaymentMethod,
    bool IsCompleted,
    decimal Subtotal,
    decimal Total)
{
    public static CheckoutResponse From(Orders.Checkout.Checkout checkout) =>
        new(
            checkout.Id,
            checkout.CartId,
            checkout.CustomerId,
            checkout.Items
                .Select(item => new CheckoutLineItemResponse(
                    item.Sku,
                    item.Quantity,
                    item.UnitPrice,
                    item.Quantity * item.UnitPrice))
                .ToList(),
            checkout.StartedAt,
            checkout.ShippingAddress is not null
                ? new ShippingAddressResponse(
                    checkout.ShippingAddress.AddressLine1,
                    checkout.ShippingAddress.AddressLine2,
                    checkout.ShippingAddress.City,
                    checkout.ShippingAddress.StateOrProvince,
                    checkout.ShippingAddress.PostalCode,
                    checkout.ShippingAddress.Country)
                : null,
            checkout.ShippingMethod,
            checkout.ShippingCost,
            checkout.PaymentMethodToken is not null,
            checkout.IsCompleted,
            checkout.Subtotal,
            checkout.Total);
}

/// <summary>
/// Response DTO for checkout line items.
/// </summary>
public sealed record CheckoutLineItemResponse(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

/// <summary>
/// Response DTO for shipping address.
/// </summary>
public sealed record ShippingAddressResponse(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country);
