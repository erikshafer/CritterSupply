using System.Net;
using System.Net.Http.Json;
using Orders.Api.Placement;
using Messages.Contracts.Shopping;

namespace Orders.Api.IntegrationTests.Placement;

[Collection(IntegrationTestCollection.Name)]
public class ListOrdersTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ListOrdersTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ListOrders_ReturnsOrdersForCustomer()
    {
        // Arrange - Create multiple orders for same customer
        var customerId = Guid.CreateVersion7();
        var otherCustomerId = Guid.CreateVersion7();

        var order1 = TestFixture.CreateCheckoutCompletedMessage(customerId);
        var order2 = TestFixture.CreateCheckoutCompletedMessage(customerId);
        var order3 = TestFixture.CreateCheckoutCompletedMessage(otherCustomerId);

        await _fixture.ExecuteAndWaitAsync(order1);
        await _fixture.ExecuteAndWaitAsync(order2);
        await _fixture.ExecuteAndWaitAsync(order3);

        // Act
        var response = await _fixture.Host.Scenario(cfg =>
        {
            cfg.Get.Url($"/api/orders?customerId={customerId}");
            cfg.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var result = await response.ReadAsJsonAsync<List<OrderSummaryResponse>>();

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);
        result.ShouldAllBe(o => o.CustomerId == customerId);

        // Should be ordered by PlacedAt descending (most recent first)
        result[0].PlacedAt.ShouldBeGreaterThanOrEqualTo(result[1].PlacedAt);
    }

    [Fact]
    public async Task ListOrders_NoOrders_ReturnsEmptyList()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();

        // Act
        var response = await _fixture.Host.Scenario(cfg =>
        {
            cfg.Get.Url($"/api/orders?customerId={customerId}");
            cfg.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var result = await response.ReadAsJsonAsync<List<OrderSummaryResponse>>();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListOrders_MissingCustomerId_ReturnsBadRequest()
    {
        // Act & Assert
        var response = await _fixture.Host.Scenario(cfg =>
        {
            cfg.Get.Url("/api/orders");
            cfg.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });

        // BadRequest returns error response - no need to further inspect it
    }

    [Fact]
    public async Task ListOrders_IncludesOrderSummaryFields()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();
        var message = TestFixture.CreateCheckoutCompletedMessage(customerId);
        await _fixture.ExecuteAndWaitAsync(message);

        // Act
        var response = await _fixture.Host.Scenario(cfg =>
        {
            cfg.Get.Url($"/api/orders?customerId={customerId}");
            cfg.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var result = await response.ReadAsJsonAsync<List<OrderSummaryResponse>>();

        // Assert
        result.ShouldNotBeNull();
        var order = result.ShouldHaveSingleItem();
        order.OrderId.ShouldNotBe(Guid.Empty);
        order.CustomerId.ShouldBe(customerId);
        order.PlacedAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
        order.Status.ShouldBe("Placed");
        order.TotalAmount.ShouldBeGreaterThan(0m);
        order.ItemCount.ShouldBeGreaterThan(0);
    }
}
