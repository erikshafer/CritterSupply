using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CustomerIdentity.Authentication;

public sealed record CurrentUserResponse(Guid CustomerId, string Email, string FirstName, string LastName);

public static class GetCurrentUser
{
    /// <summary>
    /// GET /api/auth/me
    /// Returns the currently authenticated user's information.
    /// Returns 401 Unauthorized if not authenticated.
    /// </summary>
    [WolverineGet("/api/auth/me")]
    [Authorize]
    public static IResult Handle(ClaimsPrincipal user)
    {
        var customerIdClaim = user.FindFirst("CustomerId")?.Value;
        var emailClaim = user.FindFirst(ClaimTypes.Email)?.Value;
        var firstNameClaim = user.FindFirst(ClaimTypes.GivenName)?.Value;
        var lastNameClaim = user.FindFirst(ClaimTypes.Surname)?.Value;

        if (customerIdClaim == null || emailClaim == null || firstNameClaim == null || lastNameClaim == null)
            return Results.Unauthorized();

        return Results.Ok(new CurrentUserResponse(
            Guid.Parse(customerIdClaim),
            emailClaim,
            firstNameClaim,
            lastNameClaim
        ));
    }
}
