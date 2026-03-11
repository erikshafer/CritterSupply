using Storefront.Clients;
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
/// NOTE on Wolverine.SignalR tracking behavior:
/// When a handler returns T.ToWebSocketGroup(...), Wolverine wraps the outgoing message as
/// SignalRMessage&lt;T&gt;. Wolverine's ITrackedSession.Sent.MessagesOf&lt;T&gt;() looks for the RAW T type,
/// NOT the wrapped type. To assert on group-scoped SignalR messages you must use
/// MessagesOf&lt;SignalRMessage&lt;T&gt;&gt;() and access the .Message property for payload assertions.
/// This is a significant API discoverability gap worth reporting upstream to JasperFx.
/// See: docs/research/storefront-ux-session/WOLVERINE-SIGNALR-OBSERVATIONS.md
///
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
    public async Task ItemAdded_IntegrationMessage_ReturnsGroupScopedCartUpdatedMessage()
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

        // Act
        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert — handler returns SignalRMessage<CartUpdated> (group-scoped), NOT raw CartUpdated.
        // Use MessagesOf<SignalRMessage<CartUpdated>>() to access the wrapper.
        var published = tracked.Sent.MessagesOf<SignalRMessage<CartUpdated>>();
        published.ShouldNotBeEmpty();

        var signalRMessage = published.Single();

        // Verify customer isolation: group must be "customer:{customerId}"
        signalRMessage.Locator.ToString()!.ShouldContain($"customer:{customerId}");

        // Verify payload via the inner Message property
        var cartUpdated = signalRMessage.Message;
        cartUpdated.CartId.ShouldBe(cartId);
        cartUpdated.CustomerId.ShouldBe(customerId);
        cartUpdated.ItemCount.ShouldBe(1); // 1 unique SKU
        cartUpdated.TotalAmount.ShouldBe(59.98m); // 2 * 29.99
    }

    [Fact]
    public async Task ItemRemoved_IntegrationMessage_ReturnsGroupScopedCartUpdatedMessage()
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
        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert — handler returns SignalRMessage<CartUpdated> (group-scoped)
        var published = tracked.Sent.MessagesOf<SignalRMessage<CartUpdated>>();
        published.ShouldNotBeEmpty();

        var signalRMessage = published.Single();
        signalRMessage.Locator.ToString()!.ShouldContain($"customer:{customerId}");

        var cartUpdated = signalRMessage.Message;
        cartUpdated.CartId.ShouldBe(cartId);
        cartUpdated.CustomerId.ShouldBe(customerId);
        cartUpdated.ItemCount.ShouldBe(0); // Empty cart
        cartUpdated.TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task ItemQuantityChanged_IntegrationMessage_ReturnsGroupScopedCartUpdatedMessage()
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
        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert — handler returns SignalRMessage<CartUpdated> (group-scoped)
        var published = tracked.Sent.MessagesOf<SignalRMessage<CartUpdated>>();
        published.ShouldNotBeEmpty();

        var signalRMessage = published.Single();
        signalRMessage.Locator.ToString()!.ShouldContain($"customer:{customerId}");

        var cartUpdated = signalRMessage.Message;
        cartUpdated.CartId.ShouldBe(cartId);
        cartUpdated.CustomerId.ShouldBe(customerId);
        cartUpdated.ItemCount.ShouldBe(1);
        cartUpdated.TotalAmount.ShouldBe(149.95m); // 5 * 29.99
    }

    [Fact]
    public async Task OrderPlaced_IntegrationMessage_ReturnsGroupScopedOrderStatusChangedMessage()
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
        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert — handler returns SignalRMessage<OrderStatusChanged> (group-scoped)
        var published = tracked.Sent.MessagesOf<SignalRMessage<OrderStatusChanged>>();
        published.ShouldNotBeEmpty();

        var signalRMessage = published.Single();
        signalRMessage.Locator.ToString()!.ShouldContain($"customer:{customerId}");

        var orderStatusChanged = signalRMessage.Message;
        orderStatusChanged.OrderId.ShouldBe(orderId);
        orderStatusChanged.CustomerId.ShouldBe(customerId);
        orderStatusChanged.NewStatus.ShouldBe("Placed");
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
    public async Task PaymentAuthorized_IntegrationMessage_ReturnsGroupScopedOrderStatusChangedMessage()
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
        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert — handler returns SignalRMessage<OrderStatusChanged> (group-scoped)
        var published = tracked.Sent.MessagesOf<SignalRMessage<OrderStatusChanged>>();
        published.ShouldNotBeEmpty();

        var signalRMessage = published.Single();
        // Note: CustomerId is Guid.Empty (stub — Orders BC query not yet implemented)
        signalRMessage.Locator.ToString()!.ShouldContain($"customer:{StubCustomerId}");

        var orderStatusChanged = signalRMessage.Message;
        orderStatusChanged.OrderId.ShouldBe(orderId);
        orderStatusChanged.NewStatus.ShouldBe("PaymentAuthorized");
    }

    [Fact]
    public async Task ReservationConfirmed_IntegrationMessage_ReturnsGroupScopedOrderStatusChangedMessage()
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
        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert — handler returns SignalRMessage<OrderStatusChanged> (group-scoped)
        var published = tracked.Sent.MessagesOf<SignalRMessage<OrderStatusChanged>>();
        published.ShouldNotBeEmpty();

        var signalRMessage = published.Single();
        signalRMessage.Locator.ToString()!.ShouldContain($"customer:{StubCustomerId}"); // Stub

        var orderStatusChanged = signalRMessage.Message;
        orderStatusChanged.OrderId.ShouldBe(orderId);
        orderStatusChanged.NewStatus.ShouldBe("InventoryReserved");
    }

    [Fact]
    public async Task ShipmentDispatched_IntegrationMessage_ReturnsGroupScopedShipmentStatusChangedMessage()
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
        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert — handler returns SignalRMessage<ShipmentStatusChanged> (group-scoped)
        var published = tracked.Sent.MessagesOf<SignalRMessage<ShipmentStatusChanged>>();
        published.ShouldNotBeEmpty();

        var signalRMessage = published.Single();
        signalRMessage.Locator.ToString()!.ShouldContain($"customer:{StubCustomerId}"); // Stub

        var shipmentStatusChanged = signalRMessage.Message;
        shipmentStatusChanged.ShipmentId.ShouldBe(shipmentId);
        shipmentStatusChanged.OrderId.ShouldBe(orderId);
        shipmentStatusChanged.NewStatus.ShouldBe("Dispatched");
        shipmentStatusChanged.TrackingNumber.ShouldBe("TRACK123");
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

        // Act
        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Assert — null return means no SignalR message of any kind was sent.
        // Verifying the wrapped type (SignalRMessage<CartUpdated>) is empty confirms no broadcast occurred.
        tracked.Sent.MessagesOf<SignalRMessage<CartUpdated>>().ShouldBeEmpty();
    }
}
