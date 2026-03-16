using JasperFx.Events;

namespace Backoffice.OrderNote;

/// <summary>
/// Event-sourced aggregate representing internal CS notes attached to orders.
/// Lives in Backoffice BC, not Orders BC (see ADR 0037).
/// </summary>
public sealed record OrderNote(
    Guid Id,              // Stream ID (UUID v7)
    Guid OrderId,         // Logical reference to Orders BC
    Guid AdminUserId,     // CS agent who created note
    string Text,          // Note content (max 2000 characters)
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    bool IsDeleted)
{
    /// <summary>
    /// Factory method for aggregate creation from OrderNoteAdded event.
    /// </summary>
    public static OrderNote Create(IEvent<OrderNoteAdded> @event) =>
        new(
            @event.StreamId,
            @event.Data.OrderId,
            @event.Data.AdminUserId,
            @event.Data.Text,
            @event.Data.CreatedAt,
            EditedAt: null,
            IsDeleted: false);

    /// <summary>
    /// Apply OrderNoteEdited event.
    /// </summary>
    public OrderNote Apply(OrderNoteEdited @event) =>
        this with
        {
            Text = @event.NewText,
            EditedAt = @event.EditedAt
        };

    /// <summary>
    /// Apply OrderNoteDeleted event (soft delete).
    /// </summary>
    public OrderNote Apply(OrderNoteDeleted @event) =>
        this with { IsDeleted = true };
}
