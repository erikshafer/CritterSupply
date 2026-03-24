using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Backoffice.Api.Commands;

/// <summary>
/// HTTP endpoint handler for DeleteOrderNote command.
/// </summary>
public static class DeleteOrderNoteEndpoint
{
    /// <summary>
    /// Validate note exists, is not already deleted, and user has permission.
    /// </summary>
    public static async Task<ProblemDetails?> BeforeAsync(
        Backoffice.OrderNote.DeleteOrderNote command,
        IDocumentSession session,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // Domain-level validation
        var validationResult = Backoffice.OrderNote.DeleteOrderNoteValidation.Validate(command);
        if (validationResult is not null)
        {
            return validationResult;
        }

        // Load note from Marten snapshot projection
        var note = await session.LoadAsync<Backoffice.OrderNote.OrderNote>(command.NoteId, ct);

        if (note is null)
        {
            return new ProblemDetails
            {
                Detail = $"Note {command.NoteId} not found",
                Status = StatusCodes.Status404NotFound
            };
        }

        if (note.IsDeleted)
        {
            return new ProblemDetails
            {
                Detail = "Note is already deleted",
                Status = StatusCodes.Status400BadRequest
            };
        }

        // Extract adminUserId from JWT claims
        var adminUserIdClaim = httpContext.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(adminUserIdClaim, out var adminUserId))
        {
            return new ProblemDetails
            {
                Detail = "Invalid admin user ID in JWT claims",
                Status = StatusCodes.Status400BadRequest
            };
        }

        // Check if user is system admin (can delete any note)
        var isSystemAdmin = httpContext.User.IsInRole("system-admin");

        // Original author or system admin can delete
        if (note.AdminUserId != adminUserId && !isSystemAdmin)
        {
            return new ProblemDetails
            {
                Detail = "Only the original author or system admin can delete this note",
                Status = StatusCodes.Status403Forbidden
            };
        }

        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Pure function: Command → Event
    /// </summary>
    [WolverineDelete("/api/backoffice/orders/{orderId}/notes/{noteId}")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "CustomerService")]
    public static Events Handle(
        Backoffice.OrderNote.DeleteOrderNote command,
        IDocumentSession session)
    {
        var @event = new Backoffice.OrderNote.OrderNoteDeleted(DateTimeOffset.UtcNow);

        // Append event to the stream
        session.Events.Append(command.NoteId, @event);

        return [@event];
    }
}
