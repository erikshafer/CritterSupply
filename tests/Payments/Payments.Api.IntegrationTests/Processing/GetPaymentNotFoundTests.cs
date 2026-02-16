using System.Net;

namespace Payments.Api.IntegrationTests.Processing;

/// <summary>
/// Unit tests for payment query not-found scenarios.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class GetPaymentNotFoundTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public GetPaymentNotFoundTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Test querying non-existent payment returns 404.
    /// **Validates: Requirements 8.2**
    /// </summary>
    [Fact]
    public async Task Get_NonExistent_Payment_Returns_NotFound()
    {
        // Arrange: Use a random GUID that doesn't exist
        var nonExistentPaymentId = Guid.NewGuid();

        // Act: Query the payment via HTTP endpoint
        var response = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/payments/{nonExistentPaymentId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });

        // Assert: Response should be 404 Not Found
        response.Context.Response.StatusCode.ShouldBe((int)HttpStatusCode.NotFound);
    }
}
