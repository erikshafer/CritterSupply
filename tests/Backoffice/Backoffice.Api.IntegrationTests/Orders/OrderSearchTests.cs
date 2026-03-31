using Backoffice.Clients;

namespace Backoffice.Api.IntegrationTests.Orders;

/// <summary>
/// Integration tests for order search endpoint.
/// Tests GET /api/backoffice/orders/search?query={query}
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class OrderSearchTests
{
    private readonly BackofficeTestFixture _fixture;

    public OrderSearchTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.OrdersClient.Clear();
    }

    [Fact]
    public async Task SearchOrders_WithValidGuid_ReturnsMatchingOrders()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var order = new OrderSummaryDto(
            orderId,
            Guid.NewGuid(),
            DateTime.UtcNow,
            "Confirmed",
            149.99m);
        _fixture.OrdersClient.AddOrder(order);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/orders/search?query={orderId}");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var response = await result.ReadAsJsonAsync<SearchOrdersResultDto>();
        response.ShouldNotBeNull();
        response.Query.ShouldBe(orderId.ToString());
        response.TotalCount.ShouldBe(1);
        response.Orders.Count.ShouldBe(1);
        response.Orders[0].Id.ShouldBe(orderId);
    }

    [Fact]
    public async Task SearchOrders_WithInvalidGuid_ReturnsEmptyResults()
    {
        // Arrange
        var nonExistentOrderId = Guid.NewGuid();

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/orders/search?query={nonExistentOrderId}");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var response = await result.ReadAsJsonAsync<SearchOrdersResultDto>();
        response.ShouldNotBeNull();
        response.Query.ShouldBe(nonExistentOrderId.ToString());
        response.TotalCount.ShouldBe(0);
        response.Orders.Count.ShouldBe(0);
    }

    [Fact]
    public async Task SearchOrders_WithNonGuidQuery_ReturnsEmptyResults()
    {
        // Arrange
        var invalidQuery = "not-a-guid";

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/orders/search?query={invalidQuery}");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var response = await result.ReadAsJsonAsync<SearchOrdersResultDto>();
        response.ShouldNotBeNull();
        response.Query.ShouldBe(invalidQuery);
        response.TotalCount.ShouldBe(0);
        response.Orders.Count.ShouldBe(0);
    }

    [Fact]
    public async Task SearchOrders_WithMultipleOrdersAndMatchingGuid_ReturnsOnlyMatchingOrder()
    {
        // Arrange
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var orderId3 = Guid.NewGuid();

        _fixture.OrdersClient.AddOrder(new OrderSummaryDto(
            orderId1, Guid.NewGuid(), DateTime.UtcNow, "Pending", 99.99m));
        _fixture.OrdersClient.AddOrder(new OrderSummaryDto(
            orderId2, Guid.NewGuid(), DateTime.UtcNow, "Confirmed", 149.99m));
        _fixture.OrdersClient.AddOrder(new OrderSummaryDto(
            orderId3, Guid.NewGuid(), DateTime.UtcNow, "Shipped", 199.99m));

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/orders/search?query={orderId2}");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var response = await result.ReadAsJsonAsync<SearchOrdersResultDto>();
        response.ShouldNotBeNull();
        response.TotalCount.ShouldBe(1);
        response.Orders[0].Id.ShouldBe(orderId2);
        response.Orders[0].Status.ShouldBe("Confirmed");
    }
}
