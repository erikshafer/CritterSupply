using Storefront.Clients;
using Storefront.Notifications;
using Storefront.RealTime;
using Wolverine.SignalR;
using Wolverine.Tracking;

namespace Storefront.Api.IntegrationTests;

/// <summary>
/// Tests for SignalR real-time notifications.
///
/// Verifies integration message handlers correctly:
/// 1. Produce SignalRMessage&lt;T&gt; wrappers (not raw messages) so Wolverine routes to the right group
/// 2. Target the customer-specific group ("customer:{customerId}") — preventing cross-customer leakage
/// 3. Include the correct payload in the inner Message property
///
/// IMPORTANT — Why tests call handlers directly instead of InvokeMessageAndWaitAsync:
///
/// When a handler returns SignalRMessage&lt;T&gt; (via .ToWebSocketGroup()), Wolverine routes the message
/// through the SignalR transport. In tests, DisableAllExternalWolverineTransports() disables the
/// SignalR transport. When the transport is disabled, messages sent to it are NOT recorded in
/// ITrackedSession.Sent — they are dropped silently. As a result, calling
/// tracked.Sent.MessagesOf&lt;SignalRMessage&lt;T&gt;&gt;() always returns empty in this test setup.
///
/// The correct approach: call the static handler methods directly and assert on the return value.
/// This verifies the handler logic (correct payload, correct group name) without depending on
/// Wolverine transport tracking infrastructure. Since all notification handlers are pure static
/// methods, they can be invoked and inspected directly.
///
/// This is documented as Observation 6 in WOLVERINE-SIGNALR-OBSERVATIONS.md.
/// Actual SignalR hub delivery requires full Kestrel (not TestServer) — verified via E2E tests.
/// </summary>
[Collection("Sequential")]
public class SignalRNotificationTests(TestFixture fixture) : IClassFixture<TestFixture>, IAsyncLifetime
{
    /// <summary>
    /// Stub customer ID used for handlers that have not yet implemented the Orders BC lookup
    /// (PaymentAuthorized, ReservationConfirmed, ShipmentDispatched).
    /// TODO: Replace with real customerId lookup from Orders BC when implemented.
    /// </summary>
    private static readonly Guid StubCustomerId = Guid.Empty;

    public Task InitializeAsync()
    {
        // Clear stub data before each test
        fixture.StubShoppingClient.Clear();
        fixture.StubCatalogClient.Clear();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ItemAdded_Handler_ReturnsGroupScopedCartUpdatedMessage()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        fixture.StubShoppingClient.ConfigureCart(cartId, new CartDto(
            cartId,
            customerId,
            [new CartItemDto("DOG-BOWL-001", 2, 29.99m)]));

        var message = new Messages.Contracts.Shopping.ItemAdded(
            cartId,
            customerId,
            "DOG-BOWL-001",
            2,
            29.99m,
            DateTimeOffset.UtcNow);

        // Act — call handler directly (SignalRMessage<T> is not recorded in ITrackedSession.Sent
        // when the SignalR transport is disabled in tests; see class-level doc comment)
        var result = await ItemAddedHandler.Handle(message, fixture.StubShoppingClient, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        // Verify customer isolation: group must be "customer:{customerId}"
        result!.Locator.ToString()!.ShouldContain($"customer:{customerId}");
        // Verify payload
        result.Message.CartId.ShouldBe(cartId);
        result.Message.CustomerId.ShouldBe(customerId);
        result.Message.ItemCount.ShouldBe(1); // 1 unique SKU
        result.Message.TotalAmount.ShouldBe(59.98m); // 2 * 29.99
    }

    [Fact]
    public async Task ItemRemoved_Handler_ReturnsGroupScopedCartUpdatedMessage()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        fixture.StubShoppingClient.ConfigureCart(cartId, new CartDto(cartId, customerId, []));

        var message = new Messages.Contracts.Shopping.ItemRemoved(
            cartId,
            customerId,
            "DOG-BOWL-001",
            DateTimeOffset.UtcNow);

        // Act
        var result = await ItemRemovedHandler.Handle(message, fixture.StubShoppingClient, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result!.Locator.ToString()!.ShouldContain($"customer:{customerId}");
        result.Message.CartId.ShouldBe(cartId);
        result.Message.CustomerId.ShouldBe(customerId);
        result.Message.ItemCount.ShouldBe(0); // Empty cart
        result.Message.TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task ItemQuantityChanged_Handler_ReturnsGroupScopedCartUpdatedMessage()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        fixture.StubShoppingClient.ConfigureCart(cartId, new CartDto(
            cartId,
            customerId,
            [new CartItemDto("DOG-BOWL-001", 5, 29.99m)]));

        var message = new Messages.Contracts.Shopping.ItemQuantityChanged(
            cartId,
            customerId,
            "DOG-BOWL-001",
            2,
            5,
            DateTimeOffset.UtcNow);

        // Act
        var result = await ItemQuantityChangedHandler.Handle(message, fixture.StubShoppingClient, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result!.Locator.ToString()!.ShouldContain($"customer:{customerId}");
        result.Message.CartId.ShouldBe(cartId);
        result.Message.CustomerId.ShouldBe(customerId);
        result.Message.ItemCount.ShouldBe(1);
        result.Message.TotalAmount.ShouldBe(149.95m); // 5 * 29.99
    }

    [Fact]
    public void OrderPlaced_Handler_ReturnsGroupScopedOrderStatusChangedMessage()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

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

        // Act
        var result = OrderPlacedHandler.Handle(message);

        // Assert
        result.ShouldNotBeNull();
        result.Locator.ToString()!.ShouldContain($"customer:{customerId}");
        result.Message.OrderId.ShouldBe(orderId);
        result.Message.CustomerId.ShouldBe(customerId);
        result.Message.NewStatus.ShouldBe("Placed");
    }

    [Fact]
    public async Task CartUpdated_Message_ImplementsSignalRMarkerInterface()
    {
        var message = new CartUpdated(
            Guid.NewGuid(),
            Guid.NewGuid(),
            3,
            99.99m,
            DateTimeOffset.UtcNow);

        message.ShouldBeAssignableTo<IStorefrontWebSocketMessage>();
        message.CustomerId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task OrderStatusChanged_Message_ImplementsSignalRMarkerInterface()
    {
        var message = new OrderStatusChanged(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Placed",
            DateTimeOffset.UtcNow);

        message.ShouldBeAssignableTo<IStorefrontWebSocketMessage>();
        message.CustomerId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task ShipmentStatusChanged_Message_ImplementsSignalRMarkerInterface()
    {
        var message = new ShipmentStatusChanged(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Dispatched",
            "TRACK123",
            DateTimeOffset.UtcNow);

        message.ShouldBeAssignableTo<IStorefrontWebSocketMessage>();
        message.CustomerId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void PaymentAuthorized_Handler_ReturnsGroupScopedOrderStatusChangedMessage()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var message = new Messages.Contracts.Payments.PaymentAuthorized(
            paymentId,
            orderId,
            99.99m,
            "auth_12345",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1));

        // Act
        var result = PaymentAuthorizedHandler.Handle(message);

        // Assert
        result.ShouldNotBeNull();
        // Note: CustomerId is Guid.Empty (stub — Orders BC query not yet implemented)
        result.Locator.ToString()!.ShouldContain($"customer:{StubCustomerId}");
        result.Message.OrderId.ShouldBe(orderId);
        result.Message.NewStatus.ShouldBe("PaymentAuthorized");
    }

    [Fact]
    public void ReservationConfirmed_Handler_ReturnsGroupScopedOrderStatusChangedMessage()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        var message = new Messages.Contracts.Inventory.ReservationConfirmed(
            orderId,
            inventoryId,
            reservationId,
            "DOG-BOWL-001",
            "WAREHOUSE-001",
            2,
            DateTimeOffset.UtcNow);

        // Act
        var result = ReservationConfirmedHandler.Handle(message);

        // Assert
        result.ShouldNotBeNull();
        result.Locator.ToString()!.ShouldContain($"customer:{StubCustomerId}"); // Stub
        result.Message.OrderId.ShouldBe(orderId);
        result.Message.NewStatus.ShouldBe("InventoryReserved");
    }

    [Fact]
    public void ShipmentDispatched_Handler_ReturnsGroupScopedShipmentStatusChangedMessage()
    {
        // Arrange
        var shipmentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var message = new Messages.Contracts.Fulfillment.ShipmentDispatched(
            orderId,
            shipmentId,
            "UPS",
            "TRACK123",
            DateTimeOffset.UtcNow);

        // Act
        var result = ShipmentDispatchedHandler.Handle(message);

        // Assert
        result.ShouldNotBeNull();
        result.Locator.ToString()!.ShouldContain($"customer:{StubCustomerId}"); // Stub
        result.Message.ShipmentId.ShouldBe(shipmentId);
        result.Message.OrderId.ShouldBe(orderId);
        result.Message.NewStatus.ShouldBe("Dispatched");
        result.Message.TrackingNumber.ShouldBe("TRACK123");
    }

    [Fact]
    public async Task ItemAddedHandler_WhenCartNotFound_ReturnsNull()
    {
        // Arrange — no stub configured for this cartId, so client returns null
        var nonExistentCartId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var message = new Messages.Contracts.Shopping.ItemAdded(
            nonExistentCartId,
            customerId,
            "DOG-BOWL-001",
            1,
            19.99m,
            DateTimeOffset.UtcNow);

        // Act — call handler directly; null return = no SignalR message produced
        var result = await ItemAddedHandler.Handle(message, fixture.StubShoppingClient, CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }
}
