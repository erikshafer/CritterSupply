using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VendorIdentity.Identity;
using VendorIdentity.TenantManagement;
using VendorIdentity.UserInvitations;

namespace VendorIdentity.Api.Auth;

/// <summary>
/// Seeds test vendor accounts for POC development.
/// Only runs in Development environment.
/// </summary>
public static class VendorIdentitySeedData
{
    public static async Task SeedAsync(VendorIdentityDbContext dbContext)
    {
        if (await dbContext.Tenants.AnyAsync())
            return; // Already seeded

        var hasher = new PasswordHasher<VendorUser>();

        var acmeTenant = new VendorTenant
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            OrganizationName = "Acme Pet Supplies",
            ContactEmail = "admin@acmepets.test",
            Status = VendorTenantStatus.Active,
            OnboardedAt = DateTimeOffset.UtcNow.AddDays(-30),
        };

        var acmeAdmin = new VendorUser
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000010"),
            VendorTenantId = acmeTenant.Id,
            Email = "admin@acmepets.test",
            FirstName = "Alice",
            LastName = "Admin",
            Role = Messages.Contracts.VendorIdentity.VendorRole.Admin,
            Status = VendorUserStatus.Active,
            InvitedAt = DateTimeOffset.UtcNow.AddDays(-30),
            ActivatedAt = DateTimeOffset.UtcNow.AddDays(-29),
        };
        acmeAdmin.PasswordHash = hasher.HashPassword(acmeAdmin, "password");

        var acmeCatalogMgr = new VendorUser
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
            VendorTenantId = acmeTenant.Id,
            Email = "catalog@acmepets.test",
            FirstName = "Bob",
            LastName = "Catalog",
            Role = Messages.Contracts.VendorIdentity.VendorRole.CatalogManager,
            Status = VendorUserStatus.Active,
            InvitedAt = DateTimeOffset.UtcNow.AddDays(-25),
            ActivatedAt = DateTimeOffset.UtcNow.AddDays(-24),
        };
        acmeCatalogMgr.PasswordHash = hasher.HashPassword(acmeCatalogMgr, "password");

        var acmeReadOnly = new VendorUser
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000012"),
            VendorTenantId = acmeTenant.Id,
            Email = "readonly@acmepets.test",
            FirstName = "Carol",
            LastName = "Readonly",
            Role = Messages.Contracts.VendorIdentity.VendorRole.ReadOnly,
            Status = VendorUserStatus.Active,
            InvitedAt = DateTimeOffset.UtcNow.AddDays(-20),
            ActivatedAt = DateTimeOffset.UtcNow.AddDays(-19),
        };
        acmeReadOnly.PasswordHash = hasher.HashPassword(acmeReadOnly, "password");

        acmeTenant.Users.Add(acmeAdmin);
        acmeTenant.Users.Add(acmeCatalogMgr);
        acmeTenant.Users.Add(acmeReadOnly);

        await dbContext.Tenants.AddAsync(acmeTenant);
        await dbContext.SaveChangesAsync();
    }
}
