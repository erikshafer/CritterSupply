namespace Backoffice.OrderNote;

// Domain Events (internal to Backoffice BC - stored in Marten event stream)

/// <summary>
/// OrderNote was created by a CS agent.
/// </summary>
public sealed record OrderNoteAdded(
    Guid OrderId,
    Guid AdminUserId,
    string Text,
    DateTimeOffset CreatedAt);

/// <summary>
/// OrderNote text was edited by the original author.
/// </summary>
public sealed record OrderNoteEdited(
    string NewText,
    DateTimeOffset EditedAt);

/// <summary>
/// OrderNote was soft-deleted by the original author or admin.
/// Event stream preserves full history for audit trail.
/// </summary>
public sealed record OrderNoteDeleted(
    DateTimeOffset DeletedAt);
