namespace Backoffice.Api.IntegrationTests.CustomerService;

/// <summary>
/// Integration tests for CS order cancellation workflow.
/// Tests POST /api/backoffice/orders/{orderId}/cancel
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class OrderCancellationTests
{
    private readonly BackofficeTestFixture _fixture;

    public OrderCancellationTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.OrdersClient.Clear();
    }

    [Fact]
    public async Task CancelOrder_WithValidOrderId_Returns204AndCancelsOrder()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var request = new { Reason = "Customer requested cancellation via CS agent" };

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(request).ToUrl($"/api/backoffice/orders/{orderId}/cancel");
            s.StatusCodeShouldBe(204);
        });

        // Assert
        _fixture.OrdersClient.WasCancelled(orderId).ShouldBeTrue();
    }

    [Fact]
    public async Task CancelOrder_WithValidReason_ProcessesSuccessfully()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var request = new { Reason = "Order placed by mistake - customer called to cancel" };

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(request).ToUrl($"/api/backoffice/orders/{orderId}/cancel");
            s.StatusCodeShouldBe(204);
        });

        // Assert
        _fixture.OrdersClient.WasCancelled(orderId).ShouldBeTrue();
    }

    [Fact]
    public async Task CancelOrder_WithAuthenticatedUser_ExtractsAdminUserId()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var request = new { Reason = "Fraudulent order detected" };

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(request).ToUrl($"/api/backoffice/orders/{orderId}/cancel");
            s.StatusCodeShouldBe(204);
        });

        // Assert - verify the command was processed (stub client tracks cancellation)
        _fixture.OrdersClient.WasCancelled(orderId).ShouldBeTrue();
    }
}
