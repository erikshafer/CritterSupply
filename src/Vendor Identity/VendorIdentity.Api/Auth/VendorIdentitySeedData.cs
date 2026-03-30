using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using VendorIdentity.TenantManagement;
using VendorIdentity.UserInvitations;

namespace VendorIdentity.Api.Auth;

/// <summary>
/// Seeds test vendor accounts for development.
/// Only runs in Development environment. All passwords: Dev@123!
///
/// Vendors sourced from docs/domain/vendors/vendor-catalog.md:
///   1. HearthHound Nutrition Co. — default happy-path vendor (all three roles)
///   2. TumblePaw Play Labs — onboarding vendor (admin active, two users invited)
/// </summary>
public static class VendorIdentitySeedData
{
    public static async Task SeedAsync(VendorIdentityDbContext dbContext)
    {
        if (await dbContext.Tenants.AnyAsync())
            return; // Already seeded

        var hasher = new PasswordHasher<VendorUser>();
        const string devPassword = "Dev@123!";

        // ── HearthHound Nutrition Co. ──────────────────────────────────
        var hearthHoundTenant = new VendorTenant
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000101"),
            OrganizationName = "HearthHound Nutrition Co.",
            ContactEmail = "ops@hearthhound.com",
            Status = VendorTenantStatus.Active,
            OnboardedAt = DateTimeOffset.Parse("2024-09-12T14:00:00Z"),
        };

        var melissaKerr = CreateActiveUser(hasher, devPassword,
            id: Guid.Parse("10000000-0000-0000-0000-000000001101"),
            tenantId: hearthHoundTenant.Id,
            email: "mkerr@hearthhound.com",
            firstName: "Melissa", lastName: "Kerr",
            role: Messages.Contracts.VendorIdentity.VendorRole.Admin,
            invitedAt: DateTimeOffset.Parse("2024-09-12T15:00:00Z"),
            activatedAt: DateTimeOffset.Parse("2024-09-13T09:30:00Z"));

        var jordanPike = CreateActiveUser(hasher, devPassword,
            id: Guid.Parse("10000000-0000-0000-0000-000000001102"),
            tenantId: hearthHoundTenant.Id,
            email: "jpike@hearthhound.com",
            firstName: "Jordan", lastName: "Pike",
            role: Messages.Contracts.VendorIdentity.VendorRole.CatalogManager,
            invitedAt: DateTimeOffset.Parse("2024-09-14T14:15:00Z"),
            activatedAt: DateTimeOffset.Parse("2024-09-15T10:00:00Z"));

        var elenaSuarez = CreateActiveUser(hasher, devPassword,
            id: Guid.Parse("10000000-0000-0000-0000-000000001103"),
            tenantId: hearthHoundTenant.Id,
            email: "esuarez@hearthhound.com",
            firstName: "Elena", lastName: "Suarez",
            role: Messages.Contracts.VendorIdentity.VendorRole.ReadOnly,
            invitedAt: DateTimeOffset.Parse("2024-09-18T11:00:00Z"),
            activatedAt: DateTimeOffset.Parse("2024-09-19T08:20:00Z"));

        hearthHoundTenant.Users.Add(melissaKerr);
        hearthHoundTenant.Users.Add(jordanPike);
        hearthHoundTenant.Users.Add(elenaSuarez);

        // ── TumblePaw Play Labs (Onboarding) ──────────────────────────
        var tumblePawTenant = new VendorTenant
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000102"),
            OrganizationName = "TumblePaw Play Labs",
            ContactEmail = "asha@tumblepaw.com",
            Status = VendorTenantStatus.Onboarding,
            OnboardedAt = DateTimeOffset.Parse("2026-03-01T10:00:00Z"),
        };

        var ashaBell = CreateActiveUser(hasher, devPassword,
            id: Guid.Parse("10000000-0000-0000-0000-000000001201"),
            tenantId: tumblePawTenant.Id,
            email: "asha@tumblepaw.com",
            firstName: "Asha", lastName: "Bell",
            role: Messages.Contracts.VendorIdentity.VendorRole.Admin,
            invitedAt: DateTimeOffset.Parse("2026-03-01T10:30:00Z"),
            activatedAt: DateTimeOffset.Parse("2026-03-01T14:00:00Z"));

        var connorReeves = new VendorUser
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000001202"),
            VendorTenantId = tumblePawTenant.Id,
            Email = "connor@tumblepaw.com",
            FirstName = "Connor",
            LastName = "Reeves",
            Role = Messages.Contracts.VendorIdentity.VendorRole.CatalogManager,
            Status = VendorUserStatus.Invited,
            InvitedAt = DateTimeOffset.Parse("2026-03-02T09:00:00Z"),
        };
        // Invited users have no password hash

        var minaAlbright = new VendorUser
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000001203"),
            VendorTenantId = tumblePawTenant.Id,
            Email = "mina@tumblepaw.com",
            FirstName = "Mina",
            LastName = "Albright",
            Role = Messages.Contracts.VendorIdentity.VendorRole.ReadOnly,
            Status = VendorUserStatus.Invited,
            InvitedAt = DateTimeOffset.Parse("2026-03-02T09:15:00Z"),
        };

        tumblePawTenant.Users.Add(ashaBell);
        tumblePawTenant.Users.Add(connorReeves);
        tumblePawTenant.Users.Add(minaAlbright);

        // ── Persist ────────────────────────────────────────────────────
        await dbContext.Tenants.AddRangeAsync(hearthHoundTenant, tumblePawTenant);
        await dbContext.SaveChangesAsync();
    }

    private static VendorUser CreateActiveUser(
        PasswordHasher<VendorUser> hasher,
        string password,
        Guid id,
        Guid tenantId,
        string email,
        string firstName,
        string lastName,
        Messages.Contracts.VendorIdentity.VendorRole role,
        DateTimeOffset invitedAt,
        DateTimeOffset activatedAt)
    {
        var user = new VendorUser
        {
            Id = id,
            VendorTenantId = tenantId,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            Role = role,
            Status = VendorUserStatus.Active,
            InvitedAt = invitedAt,
            ActivatedAt = activatedAt,
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        return user;
    }
}
