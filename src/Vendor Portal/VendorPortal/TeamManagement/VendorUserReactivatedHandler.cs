using Marten;
using Messages.Contracts.VendorIdentity;
using Microsoft.Extensions.Logging;

namespace VendorPortal.TeamManagement;

/// <summary>
/// Handles VendorUserReactivated event from Vendor Identity BC.
/// Updates TeamMember back to Active status.
/// </summary>
public static class VendorUserReactivatedHandler
{
    public static async Task Handle(
        VendorUserReactivated message,
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var member = await session.LoadAsync<TeamMember>(message.UserId, ct);
        if (member is not null)
        {
            member.Status = "Active";
            member.DeactivatedAt = null;
            session.Store(member);
        }

        logger.LogInformation(
            "Team member reactivated: {UserId} for tenant {TenantId}",
            message.UserId, message.VendorTenantId);
    }
}
