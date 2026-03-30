namespace Marketplaces.Credentials;

/// <summary>
/// Abstraction for retrieving secrets from a vault.
/// Production implementations connect to HashiCorp Vault, AWS Secrets Manager, etc.
/// Development uses <see cref="DevVaultClient"/> which reads from IConfiguration.
/// </summary>
public interface IVaultClient
{
    Task<string> GetSecretAsync(string path, CancellationToken ct = default);
}
