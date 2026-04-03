using Marketplaces.Credentials;

namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Tests for <see cref="EnvironmentVaultClient"/> — production IVaultClient implementation.
/// These are unit-style tests (no TestFixture/TestContainers needed) since
/// EnvironmentVaultClient reads only from environment variables.
/// </summary>
public sealed class EnvironmentVaultClientTests : IDisposable
{
    private readonly List<string> _envVarsToClean = [];

    [Fact]
    public async Task GetSecretAsync_ReturnsSecret_WhenEnvVarSet()
    {
        // Arrange
        SetEnvVar("VAULT__AMAZON__CLIENT_ID", "test-client-id-12345");
        var client = new EnvironmentVaultClient();

        // Act
        var result = await client.GetSecretAsync("amazon/client-id");

        // Assert
        result.ShouldBe("test-client-id-12345");
    }

    [Fact]
    public async Task GetSecretAsync_Throws_WhenEnvVarMissing()
    {
        // Arrange — ensure the env var does NOT exist
        var envVarName = "VAULT__AMAZON__NONEXISTENT_SECRET";
        Environment.SetEnvironmentVariable(envVarName, null);
        var client = new EnvironmentVaultClient();

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => client.GetSecretAsync("amazon/nonexistent-secret"));

        ex.Message.ShouldContain("VAULT__AMAZON__NONEXISTENT_SECRET");
        ex.Message.ShouldContain("amazon/nonexistent-secret");
    }

    [Fact]
    public async Task GetSecretAsync_Throws_WhenEnvVarIsEmpty()
    {
        // Arrange
        SetEnvVar("VAULT__AMAZON__EMPTY_VAR", "");
        var client = new EnvironmentVaultClient();

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => client.GetSecretAsync("amazon/empty-var"));
    }

    [Theory]
    [InlineData("amazon/client-id", "VAULT__AMAZON__CLIENT_ID")]
    [InlineData("amazon/client-secret", "VAULT__AMAZON__CLIENT_SECRET")]
    [InlineData("amazon/refresh-token", "VAULT__AMAZON__REFRESH_TOKEN")]
    [InlineData("amazon/marketplace-id", "VAULT__AMAZON__MARKETPLACE_ID")]
    [InlineData("amazon/seller-id", "VAULT__AMAZON__SELLER_ID")]
    [InlineData("walmart/client-id", "VAULT__WALMART__CLIENT_ID")]
    [InlineData("ebay/client-id", "VAULT__EBAY__CLIENT_ID")]
    public void PathToEnvironmentVariable_FollowsAdr0051Convention(
        string vaultPath, string expectedEnvVar)
    {
        // Verify the path-to-env-var mapping matches ADR 0051
        var result = EnvironmentVaultClient.PathToEnvironmentVariable(vaultPath);
        result.ShouldBe(expectedEnvVar);
    }

    [Fact]
    public async Task GetSecretAsync_Throws_WhenPathIsNull()
    {
        var client = new EnvironmentVaultClient();

        await Should.ThrowAsync<ArgumentException>(
            () => client.GetSecretAsync(null!));
    }

    [Fact]
    public async Task GetSecretAsync_Throws_WhenPathIsWhitespace()
    {
        var client = new EnvironmentVaultClient();

        await Should.ThrowAsync<ArgumentException>(
            () => client.GetSecretAsync("   "));
    }

    private void SetEnvVar(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value);
        _envVarsToClean.Add(name);
    }

    public void Dispose()
    {
        foreach (var envVar in _envVarsToClean)
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }
}
