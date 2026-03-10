using Marten;
using VendorPortal.ChangeRequests.Commands;

namespace VendorPortal.ChangeRequests.Handlers;

/// <summary>
/// Handles <see cref="DraftChangeRequest"/> commands.
/// Creates a new <see cref="ChangeRequest"/> document in Draft state.
/// Idempotent: if a request with the same ID already exists, does nothing.
/// </summary>
public static class DraftChangeRequestHandler
{
    public static async Task Handle(
        DraftChangeRequest command,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Idempotency: if this request already exists, skip (at-least-once delivery safety)
        var existing = await session.LoadAsync<ChangeRequest>(command.RequestId, ct);
        if (existing is not null) return;

        var request = new ChangeRequest
        {
            Id = command.RequestId,
            VendorTenantId = command.VendorTenantId,
            SubmittedByUserId = command.SubmittedByUserId,
            Sku = command.Sku,
            Type = command.Type,
            Status = ChangeRequestStatus.Draft,
            Title = command.Title,
            Details = command.Details,
            AdditionalNotes = command.AdditionalNotes,
            ImageStorageKeys = command.ImageStorageKeys ?? [],
            CreatedAt = DateTimeOffset.UtcNow
        };

        session.Store(request);
        await session.SaveChangesAsync(ct);
    }
}
