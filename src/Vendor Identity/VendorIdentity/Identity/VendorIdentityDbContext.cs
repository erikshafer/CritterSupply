using Microsoft.EntityFrameworkCore;
using VendorIdentity.TenantManagement;
using VendorIdentity.UserInvitations;

namespace VendorIdentity.Identity;

/// <summary>
/// EF Core DbContext for Vendor Identity bounded context.
/// Manages VendorTenant, VendorUser, and VendorUserInvitation entities with relational mappings.
/// </summary>
public sealed class VendorIdentityDbContext : DbContext
{
    public VendorIdentityDbContext(DbContextOptions<VendorIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<VendorTenant> Tenants => Set<VendorTenant>();
    public DbSet<VendorUser> Users => Set<VendorUser>();
    public DbSet<VendorUserInvitation> Invitations => Set<VendorUserInvitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Use dedicated schema for Vendor Identity BC
        modelBuilder.HasDefaultSchema("vendoridentity");

        // VendorTenant configuration
        modelBuilder.Entity<VendorTenant>(entity =>
        {
            entity.HasKey(t => t.Id);

            entity.Property(t => t.OrganizationName)
                .IsRequired()
                .HasMaxLength(200);

            // Unique constraint: OrganizationName must be unique across all tenants
            entity.HasIndex(t => t.OrganizationName)
                .IsUnique();

            entity.Property(t => t.ContactEmail)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(t => t.Status)
                .IsRequired();

            entity.Property(t => t.OnboardedAt)
                .IsRequired();

            entity.Property(t => t.SuspendedAt);

            entity.Property(t => t.SuspensionReason)
                .HasMaxLength(500);

            entity.Property(t => t.TerminatedAt);

            entity.Property(t => t.TerminationReason)
                .HasMaxLength(500);

            // One-to-many relationship: Tenant -> Users
            entity.HasMany(t => t.Users)
                .WithOne(u => u.VendorTenant)
                .HasForeignKey(u => u.VendorTenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // VendorUser configuration
        modelBuilder.Entity<VendorUser>(entity =>
        {
            entity.HasKey(u => u.Id);

            entity.Property(u => u.VendorTenantId)
                .IsRequired();

            entity.Property(u => u.Email)
                .IsRequired()
                .HasMaxLength(256);

            // Unique constraint: Email must be unique system-wide (not per-tenant)
            entity.HasIndex(u => u.Email)
                .IsUnique();

            entity.Property(u => u.PasswordHash)
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

            entity.Property(u => u.InvitedAt);

            entity.Property(u => u.ActivatedAt);

            entity.Property(u => u.DeactivatedAt);

            entity.Property(u => u.LastLoginAt);

            // Foreign key index for query performance
            entity.HasIndex(u => u.VendorTenantId);

            // One-to-many relationship: User -> Invitations
            entity.HasMany(u => u.Invitations)
                .WithOne(i => i.VendorUser)
                .HasForeignKey(i => i.VendorUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // VendorUserInvitation configuration
        modelBuilder.Entity<VendorUserInvitation>(entity =>
        {
            entity.HasKey(i => i.Id);

            entity.Property(i => i.VendorUserId)
                .IsRequired();

            entity.Property(i => i.VendorTenantId)
                .IsRequired();

            entity.Property(i => i.Token)
                .IsRequired()
                .HasMaxLength(256); // Hashed token (SHA-256)

            entity.Property(i => i.InvitedRole)
                .IsRequired();

            entity.Property(i => i.Status)
                .IsRequired();

            entity.Property(i => i.InvitedAt)
                .IsRequired();

            entity.Property(i => i.ExpiresAt)
                .IsRequired();

            entity.Property(i => i.AcceptedAt);

            entity.Property(i => i.RevokedAt);

            entity.Property(i => i.ResendCount)
                .IsRequired()
                .HasDefaultValue(0);

            // Foreign key index for query performance
            entity.HasIndex(i => i.VendorUserId);

            entity.HasIndex(i => i.VendorTenantId);
        });
    }
}
