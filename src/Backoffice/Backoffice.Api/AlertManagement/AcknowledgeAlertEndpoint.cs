using Backoffice.AlertManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Security.Claims;
using Wolverine;
using Wolverine.Http;

namespace Backoffice.Api.Commands;

/// <summary>
/// HTTP endpoint for acknowledging operations alerts.
/// Extracts admin user ID from JWT claims and updates AlertFeedView projection.
/// </summary>
public static class AcknowledgeAlertEndpoint
{
    /// <summary>
    /// POST /api/backoffice/alerts/{alertId}/acknowledge
    /// Marks alert as acknowledged by current admin user.
    /// </summary>
    [WolverinePost("/api/backoffice/alerts/{alertId}/acknowledge")]
    [Authorize(Policy = "WarehouseClerk")]
    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Post(
        Guid alertId,
        ClaimsPrincipal user,
        IMessageBus bus,
        CancellationToken ct)
    {
        // Extract admin user ID from JWT claims
        var adminUserIdClaim = user.FindFirst("sub") ?? user.FindFirst(ClaimTypes.NameIdentifier);
        if (adminUserIdClaim is null || !Guid.TryParse(adminUserIdClaim.Value, out var adminUserId))
        {
            return TypedResults.Problem(
                "Unauthorized: Admin user ID not found in JWT claims",
                statusCode: 401);
        }

        try
        {
            var cmd = new AcknowledgeAlert(alertId, adminUserId);
            await bus.InvokeAsync(cmd, ct);
            return TypedResults.NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return TypedResults.NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already acknowledged"))
        {
            return TypedResults.Problem(ex.Message, statusCode: 409); // Conflict
        }
    }
}
