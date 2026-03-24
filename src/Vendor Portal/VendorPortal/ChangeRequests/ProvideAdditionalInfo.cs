using FluentValidation;
using Marten;
using Messages.Contracts.VendorPortal;
using VendorPortal.RealTime;

namespace VendorPortal.ChangeRequests;

/// <summary>
/// Provides additional information requested by the Catalog BC.
/// Transitions the request from NeedsMoreInfo → Submitted and re-publishes the integration
/// message to the Catalog BC with the updated notes.
/// </summary>
/// <param name="RequestId">ID of the NeedsMoreInfo request.</param>
/// <param name="VendorTenantId">The tenant responding (from JWT claims — must match the request).</param>
/// <param name="Response">The vendor's answer to the Catalog BC's question.</param>
public sealed record ProvideAdditionalInfo(
    Guid RequestId,
    Guid VendorTenantId,
    string Response);

/// <summary>
/// Validates <see cref="ProvideAdditionalInfo"/> command.
/// Enforces required fields and length constraints on the vendor's response.
/// </summary>
public sealed class ProvideAdditionalInfoValidator : AbstractValidator<ProvideAdditionalInfo>
{
    public ProvideAdditionalInfoValidator()
    {
        RuleFor(x => x.RequestId)
            .NotEmpty()
            .WithMessage("RequestId is required");

        RuleFor(x => x.VendorTenantId)
            .NotEmpty()
            .WithMessage("VendorTenantId is required");

        RuleFor(x => x.Response)
            .NotEmpty()
            .WithMessage("Response is required")
            .MaximumLength(2000)
            .WithMessage("Response cannot exceed 2000 characters");
    }
}

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

        // Append a structured entry to the InfoResponses list.
        // This replaces the previous string-concatenation pattern and preserves each
        // Q&A round as a distinct timestamped entry for the UI to render as a thread.
        request.InfoResponses.Add(new VendorInfoResponse(command.Response, now));

        request.Status = ChangeRequestStatus.Submitted;
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

        // Pass command.Response explicitly so Catalog BC sees the vendor's answer.
        // AdditionalNotes on the request still holds the original draft notes;
        // the structured Q&A history lives in InfoResponses on the document.
        var catalogMessage = BuildCatalogMessage(request, command.Response, now);
        if (catalogMessage is not null) outgoing.Add(catalogMessage);

        return outgoing;
    }

    private static object? BuildCatalogMessage(ChangeRequest request, string vendorResponse, DateTimeOffset submittedAt)
    {
        return request.Type switch
        {
            ChangeRequestType.Description => new DescriptionChangeRequested(
                RequestId: request.Id,
                VendorTenantId: request.VendorTenantId,
                Sku: request.Sku,
                NewDescription: request.Details,
                AdditionalNotes: vendorResponse,
                SubmittedAt: submittedAt),

            ChangeRequestType.Image => new ImageUploadRequested(
                RequestId: request.Id,
                VendorTenantId: request.VendorTenantId,
                Sku: request.Sku,
                ImageStorageKeys: request.ImageStorageKeys,
                AdditionalNotes: vendorResponse,
                SubmittedAt: submittedAt),

            ChangeRequestType.DataCorrection => new DataCorrectionRequested(
                RequestId: request.Id,
                VendorTenantId: request.VendorTenantId,
                Sku: request.Sku,
                CorrectionType: "General",
                CorrectionDetails: request.Details,
                AdditionalNotes: vendorResponse,
                SubmittedAt: submittedAt),

            _ => null
        };
    }
}
