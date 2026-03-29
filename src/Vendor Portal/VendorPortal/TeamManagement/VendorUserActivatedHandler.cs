using Marten;
using Messages.Contracts.VendorIdentity;
using Microsoft.Extensions.Logging;

namespace VendorPortal.TeamManagement;

/// <summary>
/// Handles VendorUserActivated event from Vendor Identity BC.
/// Updates TeamMember to Active status and removes the TeamInvitation (accepted).
/// </summary>
public static class VendorUserActivatedHandler
{
    public static async Task Handle(
        VendorUserActivated message,
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var member = await session.LoadAsync<TeamMember>(message.UserId, ct);
        if (member is not null)
        {
            member.Status = "Active";
            member.Role = message.Role.ToString();
            member.ActivatedAt = message.ActivatedAt;
            session.Store(member);
        }

        // Remove the invitation — it's been accepted
        session.Delete<TeamInvitation>(message.UserId);

        logger.LogInformation(
            "Team member activated: {UserId} for tenant {TenantId}",
            message.UserId, message.VendorTenantId);
    }
}
