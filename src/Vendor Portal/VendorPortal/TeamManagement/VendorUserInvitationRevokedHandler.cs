using Marten;
using Messages.Contracts.VendorIdentity;
using Microsoft.Extensions.Logging;

namespace VendorPortal.TeamManagement;

/// <summary>
/// Handles VendorUserInvitationRevoked event from Vendor Identity BC.
/// Removes the TeamInvitation document.
/// </summary>
public static class VendorUserInvitationRevokedHandler
{
    public static async Task Handle(
        VendorUserInvitationRevoked message,
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        session.Delete<TeamInvitation>(message.UserId);

        // Also update team member status if still Invited
        var member = await session.LoadAsync<TeamMember>(message.UserId, ct);
        if (member is not null && member.Status == "Invited")
        {
            session.Delete<TeamMember>(message.UserId);
        }

        logger.LogInformation(
            "Invitation revoked for user {UserId}",
            message.UserId);
    }
}
