using System.Security.Claims;
using CustomerIdentity.AddressBook;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wolverine.Http;

namespace CustomerIdentity.Authentication;

/// <summary>
/// Login request for email + password authentication.
/// Dev mode: Password validation is lenient (just checks email exists).
/// </summary>
public sealed record LoginRequest(string Email, string? Password = null);

public sealed record LoginResponse(Guid CustomerId, string Email, string FirstName, string LastName);

public static class Login
{
    /// <summary>
    /// POST /api/auth/login
    /// Authenticates user and creates session cookie.
    ///
    /// Dev mode behavior:
    /// - Accepts any password (or no password) if email exists
    /// - Future upgrade: Add password hash validation (bcrypt, ASP.NET Core Identity)
    /// </summary>
    [WolverinePost("/api/auth/login")]
    public static async Task<IResult> Handle(LoginRequest request, CustomerIdentityDbContext db, HttpContext httpContext)
    {
        // Look up customer by email
        var customer = await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Email == request.Email);

        if (customer == null)
            return Results.Unauthorized();

        // Dev mode: Skip password validation (accept any password or empty password)
        // Future upgrade path:
        // if (!PasswordHasher.Verify(request.Password, customer.PasswordHash))
        //     return Results.Unauthorized();

        // Create claims for session
        var claims = new[]
        {
            new Claim("CustomerId", customer.Id.ToString()),
            new Claim(ClaimTypes.Email, customer.Email),
            new Claim(ClaimTypes.Name, $"{customer.FirstName} {customer.LastName}"),
            new Claim(ClaimTypes.GivenName, customer.FirstName),
            new Claim(ClaimTypes.Surname, customer.LastName)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        // Sign in (creates session cookie)
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return Results.Ok(new LoginResponse(customer.Id, customer.Email, customer.FirstName, customer.LastName));
    }
}
