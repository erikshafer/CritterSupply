using VendorIdentity.Commands;
using Wolverine.Http;

namespace VendorIdentity.Api.Endpoints;

/// <summary>
/// Wolverine HTTP endpoint for inviting vendor users.
/// </summary>
public static class InviteUser
{
    /// <summary>
    /// Invites a new user to a vendor tenant.
    /// </summary>
    /// <param name="tenantId">The vendor tenant ID.</param>
    /// <param name="request">The invitation request.</param>
    /// <returns>201 Created with user ID.</returns>
    [WolverinePost("/api/vendor-identity/tenants/{tenantId}/users/invite")]
    public static InviteVendorUser Post(Guid tenantId, InviteUserRequest request) =>
        new(tenantId, request.Email, request.FirstName, request.LastName, request.Role);
}

/// <summary>
/// Request DTO for user invitation.
/// </summary>
public sealed record InviteUserRequest(
    string Email,
    string FirstName,
    string LastName,
    Messages.Contracts.VendorIdentity.VendorRole Role);

/// <summary>
/// Response DTO for user invitation.
/// </summary>
public sealed record InviteUserResponse(Guid UserId);
