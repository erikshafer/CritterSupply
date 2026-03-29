using Marten;
using Messages.Contracts.VendorIdentity;
using Microsoft.Extensions.Logging;

namespace VendorPortal.TeamManagement;

/// <summary>
/// Handles VendorUserInvitationResent event from Vendor Identity BC.
/// Updates TeamInvitation with new expiry and incremented resend count.
/// </summary>
public static class VendorUserInvitationResentHandler
{
    public static async Task Handle(
        VendorUserInvitationResent message,
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var invitation = await session.LoadAsync<TeamInvitation>(message.UserId, ct);
        if (invitation is not null)
        {
            invitation.ResendCount = message.ResendCount;
            invitation.ExpiresAt = message.NewExpiresAt;
            session.Store(invitation);
        }

        logger.LogInformation(
            "Invitation resent for user {UserId}, resend count: {Count}",
            message.UserId, message.ResendCount);
    }
}
