using Backoffice.Clients;
using Backoffice.Composition;

namespace Backoffice.Api.IntegrationTests.CustomerService;

/// <summary>
/// Integration tests for CS customer search workflow.
/// Tests GET /api/backoffice/customers/search?email={email}
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CustomerSearchTests
{
    private readonly BackofficeTestFixture _fixture;

    public CustomerSearchTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.CustomerIdentityClient.Clear();
        _fixture.OrdersClient.Clear();
    }

    [Fact]
    public async Task GetCustomerServiceView_WithValidEmail_ReturnsCustomerAndOrders()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new CustomerDto(
            customerId,
            "john.doe@example.com",
            "John",
            "Doe",
            null,
            DateTime.UtcNow.AddMonths(-6));

        _fixture.CustomerIdentityClient.AddCustomer(customer);

        var order1 = new OrderSummaryDto(
            Guid.NewGuid(),
            customerId,
            DateTime.UtcNow.AddDays(-10),
            "Confirmed",
            125.50m);

        var order2 = new OrderSummaryDto(
            Guid.NewGuid(),
            customerId,
            DateTime.UtcNow.AddDays(-30),
            "Delivered",
            89.99m);

        _fixture.OrdersClient.AddOrder(order1);
        _fixture.OrdersClient.AddOrder(order2);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/customers/search?email={customer.Email}");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var view = result.ReadAsJson<CustomerServiceView>();
        view.ShouldNotBeNull();
        view.CustomerId.ShouldBe(customerId);
        view.Email.ShouldBe("john.doe@example.com");
        view.FirstName.ShouldBe("John");
        view.LastName.ShouldBe("Doe");
        view.Orders.Count.ShouldBe(2);
        view.Orders[0].TotalAmount.ShouldBe(125.50m);
        view.Orders[1].TotalAmount.ShouldBe(89.99m);
    }

    [Fact]
    public async Task GetCustomerServiceView_WithNonExistentEmail_Returns404()
    {
        // Arrange
        var email = "nonexistent@example.com";

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/customers/search?email={email}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task GetCustomerServiceView_WithNoOrders_ReturnsEmptyOrderList()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new CustomerDto(
            customerId,
            "new.customer@example.com",
            "New",
            "Customer",
            null,
            DateTime.UtcNow);

        _fixture.CustomerIdentityClient.AddCustomer(customer);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/customers/search?email={customer.Email}");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var view = result.ReadAsJson<CustomerServiceView>();
        view.ShouldNotBeNull();
        view.Orders.Count.ShouldBe(0);
    }
}
