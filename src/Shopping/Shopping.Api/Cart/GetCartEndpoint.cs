using Marten;
using Shopping.Cart;
using Wolverine.Http;

namespace Shopping.Api.Cart;

/// <summary>
/// Wolverine HTTP endpoint for querying carts.
/// </summary>
public static class GetCartEndpoint
{
    /// <summary>
    /// Retrieves a cart by its identifier.
    /// </summary>
    /// <param name="cartId">The cart identifier.</param>
    /// <param name="session">The Marten query session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 with CartResponse if found, 404 if not found.</returns>
    [WolverineGet("/api/carts/{cartId}")]
    public static async Task<IResult> Get(
        Guid cartId,
        IQuerySession session,
        CancellationToken cancellationToken)
    {
        var cart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(
            cartId,
            token: cancellationToken);

        return cart is null
            ? Results.NotFound()
            : Results.Ok(CartResponse.From(cart));
    }
}

/// <summary>
/// Response DTO for cart queries.
/// </summary>
public sealed record CartResponse(
    Guid CartId,
    Guid? CustomerId,
    string? SessionId,
    DateTimeOffset InitializedAt,
    IReadOnlyList<CartLineItemResponse> Items,
    string Status,
    decimal TotalAmount)
{
    public static CartResponse From(Shopping.Cart.Cart cart) =>
        new(
            cart.Id,
            cart.CustomerId,
            cart.SessionId,
            cart.InitializedAt,
            cart.Items.Values
                .Select(item => new CartLineItemResponse(
                    item.Sku,
                    item.Quantity,
                    item.UnitPrice,
                    item.Quantity * item.UnitPrice))
                .ToList(),
            cart.Status.ToString(),
            cart.Items.Values.Sum(item => item.Quantity * item.UnitPrice));
}

/// <summary>
/// Response DTO for cart line items.
/// </summary>
public sealed record CartLineItemResponse(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);
