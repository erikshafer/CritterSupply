using Microsoft.Extensions.DependencyInjection;

namespace Shopping.Api.IntegrationTests.Cart;

/// <summary>
/// Diagnostic tests to verify test fixture DI configuration.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class DiagnosticTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public DiagnosticTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.CleanAllDocumentsAsync();
        _fixture.StubPricingClient.Clear();
        _fixture.StubPromotionsClient.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void PromotionsClient_ShouldBeStubImplementation()
    {
        // Act
        var clientType = _fixture.GetPromotionsClientType();

        // Assert
        Assert.Contains("StubPromotionsClient", clientType);
    }

    [Fact]
    public async Task StubPromotionsClient_InvalidCoupon_ReturnsIsValidFalse()
    {
        // Arrange
        _fixture.StubPromotionsClient.SetInvalidCoupon("TEST123", "Test invalid coupon");

        // Act - Call the stub directly
        var validation = await _fixture.StubPromotionsClient.ValidateCouponAsync("TEST123");

        // Assert
        Assert.False(validation.IsValid);
        Assert.Equal("Test invalid coupon", validation.Reason);
    }

    [Fact]
    public async Task ResolvedPromotionsClient_InvalidCoupon_ReturnsIsValidFalse()
    {
        // Arrange - Configure the fixture's stub
        _fixture.StubPromotionsClient.SetInvalidCoupon("TEST456", "Test invalid via DI");

        // Act - Resolve from DI container and call it
        using var scope = _fixture.Host.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<Shopping.Clients.IPromotionsClient>();
        var validation = await client.ValidateCouponAsync("TEST456");

        // Assert - If this fails, it means a different instance is being used
        Assert.False(validation.IsValid);
        Assert.Equal("Test invalid via DI", validation.Reason);
    }
}