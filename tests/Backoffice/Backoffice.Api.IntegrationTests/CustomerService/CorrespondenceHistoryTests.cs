using Backoffice.Clients;
using Backoffice.Composition;

namespace Backoffice.Api.IntegrationTests.CustomerService;

/// <summary>
/// Integration tests for correspondence history workflows (CS agent: view message thread for customer).
/// </summary>
[Collection("Backoffice Integration Tests")]
public class CorrespondenceHistoryTests
{
    private readonly BackofficeTestFixture _fixture;

    public CorrespondenceHistoryTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
        // Clean stub data before each test
        _fixture.CustomerIdentityClient.Clear();
        _fixture.CorrespondenceClient.Clear();
    }

    [Fact]
    public async Task GetCorrespondenceHistory_WithValidCustomerId_ReturnsMessageHistory()
    {
        // Arrange
        var customerId = Guid.NewGuid();

        var customer = new CustomerDto(
            Id: customerId,
            Email: "john.doe@example.com",
            FirstName: "John",
            LastName: "Doe",
            PhoneNumber: null,
            CreatedAt: DateTime.UtcNow.AddMonths(-6));

        _fixture.CustomerIdentityClient.AddCustomer(customer);

        var message1 = new CorrespondenceMessageDto(
            Id: Guid.NewGuid(),
            CustomerId: customerId,
            SentAt: DateTime.UtcNow.AddDays(-7),
            MessageType: "OrderConfirmation",
            Subject: "Your order has been confirmed",
            DeliveryStatus: "Delivered");

        var message2 = new CorrespondenceMessageDto(
            Id: Guid.NewGuid(),
            CustomerId: customerId,
            SentAt: DateTime.UtcNow.AddDays(-5),
            MessageType: "ShipmentDispatched",
            Subject: "Your order has been shipped",
            DeliveryStatus: "Delivered");

        _fixture.CorrespondenceClient.AddMessage(message1);
        _fixture.CorrespondenceClient.AddMessage(message2);

        // Act
        var result = await _fixture.Host.GetAsJson<CorrespondenceHistoryView>(
            $"/api/backoffice/customers/{customerId}/correspondence");

        // Assert
        result.ShouldNotBeNull();
        result.CustomerId.ShouldBe(customerId);
        result.CustomerEmail.ShouldBe("john.doe@example.com");
        result.Messages.Count.ShouldBe(2);
        result.Messages[0].Subject.ShouldBe("Your order has been confirmed");
        result.Messages[0].DeliveryStatus.ShouldBe("Delivered");
        result.Messages[1].Subject.ShouldBe("Your order has been shipped");
    }

    [Fact]
    public async Task GetCorrespondenceHistory_WithNonExistentCustomer_Returns404()
    {
        // Arrange
        var nonExistentCustomerId = Guid.NewGuid();

        // Act & Assert
        await _fixture.Host.Scenario(_ =>
        {
            _.Get.Url($"/api/backoffice/customers/{nonExistentCustomerId}/correspondence");
            _.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task GetCorrespondenceHistory_WithCustomerWithNoMessages_ReturnsEmptyList()
    {
        // Arrange
        var customerId = Guid.NewGuid();

        var customer = new CustomerDto(
            Id: customerId,
            Email: "jane.smith@example.com",
            FirstName: "Jane",
            LastName: "Smith",
            PhoneNumber: null,
            CreatedAt: DateTime.UtcNow.AddDays(-1));

        _fixture.CustomerIdentityClient.AddCustomer(customer);

        // Act
        var result = await _fixture.Host.GetAsJson<CorrespondenceHistoryView>(
            $"/api/backoffice/customers/{customerId}/correspondence");

        // Assert
        result.ShouldNotBeNull();
        result.CustomerId.ShouldBe(customerId);
        result.CustomerEmail.ShouldBe("jane.smith@example.com");
        result.Messages.Count.ShouldBe(0);
    }
}
