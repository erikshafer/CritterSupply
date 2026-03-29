using FluentValidation;
using Marten;
using Messages.Contracts.VendorPortal;
using VendorPortal.RealTime;

namespace VendorPortal.ChangeRequests;

/// <summary>
/// Submits an existing Draft change request to the Catalog BC for review.
/// Transitions the request from Draft → Submitted and publishes the appropriate
/// integration message to the Catalog BC.
///
/// Invariant: if an active request already exists for the same SKU+Type+Tenant,
/// it is automatically superseded by this one.
/// </summary>
/// <param name="RequestId">ID of the Draft request to submit.</param>
/// <param name="VendorTenantId">The tenant submitting (from JWT claims — must match the request).</param>
public sealed record SubmitChangeRequest(
    Guid RequestId,
    Guid VendorTenantId);

/// <summary>
/// Validates <see cref="SubmitChangeRequest"/> command.
/// Enforces that both RequestId and VendorTenantId are non-empty GUIDs.
/// </summary>
public sealed class SubmitChangeRequestValidator : AbstractValidator<SubmitChangeRequest>
{
    public SubmitChangeRequestValidator()
    {
        RuleFor(x => x.RequestId)
            .NotEmpty()
            .WithMessage("RequestId is required");

        RuleFor(x => x.VendorTenantId)
            .NotEmpty()
            .WithMessage("VendorTenantId is required");
    }
}

/// <summary>
/// Handles <see cref="SubmitChangeRequest"/> commands.
///
/// Lifecycle:
/// 1. Validates the request exists, belongs to the tenant, and is in Draft state.
/// 2. Auto-supersedes any existing active request for the same SKU+Type+Tenant.
/// 3. Transitions the request to Submitted state.
/// 4. Returns the appropriate integration message for Catalog BC + a hub notification.
///    Wolverine routes the returned messages: catalog message goes to RabbitMQ exchange,
///    ChangeRequestStatusUpdated goes to the SignalR vendor:{tenantId} group.
/// </summary>
public static class SubmitChangeRequestHandler
{
    public static async Task<IEnumerable<object>> Handle(
        SubmitChangeRequest command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(command.RequestId, ct);

        if (request is null)
        {
            // Unknown request — return empty; Wolverine traces implicitly
            return [];
        }

        if (request.VendorTenantId != command.VendorTenantId)
        {
            // Cross-tenant access attempt — return silently (never reveal existence)
            return [];
        }

        if (request.Status != ChangeRequestStatus.Draft)
        {
            // Already submitted (at-least-once delivery) or invalid transition — skip
            return [];
        }

        var now = DateTimeOffset.UtcNow;

        // Auto-withdraw invariant: supersede any other active request for this SKU+Type.
        // "Active" mirrors ChangeRequest.ActiveStatuses (Draft, Submitted, NeedsMoreInfo).
        // Explicit OR conditions are required because Marten LINQ cannot parameterize enum arrays.
        var existingActive = await session.Query<ChangeRequest>()
            .Where(r =>
                r.VendorTenantId == command.VendorTenantId &&
                r.Sku == request.Sku &&
                r.Type == request.Type &&
                r.Id != command.RequestId &&
                (r.Status == ChangeRequestStatus.Draft ||
                 r.Status == ChangeRequestStatus.Submitted ||
                 r.Status == ChangeRequestStatus.NeedsMoreInfo))
            .ToListAsync(ct);

        foreach (var activeRequest in existingActive)
        {
            activeRequest.Status = ChangeRequestStatus.Superseded;
            activeRequest.ReplacedByRequestId = command.RequestId;
            activeRequest.ResolvedAt = now;
            session.Store(activeRequest);
        }

        // Transition to Submitted
        request.Status = ChangeRequestStatus.Submitted;
        request.SubmittedAt = now;
        session.Store(request);

        // Return both the Catalog integration message and the hub notification.
        // Wolverine routes each based on publish rules and message type.
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
