using System.Text.RegularExpressions;

namespace BackofficeIdentity.Identity;

/// <summary>
/// Extension methods for BackofficeRole enum.
/// Converts enum values to kebab-case role names for JWT claims and authorization policies.
/// </summary>
public static partial class BackofficeRoleExtensions
{
    /// <summary>
    /// Converts BackofficeRole enum to kebab-case string for JWT role claims.
    /// Example: BackofficeRole.SystemAdmin → "system-admin"
    /// </summary>
    public static string ToRoleString(this BackofficeRole role)
    {
        return PascalToKebabCaseRegex().Replace(role.ToString(), "-$1").Trim('-').ToLowerInvariant();
    }

    [GeneratedRegex("([A-Z])")]
    private static partial Regex PascalToKebabCaseRegex();
}
