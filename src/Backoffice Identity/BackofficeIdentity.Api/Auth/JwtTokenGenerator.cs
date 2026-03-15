using BackofficeIdentity.Authentication;
using BackofficeIdentity.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BackofficeIdentity.Api.Auth;

/// <summary>
/// JWT token generator for Backoffice Identity BC.
/// Generates access tokens with backoffice user claims (sub, role, email, name).
/// Configuration values injected via IConfiguration.
/// </summary>
public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly string _secretKey;
    private readonly int _expiryMinutes;

    public JwtTokenGenerator(IConfiguration configuration)
    {
        _issuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer not configured");
        _audience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience not configured");
        _secretKey = configuration["Jwt:SecretKey"] ?? throw new InvalidOperationException("Jwt:SecretKey not configured");
        _expiryMinutes = int.Parse(configuration["Jwt:ExpiryMinutes"] ?? "15");
    }

    public string GenerateAccessToken(BackofficeUser user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, $"{user.FirstName} {user.LastName}"),
            new Claim(ClaimTypes.Role, user.Role.ToString()), // "role" claim for RequireRole() policies
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
