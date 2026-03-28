using Marten;
using Messages.Contracts.VendorIdentity;
using Microsoft.Extensions.Logging;

namespace VendorPortal.VendorAccount;

/// <summary>
/// Handles VendorTenantCreated integration event from Vendor Identity BC.
/// Initializes a new VendorAccount document with default notification preferences (all ON).
/// Idempotent: if an account already exists for the tenant, the duplicate event is ignored.
/// </summary>
public static class VendorTenantCreatedHandler
{
    public static async Task Handle(
        VendorTenantCreated message,
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var existing = await session.LoadAsync<VendorAccount>(message.VendorTenantId, ct);
        if (existing is not null)
        {
            logger.LogDebug(
                "VendorAccount already exists for tenant {TenantId} — skipping duplicate VendorTenantCreated",
                message.VendorTenantId);
            return;
        }

        var account = new VendorAccount
        {
            Id = message.VendorTenantId,
            VendorTenantId = message.VendorTenantId,
            OrganizationName = message.OrganizationName,
            ContactEmail = message.ContactEmail,
            NotificationPreferences = NotificationPreferences.AllEnabled,
            SavedDashboardViews = [],
            CreatedAt = message.CreatedAt,
            UpdatedAt = message.CreatedAt,
        };

        session.Store(account);

        logger.LogInformation(
            "VendorAccount initialized for tenant {TenantId} ({OrgName})",
            message.VendorTenantId, message.OrganizationName);
    }
}
