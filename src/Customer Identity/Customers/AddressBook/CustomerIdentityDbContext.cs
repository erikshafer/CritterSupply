using Microsoft.EntityFrameworkCore;

namespace CustomerIdentity.AddressBook;

/// <summary>
/// EF Core DbContext for Customer Identity bounded context.
/// Manages Customer and CustomerAddress entities with relational mappings.
/// </summary>
public sealed class CustomerIdentityDbContext : DbContext
{
    public CustomerIdentityDbContext(DbContextOptions<CustomerIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> Addresses => Set<CustomerAddress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Use dedicated schema for Customer Identity BC
        modelBuilder.HasDefaultSchema("customeridentity");

        // Customer configuration
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Email)
                .IsRequired()
                .HasMaxLength(256);

            entity.HasIndex(c => c.Email)
                .IsUnique();

            entity.Property(c => c.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(c => c.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(c => c.CreatedAt)
                .IsRequired();

            // One-to-many relationship: Customer -> Addresses
            entity.HasMany(c => c.Addresses)
                .WithOne(a => a.Customer)
                .HasForeignKey(a => a.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // CustomerAddress configuration
        modelBuilder.Entity<CustomerAddress>(entity =>
        {
            entity.HasKey(a => a.Id);

            entity.Property(a => a.CustomerId)
                .IsRequired();

            entity.Property(a => a.Type)
                .IsRequired();

            entity.Property(a => a.Nickname)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(a => a.AddressLine1)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(a => a.AddressLine2)
                .HasMaxLength(200);

            entity.Property(a => a.City)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(a => a.StateOrProvince)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(a => a.PostalCode)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(a => a.Country)
                .IsRequired()
                .HasMaxLength(2); // ISO 3166-1 alpha-2

            entity.Property(a => a.IsDefault)
                .IsRequired();

            entity.Property(a => a.IsVerified)
                .IsRequired();

            entity.Property(a => a.CreatedAt)
                .IsRequired();

            entity.Property(a => a.LastUsedAt);

            // Unique constraint: Customer can't have duplicate nicknames
            entity.HasIndex(a => new { a.CustomerId, a.Nickname })
                .IsUnique();

            // Foreign key index for query performance
            entity.HasIndex(a => a.CustomerId);
        });
    }
}
