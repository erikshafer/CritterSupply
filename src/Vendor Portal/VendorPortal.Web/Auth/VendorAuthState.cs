namespace VendorPortal.Web.Auth;

/// <summary>
/// Represents authenticated vendor user state in WASM memory.
/// JWT access token stored in memory — never in localStorage (XSS risk).
/// </summary>
public sealed class VendorAuthState
{
    public bool IsAuthenticated { get; private set; }
    public string? AccessToken { get; private set; }
    public string? Email { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? Role { get; private set; }
    public string? TenantName { get; private set; }
    public Guid VendorTenantId { get; private set; }
    public Guid VendorUserId { get; private set; }
    public DateTimeOffset TokenExpiresAt { get; private set; }

    public event Action? OnChange;

    public void SetAuthenticated(
        string accessToken,
        string email,
        string firstName,
        string lastName,
        string role,
        string tenantName,
        Guid vendorTenantId,
        Guid vendorUserId,
        DateTimeOffset expiresAt)
    {
        AccessToken = accessToken;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        Role = role;
        TenantName = tenantName;
        VendorTenantId = vendorTenantId;
        VendorUserId = vendorUserId;
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
        TenantName = null;
        VendorTenantId = Guid.Empty;
        VendorUserId = Guid.Empty;
        TokenExpiresAt = default;
        IsAuthenticated = false;
        OnChange?.Invoke();
    }

    public bool IsAdmin => Role == "Admin";
    public bool IsCatalogManager => Role is "Admin" or "CatalogManager";
    public bool CanSubmitChangeRequests => Role is "Admin" or "CatalogManager";
    public bool CanManageUsers => Role == "Admin";

    /// <summary>Human-readable display name for the role (not a raw enum identifier).</summary>
    public string RoleDisplayName => Role switch
    {
        "Admin" => "Admin",
        "CatalogManager" => "Catalog Manager",
        "ReadOnly" => "Read Only",
        _ => Role ?? "Unknown"
    };
}
