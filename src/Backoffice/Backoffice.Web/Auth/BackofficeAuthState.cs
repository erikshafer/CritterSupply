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
    public bool IsSystemAdmin => Role == "SystemAdmin";
    public bool IsExecutive => Role == "Executive";
    public bool IsOperationsManager => Role == "OperationsManager";
    public bool IsCustomerService => Role == "CustomerService";
    public bool IsPricingManager => Role == "PricingManager";
    public bool IsCopyWriter => Role == "CopyWriter";
    public bool IsWarehouseClerk => Role == "WarehouseClerk";
    public bool IsFinanceClerk => Role == "FinanceClerk";

    /// <summary>Human-readable display name for the role.</summary>
    public string RoleDisplayName => Role switch
    {
        "SystemAdmin" => "System Admin",
        "Executive" => "Executive",
        "OperationsManager" => "Operations Manager",
        "CustomerService" => "Customer Service",
        "PricingManager" => "Pricing Manager",
        "CopyWriter" => "Copywriter",
        "WarehouseClerk" => "Warehouse Clerk",
        "FinanceClerk" => "Finance Clerk",
        _ => Role ?? "Unknown"
    };
}
