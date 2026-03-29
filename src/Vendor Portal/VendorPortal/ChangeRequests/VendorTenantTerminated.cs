using Marten;
using Messages.Contracts.VendorIdentity;
using Microsoft.Extensions.Logging;

namespace VendorPortal.ChangeRequests;

/// <summary>
/// Handles VendorTenantTerminated integration event from Vendor Identity BC.
/// Auto-rejects all in-flight change requests (Submitted or NeedsMoreInfo) for the terminated tenant.
/// Idempotent: already-resolved requests are skipped.
/// </summary>
public static class VendorTenantTerminatedHandler
{
    public static async Task Handle(
        VendorTenantTerminated message,
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        // Find all in-flight change requests for this tenant
        // Marten LINQ limitation: must use explicit OR conditions for enum comparisons
        var inflight = await session.Query<ChangeRequest>()
            .Where(r => r.VendorTenantId == message.VendorTenantId
                        && (r.Status == ChangeRequestStatus.Submitted
                            || r.Status == ChangeRequestStatus.NeedsMoreInfo))
            .ToListAsync(ct);

        if (inflight.Count == 0)
        {
            logger.LogDebug(
                "No in-flight change requests found for terminated tenant {TenantId}",
                message.VendorTenantId);
            return;
        }

        foreach (var request in inflight)
        {
            request.Status = ChangeRequestStatus.Rejected;
            request.RejectionReason = "Vendor contract ended";
            request.ResolvedAt = message.TerminatedAt;
            session.Store(request);
        }

        logger.LogInformation(
            "Auto-rejected {Count} in-flight change requests for terminated tenant {TenantId}",
            inflight.Count, message.VendorTenantId);
    }
}
