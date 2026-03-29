using Marten;
using Messages.Contracts.ProductCatalog;
using VendorPortal.RealTime;

namespace VendorPortal.ChangeRequests;

/// <summary>
/// Handles <see cref="AdditionalInfoRequested"/> integration messages from the Catalog BC.
/// Transitions a Submitted change request to NeedsMoreInfo status, stores the backoffice question,
/// and emits SignalR hub messages to prompt the vendor for additional information.
///
/// Silently ignores requests that are not in Submitted status (idempotency guard for redelivery).
/// </summary>
public static class AdditionalInfoRequestedHandler
{
    public static async Task<IEnumerable<object>> Handle(
        AdditionalInfoRequested @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(@event.RequestId, ct);
        if (request is null || request.VendorTenantId != @event.VendorTenantId)
            return [];

        if (request.Status != ChangeRequestStatus.Submitted)
            return [];

        request.Status = ChangeRequestStatus.NeedsMoreInfo;
        request.Question = @event.Question;
        session.Store(request);

        return
        [
            new ChangeRequestStatusUpdated(request.VendorTenantId, request.Id, request.Sku, "NeedsMoreInfo", @event.RequestedAt),
            new ChangeRequestDecisionPersonal(request.SubmittedByUserId, request.Id, request.Sku, "NeedsMoreInfo", @event.Question, @event.RequestedAt, request.Type.ToString())
        ];
    }
}
