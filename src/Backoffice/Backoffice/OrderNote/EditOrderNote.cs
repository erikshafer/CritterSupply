using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Backoffice.OrderNote;

/// <summary>
/// Command: Edit an existing order note (original author only).
/// </summary>
public sealed record EditOrderNote(
    Guid NoteId,
    string NewText);

public static class EditOrderNoteValidation
{
    public static ProblemDetails? Validate(EditOrderNote command)
    {
        if (command.NoteId == Guid.Empty)
        {
            return new ProblemDetails
            {
                Detail = "NoteId is required",
                Status = StatusCodes.Status400BadRequest
            };
        }

        if (string.IsNullOrWhiteSpace(command.NewText))
        {
            return new ProblemDetails
            {
                Detail = "NewText is required",
                Status = StatusCodes.Status400BadRequest
            };
        }

        if (command.NewText.Length > 2000)
        {
            return new ProblemDetails
            {
                Detail = "Note text must be 2000 characters or less",
                Status = StatusCodes.Status400BadRequest
            };
        }

        return null;
    }
}
