using Alba;
using Shouldly;
using Storefront.Clients;
using Storefront.RealTime;
using Wolverine.Tracking;

namespace Storefront.IntegrationTests;

/// <summary>
/// Tests for SignalR real-time notifications.
/// Verifies integration message handlers return correct SignalR messages for Wolverine routing.
/// Note: Actual SignalR hub delivery requires full Kestrel (not TestServer) - tested via E2E.
/// </summary>
[Collection("Sequential")]
public class SignalRNotificationTests(TestFixture fixture) : IClassFixture<TestFixture>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        // Clear stub data before each test
        fixture.StubShoppingClient.Clear();
        fixture.StubCatalogClient.Clear();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ItemAdded_IntegrationMessage_ReturnsCartUpdatedMessage()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        // Configure stub to return cart data when queried
        fixture.StubShoppingClient.ConfigureCart(cartId, new CartDto(
            cartId,
            customerId,
            [new CartItemDto("DOG-BOWL-001", 2, 29.99m)]));

        // Act - Publish Shopping.ItemAdded integration message via Wolverine
        var message = new Messages.Contracts.Shopping.ItemAdded(
            cartId,
            customerId,
            "DOG-BOWL-001",
            2,
            29.99m,
            DateTimeOffset.UtcNow);

        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert - Verify handler returned CartUpdated message for SignalR routing
        var published = tracked.Sent.MessagesOf<CartUpdated>();
        published.ShouldNotBeEmpty();

        var cartUpdated = published.Single();
        cartUpdated.CartId.ShouldBe(cartId);
        cartUpdated.CustomerId.ShouldBe(customerId);
        cartUpdated.ItemCount.ShouldBe(1); // 1 unique SKU
        cartUpdated.TotalAmount.ShouldBe(59.98m); // 2 * 29.99
    }

    [Fact]
    public async Task ItemRemoved_IntegrationMessage_ReturnsCartUpdatedMessage()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        // Configure stub to return empty cart after removal
        fixture.StubShoppingClient.ConfigureCart(cartId, new CartDto(cartId, customerId, []));

        // Act - Publish Shopping.ItemRemoved integration message
        var message = new Messages.Contracts.Shopping.ItemRemoved(
            cartId,
            customerId,
            "DOG-BOWL-001",
            DateTimeOffset.UtcNow);

        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert - Verify handler returned CartUpdated message
        var published = tracked.Sent.MessagesOf<CartUpdated>();
        published.ShouldNotBeEmpty();

        var cartUpdated = published.Single();
        cartUpdated.CartId.ShouldBe(cartId);
        cartUpdated.CustomerId.ShouldBe(customerId);
        cartUpdated.ItemCount.ShouldBe(0); // Empty cart
        cartUpdated.TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task ItemQuantityChanged_IntegrationMessage_ReturnsCartUpdatedMessage()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        // Configure stub to return cart with updated quantity
        fixture.StubShoppingClient.ConfigureCart(cartId, new CartDto(
            cartId,
            customerId,
            [new CartItemDto("DOG-BOWL-001", 5, 29.99m)]));

        // Act - Publish Shopping.ItemQuantityChanged integration message
        var message = new Messages.Contracts.Shopping.ItemQuantityChanged(
            cartId,
            customerId,
            "DOG-BOWL-001",
            2, // Old quantity
            5, // New quantity
            DateTimeOffset.UtcNow);

        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert - Verify handler returned CartUpdated message
        var published = tracked.Sent.MessagesOf<CartUpdated>();
        published.ShouldNotBeEmpty();

        var cartUpdated = published.Single();
        cartUpdated.CartId.ShouldBe(cartId);
        cartUpdated.CustomerId.ShouldBe(customerId);
        cartUpdated.ItemCount.ShouldBe(1);
        cartUpdated.TotalAmount.ShouldBe(149.95m); // 5 * 29.99
    }

    [Fact]
    public async Task OrderPlaced_IntegrationMessage_ReturnsOrderStatusChangedMessage()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Act - Publish Orders.OrderPlaced integration message
        var message = new Messages.Contracts.Orders.OrderPlaced(
            orderId,
            customerId,
            [],
            new Messages.Contracts.Orders.ShippingAddress(
                "123 Main St",
                null,
                "Seattle",
                "WA",
                "98101",
                "US"),
            "Standard",
            "tok_visa",
            99.99m,
            DateTimeOffset.UtcNow);

        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert - Verify handler returned OrderStatusChanged message
        var published = tracked.Sent.MessagesOf<OrderStatusChanged>();
        published.ShouldNotBeEmpty();

        var orderStatusChanged = published.Single();
        orderStatusChanged.OrderId.ShouldBe(orderId);
        orderStatusChanged.CustomerId.ShouldBe(customerId);
        orderStatusChanged.NewStatus.ShouldBe("Placed");
    }

    [Fact]
    public async Task CartUpdated_Message_ImplementsSignalRMarkerInterface()
    {
        // Arrange & Act
        var message = new CartUpdated(
            Guid.NewGuid(),
            Guid.NewGuid(),
            3,
            99.99m,
            DateTimeOffset.UtcNow);

        // Assert - Verify message implements IStorefrontWebSocketMessage for Wolverine routing
        message.ShouldBeAssignableTo<IStorefrontWebSocketMessage>();
        message.CustomerId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task OrderStatusChanged_Message_ImplementsSignalRMarkerInterface()
    {
        // Arrange & Act
        var message = new OrderStatusChanged(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Placed",
            DateTimeOffset.UtcNow);

        // Assert - Verify message implements IStorefrontWebSocketMessage
        message.ShouldBeAssignableTo<IStorefrontWebSocketMessage>();
        message.CustomerId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task ShipmentStatusChanged_Message_ImplementsSignalRMarkerInterface()
    {
        // Arrange & Act
        var message = new ShipmentStatusChanged(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Dispatched",
            "TRACK123",
            DateTimeOffset.UtcNow);

        // Assert - Verify message implements IStorefrontWebSocketMessage
        message.ShouldBeAssignableTo<IStorefrontWebSocketMessage>();
        message.CustomerId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task PaymentAuthorized_IntegrationMessage_ReturnsOrderStatusChangedMessage()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Act - Publish Payments.PaymentAuthorized integration message
        var message = new Messages.Contracts.Payments.PaymentAuthorized(
            paymentId,
            orderId,
            99.99m,
            "auth_12345",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1));

        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert - Verify handler returned OrderStatusChanged message
        var published = tracked.Sent.MessagesOf<OrderStatusChanged>();
        published.ShouldNotBeEmpty();

        var orderStatusChanged = published.Single();
        orderStatusChanged.OrderId.ShouldBe(orderId);
        orderStatusChanged.NewStatus.ShouldBe("PaymentAuthorized");
        // Note: CustomerId is Guid.Empty (stub until Orders BC query implemented)
    }

    [Fact]
    public async Task ReservationConfirmed_IntegrationMessage_ReturnsOrderStatusChangedMessage()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        // Act - Publish Inventory.ReservationConfirmed integration message
        var message = new Messages.Contracts.Inventory.ReservationConfirmed(
            orderId,
            inventoryId,
            reservationId,
            "DOG-BOWL-001",
            "WAREHOUSE-001",
            2,
            DateTimeOffset.UtcNow);

        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert - Verify handler returned OrderStatusChanged message
        var published = tracked.Sent.MessagesOf<OrderStatusChanged>();
        published.ShouldNotBeEmpty();

        var orderStatusChanged = published.Single();
        orderStatusChanged.OrderId.ShouldBe(orderId);
        orderStatusChanged.NewStatus.ShouldBe("InventoryReserved");
        // Note: CustomerId is Guid.Empty (stub until Orders BC query implemented)
    }

    [Fact]
    public async Task ShipmentDispatched_IntegrationMessage_ReturnsShipmentStatusChangedMessage()
    {
        // Arrange
        var shipmentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Act - Publish Fulfillment.ShipmentDispatched integration message
        var message = new Messages.Contracts.Fulfillment.ShipmentDispatched(
            orderId,
            shipmentId,
            "UPS",
            "TRACK123",
            DateTimeOffset.UtcNow);

        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert - Verify handler returned ShipmentStatusChanged message
        var published = tracked.Sent.MessagesOf<ShipmentStatusChanged>();
        published.ShouldNotBeEmpty();

        var shipmentStatusChanged = published.Single();
        shipmentStatusChanged.ShipmentId.ShouldBe(shipmentId);
        shipmentStatusChanged.OrderId.ShouldBe(orderId);
        shipmentStatusChanged.NewStatus.ShouldBe("Dispatched");
        shipmentStatusChanged.TrackingNumber.ShouldBe("TRACK123");
        // Note: CustomerId is Guid.Empty (stub until Orders BC query implemented)
    }

    [Fact]
    public async Task ItemAddedHandler_WhenCartNotFound_ReturnsNull()
    {
        // Arrange
        var nonExistentCartId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // Note: StubShoppingClient returns null for non-existent cart
        // DO NOT configure stub - testing null case

        // Act - Publish Shopping.ItemAdded for non-existent cart
        var message = new Messages.Contracts.Shopping.ItemAdded(
            nonExistentCartId,
            customerId,
            "DOG-BOWL-001",
            1,
            19.99m,
            DateTimeOffset.UtcNow);

        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert - Handler returns null, no SignalR message published
        var published = tracked.Sent.MessagesOf<CartUpdated>();
        published.ShouldBeEmpty(); // No message sent when cart not found
    }
}
