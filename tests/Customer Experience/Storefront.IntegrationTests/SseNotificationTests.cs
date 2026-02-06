using Alba;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Storefront.Clients;
using Storefront.Notifications;
using Wolverine.Tracking;

namespace Storefront.IntegrationTests;

/// <summary>
/// Tests for SSE real-time notifications.
/// Verifies integration message handling triggers SSE broadcast to connected clients.
/// </summary>
[Collection("Sequential")] // SSE tests share EventBroadcaster state
public class SseNotificationTests(TestFixture fixture) : IClassFixture<TestFixture>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        // Clear stub data before each test
        fixture.StubShoppingClient.Clear();
        fixture.StubCatalogClient.Clear();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(Skip = "Deferred to Phase 3 - Alba doesn't support IAsyncEnumerable SSE endpoint testing")]
    public async Task SSE_Endpoint_AcceptsConnections_AndStreamsEvents()
    {
        // TODO: Alba doesn't support IAsyncEnumerable streaming responses yet
        // Will test SSE endpoint manually in Phase 3 with browser/curl or custom HttpClient
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ItemAdded_IntegrationMessage_TriggersSSEBroadcast()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        // Configure stub to return cart data when queried
        fixture.StubShoppingClient.ConfigureCart(cartId, new CartDto(
            cartId,
            customerId,
            [new CartItemDto("DOG-BOWL-001", 2, 29.99m)]));

        // Subscribe to SSE events before publishing message
        var broadcaster = fixture.Host.Services.GetRequiredService<IEventBroadcaster>();
        var receivedEvents = new List<StorefrontEvent>();

        // Start SSE subscription in background
        var subscriptionTask = Task.Run(async () =>
        {
            await foreach (var @event in broadcaster.SubscribeAsync(customerId, CancellationToken.None))
            {
                receivedEvents.Add(@event);
                break; // Exit after receiving first event
            }
        });

        // Wait for subscription to be active
        await Task.Delay(100);

        // Act - Publish Shopping.ItemAdded integration message via Wolverine
        var message = new Messages.Contracts.Shopping.ItemAdded(
            cartId,
            customerId,
            "DOG-BOWL-001",
            2,
            29.99m,
            DateTimeOffset.UtcNow);

        await fixture.Host.InvokeMessageAndWaitAsync(message);

        // Wait for SSE broadcast
        await Task.WhenAny(subscriptionTask, Task.Delay(5000));

        // Assert
        receivedEvents.ShouldNotBeEmpty();
        var cartUpdated = receivedEvents[0].ShouldBeOfType<CartUpdated>();
        cartUpdated.CartId.ShouldBe(cartId);
        cartUpdated.CustomerId.ShouldBe(customerId);
        cartUpdated.ItemCount.ShouldBe(1); // 1 unique SKU
        cartUpdated.TotalAmount.ShouldBe(59.98m); // 2 * 29.99
    }

    [Fact]
    public async Task ItemRemoved_IntegrationMessage_TriggersSSEBroadcast()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        // Configure stub to return empty cart after removal
        fixture.StubShoppingClient.ConfigureCart(cartId, new CartDto(cartId, customerId, []));

        // Subscribe to SSE events
        var broadcaster = fixture.Host.Services.GetRequiredService<IEventBroadcaster>();
        var receivedEvents = new List<StorefrontEvent>();

        var subscriptionTask = Task.Run(async () =>
        {
            await foreach (var @event in broadcaster.SubscribeAsync(customerId, CancellationToken.None))
            {
                receivedEvents.Add(@event);
                break;
            }
        });

        await Task.Delay(100);

        // Act - Publish Shopping.ItemRemoved integration message
        var message = new Messages.Contracts.Shopping.ItemRemoved(
            cartId,
            customerId,
            "DOG-BOWL-001",
            DateTimeOffset.UtcNow);

        await fixture.Host.InvokeMessageAndWaitAsync(message);

        await Task.WhenAny(subscriptionTask, Task.Delay(5000));

        // Assert
        receivedEvents.ShouldNotBeEmpty();
        var cartUpdated = receivedEvents[0].ShouldBeOfType<CartUpdated>();
        cartUpdated.CartId.ShouldBe(cartId);
        cartUpdated.CustomerId.ShouldBe(customerId);
        cartUpdated.ItemCount.ShouldBe(0); // Empty cart
        cartUpdated.TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task ItemQuantityChanged_IntegrationMessage_TriggersSSEBroadcast()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        // Configure stub to return cart with updated quantity
        fixture.StubShoppingClient.ConfigureCart(cartId, new CartDto(
            cartId,
            customerId,
            [new CartItemDto("DOG-BOWL-001", 5, 29.99m)]));

        // Subscribe to SSE events
        var broadcaster = fixture.Host.Services.GetRequiredService<IEventBroadcaster>();
        var receivedEvents = new List<StorefrontEvent>();

        var subscriptionTask = Task.Run(async () =>
        {
            await foreach (var @event in broadcaster.SubscribeAsync(customerId, CancellationToken.None))
            {
                receivedEvents.Add(@event);
                break;
            }
        });

        await Task.Delay(100);

        // Act - Publish Shopping.ItemQuantityChanged integration message
        var message = new Messages.Contracts.Shopping.ItemQuantityChanged(
            cartId,
            customerId,
            "DOG-BOWL-001",
            2, // Old quantity
            5, // New quantity
            DateTimeOffset.UtcNow);

        await fixture.Host.InvokeMessageAndWaitAsync(message);

        await Task.WhenAny(subscriptionTask, Task.Delay(5000));

        // Assert
        receivedEvents.ShouldNotBeEmpty();
        var cartUpdated = receivedEvents[0].ShouldBeOfType<CartUpdated>();
        cartUpdated.CartId.ShouldBe(cartId);
        cartUpdated.CustomerId.ShouldBe(customerId);
        cartUpdated.ItemCount.ShouldBe(1);
        cartUpdated.TotalAmount.ShouldBe(149.95m); // 5 * 29.99
    }

    [Fact]
    public async Task OrderPlaced_IntegrationMessage_TriggersSSEBroadcast()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Subscribe to SSE events
        var broadcaster = fixture.Host.Services.GetRequiredService<IEventBroadcaster>();
        var receivedEvents = new List<StorefrontEvent>();

        var subscriptionTask = Task.Run(async () =>
        {
            await foreach (var @event in broadcaster.SubscribeAsync(customerId, CancellationToken.None))
            {
                receivedEvents.Add(@event);
                break;
            }
        });

        await Task.Delay(100);

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

        await fixture.Host.InvokeMessageAndWaitAsync(message);

        await Task.WhenAny(subscriptionTask, Task.Delay(5000));

        // Assert
        receivedEvents.ShouldNotBeEmpty();
        var orderStatusChanged = receivedEvents[0].ShouldBeOfType<OrderStatusChanged>();
        orderStatusChanged.OrderId.ShouldBe(orderId);
        orderStatusChanged.CustomerId.ShouldBe(customerId);
        orderStatusChanged.NewStatus.ShouldBe("Placed");
    }

    [Fact]
    public async Task DifferentCustomers_OnlyReceiveTheirOwnEvents()
    {
        // Arrange
        var customer1Id = Guid.NewGuid();
        var customer2Id = Guid.NewGuid();
        var cart1Id = Guid.NewGuid();

        fixture.StubShoppingClient.ConfigureCart(cart1Id, new CartDto(
            cart1Id,
            customer1Id,
            [new CartItemDto("DOG-BOWL-001", 1, 29.99m)]));

        var broadcaster = fixture.Host.Services.GetRequiredService<IEventBroadcaster>();
        var customer1Events = new List<StorefrontEvent>();
        var customer2Events = new List<StorefrontEvent>();

        // Subscribe both customers
        var customer1Task = Task.Run(async () =>
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await foreach (var @event in broadcaster.SubscribeAsync(customer1Id, cts.Token))
                {
                    customer1Events.Add(@event);
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                // Expected - timeout without receiving event
            }
        });

        var customer2Task = Task.Run(async () =>
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await foreach (var @event in broadcaster.SubscribeAsync(customer2Id, cts.Token))
                {
                    customer2Events.Add(@event);
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                // Expected - timeout without receiving event
            }
        });

        await Task.Delay(100);

        // Act - Publish message for customer 1 only
        var message = new Messages.Contracts.Shopping.ItemAdded(
            cart1Id,
            customer1Id,
            "DOG-BOWL-001",
            1,
            29.99m,
            DateTimeOffset.UtcNow);

        await fixture.Host.InvokeMessageAndWaitAsync(message);

        await Task.WhenAny(customer1Task, Task.Delay(5000));
        await Task.WhenAny(customer2Task, Task.Delay(1000)); // Give customer2 a chance to incorrectly receive event

        // Assert - Customer 1 received event, Customer 2 did not
        customer1Events.ShouldNotBeEmpty();
        customer2Events.ShouldBeEmpty(); // Customer 2 should not receive customer 1's events
    }
}
