using Backoffice.Clients;
using Backoffice.Composition;

namespace Backoffice.Api.IntegrationTests.CustomerService;

/// <summary>
/// Integration tests for CS order detail workflow.
/// Tests GET /api/backoffice/orders/{orderId}
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class OrderDetailTests
{
    private readonly BackofficeTestFixture _fixture;

    public OrderDetailTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.CustomerIdentityClient.Clear();
        _fixture.OrdersClient.Clear();
    }

    [Fact]
    public async Task GetOrderDetailView_WithValidOrderId_ReturnsFullOrderDetails()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var customer = new CustomerDto(
            customerId,
            "customer@example.com",
            "Test",
            "Customer",
            null,
            DateTime.UtcNow.AddMonths(-1));

        _fixture.CustomerIdentityClient.AddCustomer(customer);

        var orderDetail = new OrderDetailDto(
            orderId,
            customerId,
            DateTime.UtcNow.AddDays(-5),
            "Confirmed",
            199.98m,
            new List<OrderLineItemDto>
            {
                new("SKU-001", "Premium Cat Food", 2, 49.99m),
                new("SKU-002", "Cat Toy Bundle", 1, 100.00m)
            },
            null);

        _fixture.OrdersClient.AddOrderDetail(orderDetail);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/orders/{orderId}");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var view = result.ReadAsJson<OrderDetailView>();
        view.ShouldNotBeNull();
        view.OrderId.ShouldBe(orderId);
        view.CustomerId.ShouldBe(customerId);
        view.CustomerEmail.ShouldBe("customer@example.com");
        view.Status.ShouldBe("Confirmed");
        view.TotalAmount.ShouldBe(199.98m);
        view.Items.Count.ShouldBe(2);
        view.Items[0].Sku.ShouldBe("SKU-001");
        view.Items[0].ProductName.ShouldBe("Premium Cat Food");
        view.Items[0].Quantity.ShouldBe(2);
        view.Items[0].UnitPrice.ShouldBe(49.99m);
        view.Items[0].LineTotal.ShouldBe(99.98m);
    }

    [Fact]
    public async Task GetOrderDetailView_WithNonExistentOrderId_Returns404()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/orders/{orderId}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task GetOrderDetailView_WithCancelledOrder_IncludesCancellationReason()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var customer = new CustomerDto(
            customerId,
            "customer@example.com",
            "Test",
            "Customer",
            null,
            DateTime.UtcNow);

        _fixture.CustomerIdentityClient.AddCustomer(customer);

        var orderDetail = new OrderDetailDto(
            orderId,
            customerId,
            DateTime.UtcNow.AddHours(-2),
            "Cancelled",
            50.00m,
            new List<OrderLineItemDto>
            {
                new("SKU-003", "Dog Treats", 1, 50.00m)
            },
            "Customer requested cancellation - duplicate order");

        _fixture.OrdersClient.AddOrderDetail(orderDetail);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/orders/{orderId}");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var view = result.ReadAsJson<OrderDetailView>();
        view.ShouldNotBeNull();
        view.Status.ShouldBe("Cancelled");
        view.CancellationReason.ShouldBe("Customer requested cancellation - duplicate order");
    }
}
