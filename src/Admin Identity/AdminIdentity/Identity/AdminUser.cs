namespace AdminIdentity.Identity;

/// <summary>
/// Represents an internal administrative user with a single role.
/// Admin users have access to the Admin Portal with permissions determined by their Role.
/// </summary>
public sealed class AdminUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;

    /// <summary>
    /// The single role assigned to this admin user.
    /// Phase 1 constraint: One role per user.
    /// Valid values: CopyWriter, PricingManager, WarehouseClerk, CustomerService, OperationsManager, Executive, SystemAdmin
    /// </summary>
    public AdminRole Role { get; set; }

    public AdminUserStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public string? DeactivationReason { get; set; }

    /// <summary>
    /// Refresh token for JWT authentication (nullable - set on first login).
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Expiry timestamp for the refresh token.
    /// </summary>
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
}

/// <summary>
/// Admin user roles aligned with ADR 0031.
/// Each role has specific permissions defined in Admin Portal authorization policies.
/// </summary>
public enum AdminRole
{
    CopyWriter = 1,
    PricingManager = 2,
    WarehouseClerk = 3,
    CustomerService = 4,
    OperationsManager = 5,
    Executive = 6,
    SystemAdmin = 7
}

/// <summary>
/// Admin user account status lifecycle.
/// </summary>
public enum AdminUserStatus
{
    Active = 1,
    Deactivated = 2
}
