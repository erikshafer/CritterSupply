using Marten;
using Messages.Contracts.VendorIdentity;
using Microsoft.Extensions.Logging;

namespace VendorPortal.TeamManagement;

/// <summary>
/// Handles VendorUserInvited event from Vendor Identity BC.
/// Creates both a TeamMember (status=Invited) and a TeamInvitation (status=Pending) document.
/// Idempotent: skips if documents already exist.
/// </summary>
public static class VendorUserInvitedHandler
{
    public static async Task Handle(
        VendorUserInvited message,
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var existingMember = await session.LoadAsync<TeamMember>(message.UserId, ct);
        if (existingMember is null)
        {
            session.Store(new TeamMember
            {
                Id = message.UserId,
                VendorTenantId = message.VendorTenantId,
                Email = message.Email,
                FirstName = "",
                LastName = "",
                Role = message.Role.ToString(),
                Status = "Invited",
                InvitedAt = message.InvitedAt,
            });
        }

        var existingInvitation = await session.LoadAsync<TeamInvitation>(message.UserId, ct);
        if (existingInvitation is null)
        {
            session.Store(new TeamInvitation
            {
                Id = message.UserId,
                VendorTenantId = message.VendorTenantId,
                Email = message.Email,
                Role = message.Role.ToString(),
                Status = "Pending",
                ResendCount = 0,
                InvitedAt = message.InvitedAt,
                ExpiresAt = message.ExpiresAt,
            });
        }

        logger.LogInformation(
            "Team member invited: {Email} for tenant {TenantId}",
            message.Email, message.VendorTenantId);
    }
}
