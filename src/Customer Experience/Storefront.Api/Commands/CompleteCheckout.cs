using FluentValidation;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Wolverine.Http;

namespace Storefront.Api.Commands;

/// <summary>
/// BFF command to complete checkout
/// Delegates to Orders BC
/// </summary>
public sealed record CompleteCheckout(Guid CheckoutId);

public sealed class CompleteCheckoutValidator : AbstractValidator<CompleteCheckout>
{
    public CompleteCheckoutValidator()
    {
        RuleFor(x => x.CheckoutId).NotEmpty();
    }
}

public static class CompleteCheckoutHandler
{
    [WolverinePost("/api/storefront/checkouts/{checkoutId}/complete")]
    public static async Task<IResult> Handle(
        Guid checkoutId,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("OrdersClient");

        var response = await client.PostAsync(
            $"/api/checkouts/{checkoutId}/complete",
            null, // No request body - checkout prerequisites already set
            ct);

        if (response.IsSuccessStatusCode)
        {
            // Parse response to get order ID
            var content = await response.Content.ReadAsStringAsync(ct);

            try
            {
                var jsonDoc = JsonDocument.Parse(content);
                if (jsonDoc.RootElement.TryGetProperty("orderId", out var orderIdElement))
                {
                    var orderId = orderIdElement.GetGuid();
                    return Results.Ok(new { orderId });
                }
            }
            catch
            {
                // If response doesn't contain orderId, just return success
            }

            return Results.NoContent(); // 204 - Checkout completed successfully
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Checkout not found" });
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // Parse validation error from Orders BC
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            return Results.BadRequest(new { message = errorContent });
        }

        return Results.Problem(
            title: "Failed to complete checkout",
            statusCode: (int)response.StatusCode);
    }
}
