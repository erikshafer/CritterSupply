using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query: Get all non-deleted notes for an order.
/// Returns notes sorted by creation date (oldest first).
/// </summary>
public static class GetOrderNotes
{
    [WolverineGet("/api/backoffice/orders/{orderId}/notes")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<Results<Ok<IReadOnlyList<OrderNoteDto>>, NotFound>> Get(
        Guid orderId,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Query all notes for this order (excluding deleted ones)
        var notes = await session.Query<Backoffice.OrderNote.OrderNote>()
            .Where(n => n.OrderId == orderId && !n.IsDeleted)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);

        // Map to DTOs
        var dtos = notes.Select(n => new OrderNoteDto(
            n.Id,
            n.OrderId,
            n.AdminUserId,
            n.Text,
            n.CreatedAt,
            n.EditedAt)).ToList();

        return TypedResults.Ok<IReadOnlyList<OrderNoteDto>>(dtos);
    }
}

/// <summary>
/// DTO for OrderNote read model.
/// </summary>
public sealed record OrderNoteDto(
    Guid NoteId,
    Guid OrderId,
    Guid AdminUserId,
    string Text,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt);
