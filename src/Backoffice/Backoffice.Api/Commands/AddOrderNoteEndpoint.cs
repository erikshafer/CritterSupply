using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Backoffice.Api.Commands;

/// <summary>
/// HTTP endpoint handler for AddOrderNote command.
/// Creates new OrderNote event stream.
/// </summary>
public static class AddOrderNoteEndpoint
{
    /// <summary>
    /// Validate that the order exists before creating note.
    /// </summary>
    public static async Task<ProblemDetails?> BeforeAsync(
        Backoffice.OrderNote.AddOrderNote command,
        Backoffice.Clients.IOrdersClient ordersClient,
        CancellationToken ct)
    {
        // Verify order exists in Orders BC
        var order = await ordersClient.GetOrderAsync(command.OrderId, ct);
        if (order is null)
        {
            return new ProblemDetails
            {
                Detail = $"Order {command.OrderId} not found",
                Status = StatusCodes.Status404NotFound
            };
        }

        // Domain-level validation
        var validationResult = Backoffice.OrderNote.AddOrderNoteValidation.Validate(command);
        if (validationResult is not null)
        {
            return validationResult;
        }

        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Create OrderNote event stream.
    /// Wolverine automatically calls SaveChangesAsync() after handler completes.
    /// </summary>
    [WolverinePost("/api/backoffice/orders/{orderId}/notes")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "CustomerService")]
    public static CreationResponse<Guid> Handle(
        Backoffice.OrderNote.AddOrderNote command,
        HttpContext httpContext,
        IDocumentSession session)
    {
        var noteId = Guid.CreateVersion7();

        // Extract adminUserId from JWT claims
        var adminUserIdClaim = httpContext.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(adminUserIdClaim, out var adminUserId))
        {
            throw new InvalidOperationException("Invalid admin user ID in JWT claims");
        }

        var @event = new Backoffice.OrderNote.OrderNoteAdded(
            command.OrderId,
            adminUserId,
            command.Text,
            DateTimeOffset.UtcNow);

        // Start event stream - Wolverine will call SaveChangesAsync()
        session.Events.StartStream<Backoffice.OrderNote.OrderNote>(noteId, @event);

        return new CreationResponse<Guid>($"/api/backoffice/orders/{command.OrderId}/notes/{noteId}", noteId);
    }
}
