namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Smoke tests that verify the Marketplaces.Api starts and responds to basic requests.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class HealthCheckTests
{
    private readonly TestFixture _fixture;

    public HealthCheckTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/health");
            s.StatusCodeShouldBeOk();
        });

        var body = result.ReadAsText();
        body.ShouldContain("Healthy");
    }
}
