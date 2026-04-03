using System.Net;
using Storefront.Clients;

namespace Storefront.Api.IntegrationTests;

/// <summary>
/// Integration tests for the GetOrderHistory BFF endpoint.
/// Verifies the BFF correctly proxies order listing from Orders BC.
/// </summary>
[Collection("Storefront Integration Tests")]
public class OrderHistoryTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public OrderHistoryTests(TestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ClearAllStubs();
    }

    [Fact]
    public async Task GetOrderHistory_ReturnsOrdersForCustomer()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        _fixture.StubOrdersClient.AddOrderSummary(new OrderSummaryDto(
            Guid.NewGuid(), customerId, DateTimeOffset.UtcNow.AddDays(-1),
            "Placed", 99.97m, 3));
        _fixture.StubOrdersClient.AddOrderSummary(new OrderSummaryDto(
            Guid.NewGuid(), customerId, DateTimeOffset.UtcNow,
            "Shipped", 45.00m, 1));

        // Act
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/orders?customerId={customerId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // Assert
        var orders = await result.ReadAsJsonAsync<List<OrderSummaryDto>>();
        orders.ShouldNotBeNull();
        orders.Count.ShouldBe(2);
        orders.ShouldContain(o => o.Status == "Placed");
        orders.ShouldContain(o => o.Status == "Shipped");
    }

    [Fact]
    public async Task GetOrderHistory_WhenNoOrders_ReturnsEmptyList()
    {
        // Arrange — no orders configured for this customer
        var customerId = Guid.NewGuid();

        // Act
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/orders?customerId={customerId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // Assert
        var orders = await result.ReadAsJsonAsync<List<OrderSummaryDto>>();
        orders.ShouldNotBeNull();
        orders.ShouldBeEmpty();
    }
}
