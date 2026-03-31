using Backoffice.Clients;
using Backoffice.Composition;

namespace Backoffice.Api.IntegrationTests.CustomerService;

/// <summary>
/// Integration tests for CS customer detail workflow.
/// Tests GET /api/backoffice/customers/{customerId}
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CustomerDetailTests
{
    private readonly BackofficeTestFixture _fixture;

    public CustomerDetailTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.CustomerIdentityClient.Clear();
        _fixture.OrdersClient.Clear();
    }

    [Fact]
    public async Task GetCustomerDetailView_WithValidId_ReturnsCustomerAndOrders()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new CustomerDto(
            customerId,
            "jane.doe@example.com",
            "Jane",
            "Doe",
            null,
            DateTime.UtcNow.AddMonths(-3));

        _fixture.CustomerIdentityClient.AddCustomer(customer);

        var order1 = new OrderSummaryDto(
            Guid.NewGuid(),
            customerId,
            DateTime.UtcNow.AddDays(-5),
            "Confirmed",
            99.99m);

        var order2 = new OrderSummaryDto(
            Guid.NewGuid(),
            customerId,
            DateTime.UtcNow.AddDays(-15),
            "Delivered",
            45.00m);

        _fixture.OrdersClient.AddOrder(order1);
        _fixture.OrdersClient.AddOrder(order2);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/customers/{customerId}");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var view = result.ReadAsJson<CustomerDetailView>();
        view.ShouldNotBeNull();
        view.CustomerId.ShouldBe(customerId);
        view.Email.ShouldBe("jane.doe@example.com");
        view.FirstName.ShouldBe("Jane");
        view.LastName.ShouldBe("Doe");
        view.Orders.Count.ShouldBe(2);
        view.Orders[0].TotalAmount.ShouldBe(99.99m);
        view.Orders[1].TotalAmount.ShouldBe(45.00m);
    }

    [Fact]
    public async Task GetCustomerDetailView_WithNonExistentId_Returns404()
    {
        // Arrange
        var customerId = Guid.NewGuid();

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/customers/{customerId}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task GetCustomerDetailView_WithNoOrders_ReturnsEmptyOrderList()
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
            s.Get.Url($"/api/backoffice/customers/{customerId}");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var view = result.ReadAsJson<CustomerDetailView>();
        view.ShouldNotBeNull();
        view.Orders.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetCustomerDetailView_WithAddresses_ReturnsAddresses()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new CustomerDto(
            customerId,
            "address.test@example.com",
            "Address",
            "Test",
            null,
            DateTime.UtcNow.AddMonths(-1));

        _fixture.CustomerIdentityClient.AddCustomer(customer);

        var address = new CustomerAddressDto(
            Guid.NewGuid(),
            customerId,
            "Home",
            "123 Main St",
            "Apt 4B",
            "Springfield",
            "IL",
            "62704",
            "US",
            "Shipping",
            true);

        _fixture.CustomerIdentityClient.AddAddress(address);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/customers/{customerId}");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var view = result.ReadAsJson<CustomerDetailView>();
        view.ShouldNotBeNull();
        view.Addresses.Count.ShouldBe(1);
        view.Addresses[0].Nickname.ShouldBe("Home");
        view.Addresses[0].AddressLine1.ShouldBe("123 Main St");
        view.Addresses[0].City.ShouldBe("Springfield");
        view.Addresses[0].IsDefault.ShouldBeTrue();
    }
}
