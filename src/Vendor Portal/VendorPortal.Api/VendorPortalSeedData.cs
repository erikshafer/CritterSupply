using Marten;

/// <summary>
/// Seeds a VendorAccount document in Marten for the development test tenant.
///
/// In production, the VendorAccount is created when VendorPortal.Api receives a
/// VendorTenantCreated integration event from VendorIdentity.Api via RabbitMQ.
/// In development, VendorIdentitySeedData writes directly to EF Core and bypasses
/// the event bus, so no VendorTenantCreated message is ever published — and no
/// VendorAccount document is created.
///
/// This class fills that gap so that developers and UX researchers can exercise
/// account-level features (notification preferences, dashboard views) against the
/// seed tenant without having to manually trigger an event.
///
/// Only runs in the Development environment.
/// </summary>
internal static class VendorPortalSeedData
{
    // Must match the tenant ID in VendorIdentitySeedData
    private static readonly Guid AcmeTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static async Task SeedAsync(IDocumentStore store)
    {
        await using var session = store.LightweightSession();

        var existing = await session.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(AcmeTenantId);
        if (existing is not null)
            return; // Already seeded

        var account = new VendorPortal.VendorAccount.VendorAccount
        {
            Id = AcmeTenantId,
            VendorTenantId = AcmeTenantId,
            OrganizationName = "Acme Pet Supplies",
            ContactEmail = "admin@acmepets.test",
            NotificationPreferences = VendorPortal.VendorAccount.NotificationPreferences.AllEnabled,
            SavedDashboardViews = [],
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        };

        session.Store(account);
        await session.SaveChangesAsync();
    }
}
