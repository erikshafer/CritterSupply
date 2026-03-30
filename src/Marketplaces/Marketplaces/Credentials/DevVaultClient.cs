using Microsoft.Extensions.Configuration;

namespace Marketplaces.Credentials;

/// <summary>
/// Development-only vault client that reads secrets from IConfiguration
/// (e.g. appsettings.json Vault section). Must NOT be used in Production —
/// a startup guard in Program.cs enforces this.
/// </summary>
public sealed class DevVaultClient(IConfiguration configuration) : IVaultClient
{
    public Task<string> GetSecretAsync(string path, CancellationToken ct = default)
    {
        var configKey = $"Vault:{path.Replace('/', ':')}";
        var value = configuration[configKey]
            ?? throw new KeyNotFoundException($"Dev vault secret not found: {path}");
        return Task.FromResult(value);
    }
}
