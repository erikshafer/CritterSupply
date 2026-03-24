using System.Security.Claims;
using Backoffice.Clients;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace Backoffice.Api.Commands;

/// <summary>
/// Command to cancel an order (CS workflow: order cancellation)
/// Extracts adminUserId from JWT claims for audit trail
/// </summary>
public sealed record CancelOrderCommand(
    Guid OrderId,
    string Reason,
    Guid AdminUserId);

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.AdminUserId).NotEmpty();
    }
}

public static class CancelOrderCommandHandler
{
    [WolverinePost("/api/backoffice/orders/{orderId}/cancel")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<IResult> Handle(
        Guid orderId,
        CancelOrderRequest request,
        IOrdersClient ordersClient,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        // Extract admin user ID from JWT claims
        var adminUserIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(adminUserIdClaim) || !Guid.TryParse(adminUserIdClaim, out var adminUserId))
        {
            return Results.Problem(
                title: "Unauthorized",
                detail: "Admin user ID not found in JWT claims",
                statusCode: 401);
        }

        try
        {
            // Delegate to Orders BC
            await ordersClient.CancelOrderAsync(orderId, ct);

            return Results.NoContent(); // 204 - Order cancelled successfully
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Order not found" });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                title: "Failed to cancel order",
                detail: ex.Message,
                statusCode: (int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }
    }
}

/// <summary>
/// Request body for order cancellation
/// </summary>
public sealed record CancelOrderRequest(
    string Reason);
