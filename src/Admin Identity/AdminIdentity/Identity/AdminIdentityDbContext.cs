using Microsoft.EntityFrameworkCore;

namespace AdminIdentity.Identity;

/// <summary>
/// EF Core DbContext for Admin Identity bounded context.
/// Manages AdminUser entities for internal employee authentication and authorization.
/// Schema: adminidentity
/// </summary>
public sealed class AdminIdentityDbContext : DbContext
{
    public AdminIdentityDbContext(DbContextOptions<AdminIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<AdminUser> Users => Set<AdminUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Use dedicated schema for Admin Identity BC
        modelBuilder.HasDefaultSchema("adminidentity");

        // AdminUser configuration
        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Email)
                .IsRequired()
                .HasMaxLength(256);

            // Unique constraint: Email must be unique system-wide
            entity.HasIndex(u => u.Email)
                .IsUnique();

            entity.Property(u => u.PasswordHash)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(u => u.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(u => u.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(u => u.Role)
                .IsRequired();

            entity.Property(u => u.Status)
                .IsRequired();

            entity.Property(u => u.CreatedAt)
                .IsRequired();

            entity.Property(u => u.LastLoginAt);

            entity.Property(u => u.DeactivatedAt);

            entity.Property(u => u.DeactivationReason)
                .HasMaxLength(500);

            entity.Property(u => u.RefreshToken)
                .HasMaxLength(256);

            entity.Property(u => u.RefreshTokenExpiresAt);

            // Index for refresh token lookups
            entity.HasIndex(u => u.RefreshToken)
                .IsUnique()
                .HasFilter($"\"RefreshToken\" IS NOT NULL");
        });
    }
}
