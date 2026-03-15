namespace BackofficeIdentity.Identity;

/// <summary>
/// Represents an internal administrative user with a single role.
/// Backoffice users have access to the Backoffice with permissions determined by their Role.
/// </summary>
public sealed class BackofficeUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;

    /// <summary>
    /// The single role assigned to this backoffice user.
    /// Phase 1 constraint: One role per user.
    /// Valid values: CopyWriter, PricingManager, WarehouseClerk, CustomerService, OperationsManager, Executive, SystemAdmin
    /// </summary>
    public BackofficeRole Role { get; set; }

    public BackofficeUserStatus Status { get; set; }
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
/// Backoffice user roles aligned with ADR 0031.
/// Each role has specific permissions defined in Backoffice authorization policies.
/// </summary>
public enum BackofficeRole
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
/// Backoffice user account status lifecycle.
/// </summary>
public enum BackofficeUserStatus
{
    Active = 1,
    Deactivated = 2
}
