using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackofficeIdentity.Identity;

/// <summary>
/// Design-time factory for BackofficeIdentityDbContext.
/// Enables EF Core CLI tools (dotnet ef migrations add, dotnet ef database update) to create the DbContext
/// without a running application.
/// </summary>
public sealed class BackofficeIdentityDbContextFactory : IDesignTimeDbContextFactory<BackofficeIdentityDbContext>
{
    public BackofficeIdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BackofficeIdentityDbContext>();

        // Connection string for local development (docker-compose Postgres on port 5433)
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres",
            npgsqlOptions => npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "backofficeidentity"));

        return new BackofficeIdentityDbContext(optionsBuilder.Options);
    }
}
