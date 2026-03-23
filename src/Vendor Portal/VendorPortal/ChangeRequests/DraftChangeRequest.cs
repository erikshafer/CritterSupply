using Marten;

namespace VendorPortal.ChangeRequests;

/// <summary>
/// Creates a change request in Draft state.
/// The request is saved locally but not yet submitted to the Catalog BC.
/// </summary>
/// <param name="RequestId">Caller-supplied request ID (idempotency key).</param>
/// <param name="VendorTenantId">The tenant submitting the request (from JWT claims).</param>
/// <param name="SubmittedByUserId">The user creating the draft (from JWT claims).</param>
/// <param name="Sku">The product SKU this change applies to.</param>
/// <param name="Type">Type of change: Description, Image, or DataCorrection.</param>
/// <param name="Title">Short summary of the change (shown in list views).</param>
/// <param name="Details">Full details of the change. For Image type, a brief description of the new images.</param>
/// <param name="AdditionalNotes">Optional vendor notes.</param>
/// <param name="ImageStorageKeys">Pre-uploaded image storage keys (Image type only).</param>
public sealed record DraftChangeRequest(
    Guid RequestId,
    Guid VendorTenantId,
    Guid SubmittedByUserId,
    string Sku,
    ChangeRequestType Type,
    string Title,
    string Details,
    string? AdditionalNotes = null,
    IReadOnlyList<string>? ImageStorageKeys = null);

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
