using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Backoffice.Api.Commands;

/// <summary>
/// HTTP endpoint handler for EditOrderNote command.
/// </summary>
public static class EditOrderNoteEndpoint
{
    /// <summary>
    /// Validate note exists, is not deleted, and user is original author.
    /// </summary>
    public static async Task<ProblemDetails?> BeforeAsync(
        Backoffice.OrderNote.EditOrderNote command,
        IDocumentSession session,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // Domain-level validation
        var validationResult = Backoffice.OrderNote.EditOrderNoteValidation.Validate(command);
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
                Detail = "Cannot edit deleted note",
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

        // Only original author can edit
        if (note.AdminUserId != adminUserId)
        {
            return new ProblemDetails
            {
                Detail = "Only the original author can edit this note",
                Status = StatusCodes.Status403Forbidden
            };
        }

        return null; // No problems
    }

    /// <summary>
    /// Pure function: Command → Event
    /// </summary>
    [WolverinePut("/api/backoffice/orders/{orderId}/notes/{noteId}")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "CustomerService")]
    public static Events Handle(
        Backoffice.OrderNote.EditOrderNote command,
        IDocumentSession session)
    {
        var @event = new Backoffice.OrderNote.OrderNoteEdited(
            command.NewText,
            DateTimeOffset.UtcNow);

        // Append event to the stream
        session.Events.Append(command.NoteId, @event);

        return [@event];
    }
}
