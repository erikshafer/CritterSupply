using Marten;
using VendorPortal.ChangeRequests.Commands;
using VendorPortal.RealTime;

namespace VendorPortal.ChangeRequests.Handlers;

/// <summary>
/// Handles <see cref="WithdrawChangeRequest"/> commands.
/// Allowed from Draft, Submitted, or NeedsMoreInfo states.
/// Transitions the request to Withdrawn (terminal state).
/// Returns a <see cref="ChangeRequestStatusUpdated"/> hub message on success.
/// </summary>
public static class WithdrawChangeRequestHandler
{
    public static async Task<ChangeRequestStatusUpdated?> Handle(
        WithdrawChangeRequest command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(command.RequestId, ct);

        if (request is null) return null;

        if (request.VendorTenantId != command.VendorTenantId)
        {
            // Cross-tenant access attempt — return silently
            return null;
        }

        // Only allow withdrawal from active states
        if (!request.IsActive) return null;

        var now = DateTimeOffset.UtcNow;

        request.Status = ChangeRequestStatus.Withdrawn;
        request.ResolvedAt = now;

        session.Store(request);
        await session.SaveChangesAsync(ct);

        return new ChangeRequestStatusUpdated(
            VendorTenantId: request.VendorTenantId,
            RequestId: request.Id,
            Sku: request.Sku,
            Status: request.Status.ToString(),
            UpdatedAt: now);
    }
}
