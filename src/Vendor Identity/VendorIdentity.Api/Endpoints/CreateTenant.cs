using VendorIdentity.Commands;
using Wolverine.Http;

namespace VendorIdentity.Api.Endpoints;

/// <summary>
/// Wolverine HTTP endpoint for creating vendor tenants.
/// </summary>
public static class CreateTenant
{
    /// <summary>
    /// Creates a new vendor tenant organization.
    /// </summary>
    /// <param name="command">The create tenant command.</param>
    /// <returns>The command for Wolverine to process.</returns>
    [WolverinePost("/api/vendor-identity/tenants")]
    public static CreateVendorTenant Post(CreateVendorTenant command) => command;
}

/// <summary>
/// Response DTO for tenant creation.
/// </summary>
public sealed record CreateTenantResponse(Guid TenantId);
