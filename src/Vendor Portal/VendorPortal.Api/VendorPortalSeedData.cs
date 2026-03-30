using Marten;

/// <summary>
/// Seeds VendorAccount documents in Marten for the development test tenants.
///
/// In production, VendorAccount documents are created when VendorPortal.Api receives
/// VendorTenantCreated integration events from VendorIdentity.Api via RabbitMQ.
/// In development, VendorIdentitySeedData writes directly to EF Core and bypasses
/// the event bus, so no VendorTenantCreated messages are published — and no
/// VendorAccount documents are created.
///
/// This class fills that gap so that developers can exercise account-level features
/// (notification preferences, dashboard views) against the seed tenants without
/// having to manually trigger events.
///
/// Only runs in the Development environment.
/// </summary>
public static class VendorPortalSeedData
{
    // Must match the tenant IDs in VendorIdentitySeedData
    private static readonly Guid HearthHoundTenantId = Guid.Parse("10000000-0000-0000-0000-000000000101");
    private static readonly Guid TumblePawTenantId = Guid.Parse("10000000-0000-0000-0000-000000000102");

    public static async Task SeedAsync(IDocumentStore store)
    {
        await using var session = store.LightweightSession();

        var existing = await session.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(HearthHoundTenantId);
        if (existing is not null)
            return; // Already seeded

        var hearthHoundAccount = new VendorPortal.VendorAccount.VendorAccount
        {
            Id = HearthHoundTenantId,
            VendorTenantId = HearthHoundTenantId,
            OrganizationName = "HearthHound Nutrition Co.",
            ContactEmail = "ops@hearthhound.com",
            NotificationPreferences = VendorPortal.VendorAccount.NotificationPreferences.AllEnabled,
            SavedDashboardViews = [],
            CreatedAt = DateTimeOffset.Parse("2024-09-12T14:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2024-09-12T14:00:00Z"),
        };

        var tumblePawAccount = new VendorPortal.VendorAccount.VendorAccount
        {
            Id = TumblePawTenantId,
            VendorTenantId = TumblePawTenantId,
            OrganizationName = "TumblePaw Play Labs",
            ContactEmail = "asha@tumblepaw.com",
            NotificationPreferences = VendorPortal.VendorAccount.NotificationPreferences.AllEnabled,
            SavedDashboardViews = [],
            CreatedAt = DateTimeOffset.Parse("2026-03-01T10:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-03-01T10:00:00Z"),
        };

        session.Store(hearthHoundAccount);
        session.Store(tumblePawAccount);
        await session.SaveChangesAsync();
    }
}
