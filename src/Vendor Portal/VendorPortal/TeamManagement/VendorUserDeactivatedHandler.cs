using Marten;
using Messages.Contracts.VendorIdentity;
using Microsoft.Extensions.Logging;

namespace VendorPortal.TeamManagement;

/// <summary>
/// Handles VendorUserDeactivated event from Vendor Identity BC.
/// Updates TeamMember to Deactivated status.
/// </summary>
public static class VendorUserDeactivatedHandler
{
    public static async Task Handle(
        VendorUserDeactivated message,
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var member = await session.LoadAsync<TeamMember>(message.UserId, ct);
        if (member is not null)
        {
            member.Status = "Deactivated";
            member.DeactivatedAt = message.DeactivatedAt;
            session.Store(member);
        }

        logger.LogInformation(
            "Team member deactivated: {UserId} for tenant {TenantId}",
            message.UserId, message.VendorTenantId);
    }
}
