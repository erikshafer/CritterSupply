using Messages.Contracts.Fulfillment;
using Returns.ReturnProcessing;

namespace Returns.Api.IntegrationTests.CrossBcSmokeTests;

/// <summary>
/// Smoke tests verifying Fulfillment → Returns integration pipeline.
/// Tests that ShipmentDelivered messages from Fulfillment BC successfully create
/// ReturnEligibilityWindow documents in Returns BC via RabbitMQ.
/// </summary>
[Collection(nameof(CrossBcTestCollection))]
public class FulfillmentToReturnsPipelineTests(CrossBcTestFixture fixture)
{
    private readonly CrossBcTestFixture _fixture = fixture;

    [Fact]
    public async Task ShipmentDelivered_Creates_ReturnEligibilityWindow_In_Returns_BC()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var shipmentId = Guid.CreateVersion7();
        var deliveredAt = DateTimeOffset.UtcNow;

        // IMPORTANT: Create Order saga first (Orders BC also handles ShipmentDelivered)
        // CheckoutCompleted is internal routing, so create saga directly in database
        var checkoutCompleted = CreateCheckoutCompletedMessage(orderId, customerId);
        await _fixture.CreateOrderSagaAsync(orderId, customerId, checkoutCompleted);

        var shipmentDelivered = new ShipmentDelivered(
            orderId,
            shipmentId,
            deliveredAt,
            RecipientName: "John Doe");

        // Act - Publish from Fulfillment BC
        var tracked = await _fixture.ExecuteOnHostAndWaitAsync(
            _fixture.FulfillmentHost,
            shipmentDelivered,
            timeoutSeconds: 30);

        // Assert - Verify message was published to RabbitMQ
        tracked.Sent.SingleMessage<ShipmentDelivered>()
            .OrderId.ShouldBe(orderId);

        // Assert - Verify ReturnEligibilityWindow created in Returns BC database
        using var returnsSession = _fixture.GetReturnsSession();
        var eligibilityWindow = await returnsSession.LoadAsync<ReturnEligibilityWindow>(orderId);

        eligibilityWindow.ShouldNotBeNull();
        eligibilityWindow.OrderId.ShouldBe(orderId);
        eligibilityWindow.DeliveredAt.ShouldBe(deliveredAt);
        eligibilityWindow.WindowExpiresAt.ShouldBe(deliveredAt.AddDays(ReturnEligibilityWindow.ReturnWindowDays));
        eligibilityWindow.IsExpired.ShouldBeFalse();
    }

    [Fact(Skip = "Blocked by Wolverine saga persistence issue — saga created via InvokeAsync() is not found by subsequent handlers in multi-host tests. See docs/wolverine-saga-persistence-issue.md")]
    public async Task ShipmentDelivered_Is_Idempotent_When_Redelivered()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var shipmentId = Guid.CreateVersion7();
        var firstDeliveredAt = DateTimeOffset.UtcNow.AddHours(-2);

        // IMPORTANT: Create Order saga first (Orders BC also handles ShipmentDelivered)
        // CheckoutCompleted is internal routing, so create saga directly in database
        var checkoutCompleted = CreateCheckoutCompletedMessage(orderId, customerId);
        await _fixture.CreateOrderSagaAsync(orderId, customerId, checkoutCompleted);

        var firstMessage = new ShipmentDelivered(
            orderId,
            shipmentId,
            firstDeliveredAt);

        // Act - Deliver first message
        await _fixture.ExecuteOnHostAndWaitAsync(
            _fixture.FulfillmentHost,
            firstMessage,
            timeoutSeconds: 30);

        // Capture first window state
        using var session1 = _fixture.GetReturnsSession();
        var firstWindow = await session1.LoadAsync<ReturnEligibilityWindow>(orderId);
        var firstWindowExpiry = firstWindow!.WindowExpiresAt;

        // Act - Redeliver same message (RabbitMQ redelivery scenario)
        var secondMessage = new ShipmentDelivered(
            orderId,
            shipmentId,
            DateTimeOffset.UtcNow); // Different timestamp!

        await _fixture.ExecuteOnHostAndWaitAsync(
            _fixture.FulfillmentHost,
            secondMessage,
            timeoutSeconds: 30);

        // Assert - Second delivery should NOT change the window
        using var session2 = _fixture.GetReturnsSession();
        var secondWindow = await session2.LoadAsync<ReturnEligibilityWindow>(orderId);

        secondWindow.ShouldNotBeNull();
        secondWindow.DeliveredAt.ShouldBe(firstDeliveredAt); // Original timestamp preserved
        secondWindow.WindowExpiresAt.ShouldBe(firstWindowExpiry); // Expiry unchanged
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
