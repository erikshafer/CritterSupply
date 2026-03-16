using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Backoffice.OrderNote;

/// <summary>
/// Command: Soft-delete an order note (original author or system admin).
/// </summary>
public sealed record DeleteOrderNote(
    Guid NoteId);

public static class DeleteOrderNoteValidation
{
    public static ProblemDetails? Validate(DeleteOrderNote command)
    {
        if (command.NoteId == Guid.Empty)
        {
            return new ProblemDetails
            {
                Detail = "NoteId is required",
                Status = StatusCodes.Status400BadRequest
            };
        }

        return null;
    }
}
