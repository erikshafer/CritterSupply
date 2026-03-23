using Marten;
using VendorPortal.RealTime;

namespace VendorPortal.ChangeRequests;

/// <summary>
/// Withdraws a change request. Allowed from Draft, Submitted, or NeedsMoreInfo states.
/// Transitions the request to Withdrawn (terminal state).
/// </summary>
/// <param name="RequestId">ID of the request to withdraw.</param>
/// <param name="VendorTenantId">The tenant withdrawing (from JWT claims — must match the request).</param>
public sealed record WithdrawChangeRequest(
    Guid RequestId,
    Guid VendorTenantId);

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
