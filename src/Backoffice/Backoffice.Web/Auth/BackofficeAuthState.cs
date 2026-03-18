namespace Backoffice.Web.Auth;

/// <summary>
/// Represents authenticated admin user state in WASM memory.
/// JWT access token stored in memory — never in localStorage (XSS risk).
/// </summary>
public sealed class BackofficeAuthState
{
    public bool IsAuthenticated { get; private set; }
    public string? AccessToken { get; private set; }
    public string? Email { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? Role { get; private set; }
    public Guid AdminUserId { get; private set; }
    public DateTimeOffset TokenExpiresAt { get; private set; }

    public event Action? OnChange;

    public void SetAuthenticated(
        string accessToken,
        string email,
        string firstName,
        string lastName,
        string role,
        Guid adminUserId,
        DateTimeOffset expiresAt)
    {
        AccessToken = accessToken;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        Role = role;
        AdminUserId = adminUserId;
        TokenExpiresAt = expiresAt;
        IsAuthenticated = true;
        OnChange?.Invoke();
    }

    public void UpdateAccessToken(string newAccessToken, DateTimeOffset expiresAt)
    {
        AccessToken = newAccessToken;
        TokenExpiresAt = expiresAt;
        OnChange?.Invoke();
    }

    public void ClearAuthentication()
    {
        AccessToken = null;
        Email = null;
        FirstName = null;
        LastName = null;
        Role = null;
        AdminUserId = Guid.Empty;
        TokenExpiresAt = default;
        IsAuthenticated = false;
        OnChange?.Invoke();
    }

    // Role-based permissions (ADR 0031: Backoffice RBAC Model)
    // Role values are kebab-case to match JWT claims from BackofficeIdentity.Api (e.g. "system-admin")
    public bool IsSystemAdmin => Role == "system-admin";
    public bool IsExecutive => Role == "executive";
    public bool IsOperationsManager => Role == "operations-manager";
    public bool IsCustomerService => Role == "customer-service";
    public bool IsPricingManager => Role == "pricing-manager";
    public bool IsCopyWriter => Role == "copy-writer";
    public bool IsWarehouseClerk => Role == "warehouse-clerk";
    public bool IsFinanceClerk => Role == "finance-clerk";

    /// <summary>Human-readable display name for the role.</summary>
    public string RoleDisplayName => Role switch
    {
        "system-admin" => "System Admin",
        "executive" => "Executive",
        "operations-manager" => "Operations Manager",
        "customer-service" => "Customer Service",
        "pricing-manager" => "Pricing Manager",
        "copy-writer" => "Copywriter",
        "warehouse-clerk" => "Warehouse Clerk",
        "finance-clerk" => "Finance Clerk",
        _ => Role ?? "Unknown"
    };
}
