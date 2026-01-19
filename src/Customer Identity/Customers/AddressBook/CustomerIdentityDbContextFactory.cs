using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace CustomerIdentity.AddressBook;

/// <summary>
/// Design-time factory for CustomerIdentityDbContext.
/// Used by EF Core migrations tooling.
/// </summary>
public sealed class CustomerIdentityDbContextFactory : IDesignTimeDbContextFactory<CustomerIdentityDbContext>
{
    public CustomerIdentityDbContext CreateDbContext(string[] args)
    {
        // Build configuration to read from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("postgres")
                               ?? throw new InvalidOperationException("Connection string 'postgres' not found in appsettings.json");

        var optionsBuilder = new DbContextOptionsBuilder<CustomerIdentityDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new CustomerIdentityDbContext(optionsBuilder.Options);
    }
}
