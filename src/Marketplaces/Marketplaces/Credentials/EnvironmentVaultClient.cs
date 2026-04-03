namespace Marketplaces.Credentials;

/// <summary>
/// Production <see cref="IVaultClient"/> implementation that reads secrets from environment variables.
/// <para>
/// Path-to-environment-variable mapping convention (per ADR 0051):
/// <list type="bullet">
/// <item>Replace <c>/</c> with <c>__</c> (double underscore)</item>
/// <item>Replace <c>-</c> with <c>_</c></item>
/// <item>Convert to UPPERCASE</item>
/// <item>Prefix with <c>VAULT__</c></item>
/// </list>
/// Example: <c>amazon/client-id</c> → <c>VAULT__AMAZON__CLIENT_ID</c>
/// </para>
/// </summary>
public sealed class EnvironmentVaultClient : IVaultClient
{
    public Task<string> GetSecretAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var envVarName = PathToEnvironmentVariable(path);
        var value = Environment.GetEnvironmentVariable(envVarName);

        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException(
                $"Vault secret not found: environment variable '{envVarName}' is not set. " +
                $"Requested path: '{path}'. " +
                $"Set the environment variable or switch to DevVaultClient for local development.");

        return Task.FromResult(value);
    }

    /// <summary>
    /// Converts a vault path to the corresponding environment variable name.
    /// Visible for testing to verify the convention matches ADR 0051.
    /// </summary>
    internal static string PathToEnvironmentVariable(string path) =>
        "VAULT__" + path.Replace("/", "__").Replace("-", "_").ToUpperInvariant();
}
