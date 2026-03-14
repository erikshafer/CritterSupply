using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AdminIdentity.Identity;

/// <summary>
/// Design-time factory for AdminIdentityDbContext.
/// Enables EF Core CLI tools (dotnet ef migrations add, dotnet ef database update) to create the DbContext
/// without a running application.
/// </summary>
public sealed class AdminIdentityDbContextFactory : IDesignTimeDbContextFactory<AdminIdentityDbContext>
{
    public AdminIdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AdminIdentityDbContext>();

        // Connection string for local development (docker-compose Postgres on port 5433)
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres",
            npgsqlOptions => npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "adminidentity"));

        return new AdminIdentityDbContext(optionsBuilder.Options);
    }
}
