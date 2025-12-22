using Marten;
using Messages.Contracts.Shopping;
using Orders.Placement;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Integration tests for Shopping-related saga handlers in Orders.
/// Tests the integration between Shopping BC and Orders BC.
/// </summary>
[Collection("orders-integration")]
public class ShoppingIntegrationTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ShoppingIntegrationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Integration test for CheckoutCompleted from Shopping BC.
    /// Verifies that CheckoutCompleted message from Shopping starts an Order saga.
    /// </summary>
    [Fact]
    public async Task CheckoutCompleted_FromShopping_CreatesOrderSaga()
    {
        // Arrange: Create CheckoutCompleted message from Shopping BC
        var orderId = Guid.CreateVersion7();
        var checkoutId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();

        var items = new List<Messages.Contracts.Shopping.CheckoutLineItem>
        {
            new("SKU-001", 2, 19.99m),
            new("SKU-002", 1, 39.99m)
        };

        var shippingAddress = new Messages.Contracts.Shopping.ShippingAddress(
            "123 Main St",
            "Apt 4B",
            "Seattle",
            "WA",
            "98101",
            "USA");

        var checkoutCompleted = new Messages.Contracts.Shopping.CheckoutCompleted(
            orderId,
            checkoutId,
            customerId,
            items,
            shippingAddress,
            "Standard Ground",
            5.99m,
            "tok_visa_test_12345",
            DateTimeOffset.UtcNow);

        // Act: Send CheckoutCompleted integration message
        await _fixture.ExecuteAndWaitAsync(checkoutCompleted, timeoutSeconds: 10);

        // Assert: Verify Order saga was created
        await using var session = _fixture.GetDocumentSession();
        var order = await session.LoadAsync<Order>(orderId);

        order.ShouldNotBeNull();
        order.Id.ShouldBe(orderId);
        order.CustomerId.ShouldBe(customerId);
        order.Status.ShouldBe(OrderStatus.Placed);
        order.LineItems.Count.ShouldBe(2);
        order.TotalAmount.ShouldBe(85.96m); // (2*19.99 + 39.99) + 5.99 shipping
        order.ShippingMethod.ShouldBe("Standard Ground");
        order.PaymentMethodToken.ShouldBe("tok_visa_test_12345");
    }

    /// <summary>
    /// Integration test verifying CheckoutCompleted publishes OrderPlaced event.
    /// Validates that downstream BCs (Inventory, Payments) receive OrderPlaced.
    /// </summary>
    [Fact]
    public async Task CheckoutCompleted_PublishesOrderPlaced()
    {
        // Arrange: Create CheckoutCompleted message from Shopping BC
        var orderId = Guid.CreateVersion7();
        var checkoutId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();

        var items = new List<Messages.Contracts.Shopping.CheckoutLineItem>
        {
            new("SKU-100", 3, 29.99m)
        };

        var shippingAddress = new Messages.Contracts.Shopping.ShippingAddress(
            "456 Oak Ave",
            null,
            "Portland",
            "OR",
            "97201",
            "USA");

        var checkoutCompleted = new Messages.Contracts.Shopping.CheckoutCompleted(
            orderId,
            checkoutId,
            customerId,
            items,
            shippingAddress,
            "Express",
            12.99m,
            "tok_mastercard_test_67890",
            DateTimeOffset.UtcNow);

        // Act: Send CheckoutCompleted and track messages
        var tracked = await _fixture.ExecuteAndWaitAsync(checkoutCompleted, timeoutSeconds: 10);

        // Assert: Verify OrderPlaced was published
        var orderPlacedMessages = tracked.Sent.MessagesOf<Messages.Contracts.Orders.OrderPlaced>();
        orderPlacedMessages.ShouldHaveSingleItem();

        var orderPlaced = orderPlacedMessages.Single();
        orderPlaced.OrderId.ShouldBe(orderId);
        orderPlaced.CustomerId.ShouldBe(customerId);
        orderPlaced.LineItems.Count.ShouldBe(1);
        orderPlaced.TotalAmount.ShouldBe(102.96m); // (3*29.99) + 12.99 shipping
    }

    /// <summary>
    /// Integration test verifying correct handling of shipping cost in total calculation.
    /// </summary>
    [Fact]
    public async Task CheckoutCompleted_IncludesShippingCostInTotal()
    {
        // Arrange: Create order with specific shipping cost
        var orderId = Guid.CreateVersion7();
        var checkoutId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();

        var items = new List<Messages.Contracts.Shopping.CheckoutLineItem>
        {
            new("SKU-200", 1, 100.00m)
        };

        var shippingAddress = new Messages.Contracts.Shopping.ShippingAddress(
            "789 Pine St",
            null,
            "Denver",
            "CO",
            "80202",
            "USA");

        var checkoutCompleted = new Messages.Contracts.Shopping.CheckoutCompleted(
            orderId,
            checkoutId,
            customerId,
            items,
            shippingAddress,
            "Overnight",
            25.00m, // Specific shipping cost
            "tok_amex_test_11111",
            DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteAndWaitAsync(checkoutCompleted, timeoutSeconds: 10);

        // Assert: Verify total includes shipping
        await using var session = _fixture.GetDocumentSession();
        var order = await session.LoadAsync<Order>(orderId);

        order.ShouldNotBeNull();
        order.TotalAmount.ShouldBe(125.00m); // 100.00 + 25.00 shipping
    }
}
