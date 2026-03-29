using Marten;
using Messages.Contracts.VendorIdentity;
using Microsoft.Extensions.Logging;

namespace VendorPortal.TeamManagement;

/// <summary>
/// Handles VendorUserRoleChanged event from Vendor Identity BC.
/// Updates TeamMember role.
/// </summary>
public static class VendorUserRoleChangedHandler
{
    public static async Task Handle(
        VendorUserRoleChanged message,
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var member = await session.LoadAsync<TeamMember>(message.UserId, ct);
        if (member is not null)
        {
            member.Role = message.NewRole.ToString();
            session.Store(member);
        }

        logger.LogInformation(
            "Team member role changed: {UserId} from {OldRole} to {NewRole}",
            message.UserId, message.OldRole, message.NewRole);
    }
}
