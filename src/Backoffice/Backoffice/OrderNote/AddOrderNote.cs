using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Backoffice.OrderNote;

/// <summary>
/// Command: Add a new internal note to an order.
/// </summary>
public sealed record AddOrderNote(
    Guid OrderId,
    string Text);

public static class AddOrderNoteValidation
{
    public static ProblemDetails? Validate(AddOrderNote command)
    {
        if (command.OrderId == Guid.Empty)
        {
            return new ProblemDetails
            {
                Detail = "OrderId is required",
                Status = StatusCodes.Status400BadRequest
            };
        }

        if (string.IsNullOrWhiteSpace(command.Text))
        {
            return new ProblemDetails
            {
                Detail = "Text is required",
                Status = StatusCodes.Status400BadRequest
            };
        }

        if (command.Text.Length > 2000)
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
