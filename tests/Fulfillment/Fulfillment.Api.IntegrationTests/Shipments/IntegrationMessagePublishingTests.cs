using Fulfillment.Shipments;
using Marten;
using Marten.Linq;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests verifying that Dispatch and ConfirmDelivery publish the correct
/// integration messages (P0 — RabbitMQ transport and outgoing message correctness).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class IntegrationMessagePublishingTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public IntegrationMessagePublishingTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private async Task<(Guid orderId, Guid shipmentId)> CreateAssignedShipmentAsync()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var address = new ShippingAddress("100 Integration Ave", null, "Portland", "OR", "97201", "USA");

        await _fixture.ExecuteAndWaitAsync(new RequestFulfillment(
            orderId, customerId, address,
            new List<FulfillmentLineItem> { new("SKU-IMP-001", 1) },
            "Express"));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var shipmentId = shipment.Id;

        await _fixture.ExecuteAndWaitAsync(new AssignWarehouse(shipmentId, "WH-IMP-01"));
        return (orderId, shipmentId);
    }

    // ---------------------------------------------------------------------------
    // DispatchShipment publishes ShipmentDispatched
    // ---------------------------------------------------------------------------

    /// <summary>
    /// After dispatching a shipment, a <see cref="Messages.Contracts.Fulfillment.ShipmentDispatched"/>
    /// integration message must be published for the Orders saga and Storefront consumers.
    /// </summary>
    [Fact]
    public async Task Dispatch_Publishes_ShipmentDispatched_Integration_Message()
    {
        // Arrange: create and assign a shipment
        var (orderId, shipmentId) = await CreateAssignedShipmentAsync();
        const string carrier = "UPS";
        const string trackingNumber = "1Z999AA10123456784";

        // Act: dispatch and capture tracked session
        var tracked = await _fixture.ExecuteAndWaitAsync(
            new DispatchShipment(shipmentId, carrier, trackingNumber));

        // Assert: ShipmentDispatched integration message was published
        // Two copies are expected — one routed to orders-fulfillment-events, one to storefront-fulfillment-events.
        var sent = tracked.Sent.MessagesOf<IntegrationContracts.ShipmentDispatched>().ToList();
        sent.ShouldNotBeEmpty("ShipmentDispatched integration message was not published");

        var msg = sent.First();
        msg.OrderId.ShouldBe(orderId);
        msg.ShipmentId.ShouldBe(shipmentId);
        msg.Carrier.ShouldBe(carrier);
        msg.TrackingNumber.ShouldBe(trackingNumber);
        msg.DispatchedAt.ShouldBeInRange(
            DateTimeOffset.UtcNow.AddSeconds(-30),
            DateTimeOffset.UtcNow.AddSeconds(5));
    }

    // ---------------------------------------------------------------------------
    // ConfirmDelivery publishes ShipmentDelivered
    // ---------------------------------------------------------------------------

    /// <summary>
    /// After confirming delivery, a <see cref="Messages.Contracts.Fulfillment.ShipmentDelivered"/>
    /// integration message must be published. The Orders saga listens for this to transition
    /// to Delivered status and schedule the return window.
    /// </summary>
    [Fact]
    public async Task ConfirmDelivery_Publishes_ShipmentDelivered_Integration_Message()
    {
        // Arrange: create, assign, dispatch
        var (orderId, shipmentId) = await CreateAssignedShipmentAsync();

        await _fixture.ExecuteAndWaitAsync(
            new DispatchShipment(shipmentId, "FedEx", "FX-TRACK-12345"));

        // Verify dispatched
        await using var session = _fixture.GetDocumentSession();
        var dispatchedShipment = await session.LoadAsync<Shipment>(shipmentId);
        dispatchedShipment!.Status.ShouldBe(ShipmentStatus.Shipped);

        // Act: confirm delivery and capture tracked session
        const string recipientName = "Jane Doe";
        var tracked = await _fixture.ExecuteAndWaitAsync(
            new ConfirmDelivery(shipmentId, recipientName));

        // Assert: ShipmentDelivered integration message was published
        var sent = tracked.Sent.MessagesOf<IntegrationContracts.ShipmentDelivered>().ToList();
        sent.ShouldNotBeEmpty("ShipmentDelivered integration message was not published");

        var msg = sent.First();
        msg.OrderId.ShouldBe(orderId);
        msg.ShipmentId.ShouldBe(shipmentId);
        msg.RecipientName.ShouldBe(recipientName);
        msg.DeliveredAt.ShouldBeInRange(
            DateTimeOffset.UtcNow.AddSeconds(-30),
            DateTimeOffset.UtcNow.AddSeconds(5));
    }
}
