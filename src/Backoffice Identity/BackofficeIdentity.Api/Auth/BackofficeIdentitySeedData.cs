using BackofficeIdentity.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BackofficeIdentity.Api.Auth;

/// <summary>
/// Seeds dev-only backoffice user accounts — one per role defined in ADR 0031.
/// Only runs in Development environment. All passwords: Dev@123!
/// </summary>
public static class BackofficeIdentitySeedData
{
    public static async Task SeedAsync(BackofficeIdentityDbContext dbContext)
    {
        if (await dbContext.Users.AnyAsync())
            return; // Already seeded

        var hasher = new PasswordHasher<BackofficeUser>();
        const string devPassword = "Dev@123!";

        var users = new[]
        {
            CreateUser(hasher, devPassword,
                id: Guid.Parse("AA000000-AA00-AA00-AA00-AA0000000001"),
                email: "admin@crittersupply.dev",
                firstName: "Alex",
                lastName: "Admin",
                role: BackofficeRole.SystemAdmin),

            CreateUser(hasher, devPassword,
                id: Guid.Parse("AA000000-AA00-AA00-AA00-AA0000000002"),
                email: "exec@crittersupply.dev",
                firstName: "Eve",
                lastName: "Executive",
                role: BackofficeRole.Executive),

            CreateUser(hasher, devPassword,
                id: Guid.Parse("AA000000-AA00-AA00-AA00-AA0000000003"),
                email: "ops@crittersupply.dev",
                firstName: "Oscar",
                lastName: "Ops",
                role: BackofficeRole.OperationsManager),

            CreateUser(hasher, devPassword,
                id: Guid.Parse("AA000000-AA00-AA00-AA00-AA0000000004"),
                email: "cs@crittersupply.dev",
                firstName: "Clara",
                lastName: "Service",
                role: BackofficeRole.CustomerService),

            CreateUser(hasher, devPassword,
                id: Guid.Parse("AA000000-AA00-AA00-AA00-AA0000000005"),
                email: "warehouse@crittersupply.dev",
                firstName: "Walt",
                lastName: "Warehouse",
                role: BackofficeRole.WarehouseClerk),

            CreateUser(hasher, devPassword,
                id: Guid.Parse("AA000000-AA00-AA00-AA00-AA0000000006"),
                email: "pricing@crittersupply.dev",
                firstName: "Priya",
                lastName: "Pricing",
                role: BackofficeRole.PricingManager),

            CreateUser(hasher, devPassword,
                id: Guid.Parse("AA000000-AA00-AA00-AA00-AA0000000007"),
                email: "copy@crittersupply.dev",
                firstName: "Connor",
                lastName: "Copy",
                role: BackofficeRole.CopyWriter),
        };

        await dbContext.Users.AddRangeAsync(users);
        await dbContext.SaveChangesAsync();
    }

    private static BackofficeUser CreateUser(
        PasswordHasher<BackofficeUser> hasher,
        string password,
        Guid id,
        string email,
        string firstName,
        string lastName,
        BackofficeRole role)
    {
        var user = new BackofficeUser
        {
            Id = id,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            Role = role,
            Status = BackofficeUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        return user;
    }
}
