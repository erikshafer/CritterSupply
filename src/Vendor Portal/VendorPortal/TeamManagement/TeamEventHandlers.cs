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

        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "Team member invited: {Email} for tenant {TenantId}",
            message.Email, message.VendorTenantId);
    }
}

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

        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "Team member activated: {UserId} for tenant {TenantId}",
            message.UserId, message.VendorTenantId);
    }
}

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
            await session.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Team member deactivated: {UserId} for tenant {TenantId}",
            message.UserId, message.VendorTenantId);
    }
}

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
            await session.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Team member reactivated: {UserId} for tenant {TenantId}",
            message.UserId, message.VendorTenantId);
    }
}

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
            await session.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Team member role changed: {UserId} from {OldRole} to {NewRole}",
            message.UserId, message.OldRole, message.NewRole);
    }
}

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
            await session.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Invitation resent for user {UserId}, resend count: {Count}",
            message.UserId, message.ResendCount);
    }
}

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

        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "Invitation revoked for user {UserId}",
            message.UserId);
    }
}
