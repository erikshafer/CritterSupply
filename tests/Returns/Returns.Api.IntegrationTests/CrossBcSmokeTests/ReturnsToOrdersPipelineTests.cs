using Messages.Contracts.Returns;
using Messages.Contracts.Payments;
using Orders.Placement;
using Shouldly;

namespace Returns.Api.IntegrationTests.CrossBcSmokeTests;

/// <summary>
/// Smoke tests verifying Returns → Orders integration pipeline.
/// Tests that ReturnCompleted messages from Returns BC are successfully received
/// by the Order saga via RabbitMQ, triggering refund requests and saga state updates.
/// </summary>
[Collection(nameof(CrossBcTestCollection))]
public class ReturnsToOrdersPipelineTests(CrossBcTestFixture fixture)
{
    private readonly CrossBcTestFixture _fixture = fixture;

    [Fact(Skip = "Blocked by Wolverine saga persistence issue — saga created via InvokeAsync() is not found by subsequent handlers in multi-host tests. See docs/wolverine-saga-persistence-issue.md")]
    public async Task ReturnCompleted_Updates_Order_Saga_And_Emits_RefundRequested()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        // Step 1: Create an Order saga in Orders BC
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var checkoutCompleted = CreateCheckoutCompletedMessage(orderId, customerId);

        // IMPORTANT: Create saga directly in database (CheckoutCompleted is internal routing, not RabbitMQ)
        await _fixture.CreateOrderSagaAsync(orderId, customerId, checkoutCompleted);

        // Step 2: Deliver the shipment to get Order to Delivered status
        var shipmentDelivered = new Messages.Contracts.Fulfillment.ShipmentDelivered(
            orderId,
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteOnHostAndWaitAsync(
            _fixture.FulfillmentHost,
            shipmentDelivered,
            timeoutSeconds: 30);

        // Step 3: Start a return (simulate ReturnRequested)
        var returnId = Guid.CreateVersion7();
        var returnRequested = new ReturnRequested(
            returnId,
            orderId,
            customerId,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteOnHostAndWaitAsync(
            _fixture.ReturnsHost,
            returnRequested,
            timeoutSeconds: 30);

        // Verify return was tracked by Order saga
        using var ordersSession1 = _fixture.GetOrdersSession();
        var orderBeforeCompletion = await ordersSession1.LoadAsync<Order>(orderId);
        orderBeforeCompletion.ShouldNotBeNull();
        orderBeforeCompletion.ActiveReturnIds.ShouldContain(returnId);

        // Step 4: Complete the return (Returns BC publishes ReturnCompleted)
        const decimal finalRefundAmount = 45.00m;
        var returnCompleted = new ReturnCompleted(
            returnId,
            orderId,
            customerId,
            finalRefundAmount,
            Items: [],
            DateTimeOffset.UtcNow);

        // Act - Publish ReturnCompleted from Returns BC
        var tracked = await _fixture.ExecuteOnHostAndWaitAsync(
            _fixture.ReturnsHost,
            returnCompleted,
            timeoutSeconds: 30);

        // Assert - Verify ReturnCompleted was published to RabbitMQ
        tracked.Sent.SingleMessage<ReturnCompleted>()
            .ReturnId.ShouldBe(returnId);

        // Assert - Verify Order saga received the message and updated state
        using var ordersSession2 = _fixture.GetOrdersSession();
        var orderAfterCompletion = await ordersSession2.LoadAsync<Order>(orderId);

        orderAfterCompletion.ShouldNotBeNull();
        orderAfterCompletion.ActiveReturnIds.ShouldNotContain(returnId); // Return removed from active list
        orderAfterCompletion.ActiveReturnIds.Count.ShouldBe(0);

        // Assert - Verify RefundRequested was emitted for Payments BC
        var refundMessages = tracked.Sent.MessagesOf<RefundRequested>().ToList();
        refundMessages.Count.ShouldBe(1);
        refundMessages[0].OrderId.ShouldBe(orderId);
        refundMessages[0].Amount.ShouldBe(finalRefundAmount);
    }

    [Fact(Skip = "Blocked by Wolverine saga persistence issue — saga created via InvokeAsync() is not found by subsequent handlers in multi-host tests. See docs/wolverine-saga-persistence-issue.md")]
    public async Task ReturnCompleted_Closes_Saga_When_Return_Window_Already_Expired()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        // Step 1: Create an Order saga
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var checkoutCompleted = CreateCheckoutCompletedMessage(orderId, customerId);

        // IMPORTANT: Create saga directly in database (CheckoutCompleted is internal routing, not RabbitMQ)
        await _fixture.CreateOrderSagaAsync(orderId, customerId, checkoutCompleted);

        // Step 2: Deliver the shipment
        var shipmentDelivered = new Messages.Contracts.Fulfillment.ShipmentDelivered(
            orderId,
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteOnHostAndWaitAsync(
            _fixture.FulfillmentHost,
            shipmentDelivered,
            timeoutSeconds: 30);

        // Step 3: Start a return
        var returnId = Guid.CreateVersion7();
        var returnRequested = new ReturnRequested(
            returnId,
            orderId,
            customerId,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteOnHostAndWaitAsync(
            _fixture.ReturnsHost,
            returnRequested,
            timeoutSeconds: 30);

        // Step 4: Fire the return window expiry (simulates 30 days passing)
        var returnWindowExpired = new ReturnWindowExpired(orderId);

        await _fixture.ExecuteOnHostAndWaitAsync(
            _fixture.OrdersHost,
            returnWindowExpired,
            timeoutSeconds: 30);

        // Verify saga stayed open (return still in progress)
        using var ordersSession1 = _fixture.GetOrdersSession();
        var orderAfterWindowExpired = await ordersSession1.LoadAsync<Order>(orderId);
        orderAfterWindowExpired.ShouldNotBeNull();
        orderAfterWindowExpired.ReturnWindowFired.ShouldBeTrue();
        orderAfterWindowExpired.Status.ShouldBe(OrderStatus.Delivered); // NOT closed yet
        orderAfterWindowExpired.IsCompleted().ShouldBeFalse();

        // Step 5: Complete the return AFTER window expired
        var returnCompleted = new ReturnCompleted(
            returnId,
            orderId,
            customerId,
            FinalRefundAmount: 30.00m,
            Items: [],
            DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteOnHostAndWaitAsync(
            _fixture.ReturnsHost,
            returnCompleted,
            timeoutSeconds: 30);

        // Assert - Saga should now be closed (window expired + no active returns = close)
        using var ordersSession2 = _fixture.GetOrdersSession();
        var orderFinal = await ordersSession2.LoadAsync<Order>(orderId);

        orderFinal.ShouldNotBeNull();
        orderFinal.ActiveReturnIds.Count.ShouldBe(0);
        orderFinal.Status.ShouldBe(OrderStatus.Closed); // Now closed
        orderFinal.IsCompleted().ShouldBeTrue(); // Saga lifecycle complete
    }

    // ---------------------------------------------------------------------------
    // Helper Methods
    // ---------------------------------------------------------------------------

    private static Messages.Contracts.Shopping.CartCheckoutCompleted CreateCheckoutCompletedMessage(
        Guid orderId,
        Guid customerId)
    {
        var lineItems = new List<Messages.Contracts.Shopping.CheckoutLineItem>
        {
            new("SKU-001", 2, 19.99m),
            new("SKU-002", 1, 29.99m)
        };

        var address = new Messages.Contracts.CustomerIdentity.AddressSnapshot(
            "123 Main St",
            null,
            "Springfield",
            "IL",
            "62701",
            "USA");

        return new Messages.Contracts.Shopping.CartCheckoutCompleted(
            orderId,
            Guid.CreateVersion7(),
            customerId,
            lineItems,
            address,
            "Standard",
            5.99m,
            "tok_visa_4242",
            DateTimeOffset.UtcNow);
    }
}
