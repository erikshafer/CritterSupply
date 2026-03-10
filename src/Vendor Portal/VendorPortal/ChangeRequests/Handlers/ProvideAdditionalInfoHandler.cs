using Marten;
using Messages.Contracts.VendorPortal;
using VendorPortal.ChangeRequests.Commands;
using VendorPortal.RealTime;

namespace VendorPortal.ChangeRequests.Handlers;

/// <summary>
/// Handles <see cref="ProvideAdditionalInfo"/> commands.
/// Transitions the request from NeedsMoreInfo → Submitted and returns the updated
/// integration message to the Catalog BC with the vendor's response.
/// Returns both the catalog message and a hub notification as cascaded messages.
/// </summary>
public static class ProvideAdditionalInfoHandler
{
    public static async Task<IEnumerable<object>> Handle(
        ProvideAdditionalInfo command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(command.RequestId, ct);

        if (request is null) return [];

        if (request.VendorTenantId != command.VendorTenantId)
        {
            // Cross-tenant access attempt — return silently
            return [];
        }

        if (request.Status != ChangeRequestStatus.NeedsMoreInfo) return [];

        var now = DateTimeOffset.UtcNow;

        // Append the vendor's response to AdditionalNotes
        var updatedNotes = string.IsNullOrWhiteSpace(request.AdditionalNotes)
            ? command.Response
            : $"{request.AdditionalNotes}\n\n[Response to question]: {command.Response}";

        request.Status = ChangeRequestStatus.Submitted;
        request.AdditionalNotes = updatedNotes;
        request.SubmittedAt = now;

        session.Store(request);
        await session.SaveChangesAsync(ct);

        // Return both the Catalog integration message and the hub notification
        var outgoing = new List<object>
        {
            new ChangeRequestStatusUpdated(
                VendorTenantId: request.VendorTenantId,
                RequestId: request.Id,
                Sku: request.Sku,
                Status: request.Status.ToString(),
                UpdatedAt: now)
        };

        var catalogMessage = BuildCatalogMessage(request, now);
        if (catalogMessage is not null) outgoing.Add(catalogMessage);

        return outgoing;
    }

    private static object? BuildCatalogMessage(ChangeRequest request, DateTimeOffset submittedAt)
    {
        return request.Type switch
        {
            ChangeRequestType.Description => new DescriptionChangeRequested(
                RequestId: request.Id,
                VendorTenantId: request.VendorTenantId,
                Sku: request.Sku,
                NewDescription: request.Details,
                AdditionalNotes: request.AdditionalNotes,
                SubmittedAt: submittedAt),

            ChangeRequestType.Image => new ImageUploadRequested(
                RequestId: request.Id,
                VendorTenantId: request.VendorTenantId,
                Sku: request.Sku,
                ImageStorageKeys: request.ImageStorageKeys,
                AdditionalNotes: request.AdditionalNotes,
                SubmittedAt: submittedAt),

            ChangeRequestType.DataCorrection => new DataCorrectionRequested(
                RequestId: request.Id,
                VendorTenantId: request.VendorTenantId,
                Sku: request.Sku,
                CorrectionType: "General",
                CorrectionDetails: request.Details,
                AdditionalNotes: request.AdditionalNotes,
                SubmittedAt: submittedAt),

            _ => null
        };
    }
}
