using FluentValidation;
using Storefront.Clients;
using Wolverine.Http;

namespace Storefront.Api.Commands;

/// <summary>
/// BFF command to initialize a cart for a customer
/// Delegates to Shopping BC
/// </summary>
public sealed record InitializeCart(Guid CustomerId);

public sealed class InitializeCartValidator : AbstractValidator<InitializeCart>
{
    public InitializeCartValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
    }
}

public static class InitializeCartHandler
{
    [WolverinePost("/api/storefront/carts/initialize")]
    public static async Task<IResult> Handle(
        InitializeCartRequest request,
        IShoppingClient shoppingClient,
        CancellationToken ct)
    {
        try
        {
            var cartId = await shoppingClient.InitializeCartAsync(request.CustomerId, ct);
            return Results.Ok(cartId); // Return the new cart ID
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                title: "Failed to initialize cart",
                detail: ex.Message,
                statusCode: (int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }
    }
}

/// <summary>
/// Request body for initializing cart
/// </summary>
public sealed record InitializeCartRequest(Guid CustomerId);
