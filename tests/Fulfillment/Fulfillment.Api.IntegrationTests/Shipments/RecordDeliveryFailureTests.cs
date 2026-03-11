using Fulfillment.Shipments;
using Marten;
using Marten.Linq;
using Shouldly;
using Wolverine.Tracking;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for the RecordDeliveryFailure endpoint (P0).
/// Covers success, 404 for missing shipment, 400 for invalid state transition,
/// and integration message publishing.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class RecordDeliveryFailureTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public RecordDeliveryFailureTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private async Task<Guid> CreatePendingShipmentAsync(Guid orderId)
    {
        var customerId = Guid.NewGuid();
        var address = new ShippingAddress("123 Test St", null, "Seattle", "WA", "98101", "USA");
        var command = new RequestFulfillment(
            orderId,
            customerId,
            address,
            new List<FulfillmentLineItem> { new("SKU-RDF-001", 2) },
            "Standard");

        await _fixture.ExecuteAndWaitAsync(command);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        return shipment.Id;
    }

    private async Task<(Guid orderId, Guid shipmentId)> CreateShippedShipmentAsync()
    {
        var orderId = Guid.NewGuid();
        var shipmentId = await CreatePendingShipmentAsync(orderId);

        await _fixture.ExecuteAndWaitAsync(new AssignWarehouse(shipmentId, "WH-TEST-01"));
        await _fixture.ExecuteAndWaitAsync(new DispatchShipment(shipmentId, "FedEx", "FEDEX-TRACK-001"));

        // Verify it reached Shipped status
        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.Shipped);

        return (orderId, shipmentId);
    }

    // ---------------------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Full lifecycle: create → assign → dispatch → record failure.
    /// Verifies the shipment transitions to DeliveryFailed status with the correct reason.
    /// </summary>
    [Fact]
    public async Task RecordDeliveryFailure_For_Shipped_Shipment_Succeeds()
    {
        // Arrange
        var (orderId, shipmentId) = await CreateShippedShipmentAsync();

        // Act
        var command = new RecordDeliveryFailure(shipmentId, "Recipient unavailable — notice left");
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert: status = DeliveryFailed, reason persisted
        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment.ShouldNotBeNull();
        shipment.Status.ShouldBe(ShipmentStatus.DeliveryFailed);
        shipment.FailureReason.ShouldBe("Recipient unavailable — notice left");
    }

    // ---------------------------------------------------------------------------
    // Error cases (HTTP-level)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Calling the endpoint for a shipment that was never created must return HTTP 404.
    /// </summary>
    [Fact]
    public async Task RecordDeliveryFailure_For_NonExistent_Shipment_Returns_404()
    {
        var nonExistentShipmentId = Guid.NewGuid();

        await _fixture.Host.Scenario(s =>
        {
            s.Post
                .Json(new { ShipmentId = nonExistentShipmentId, Reason = "Failed to deliver" })
                .ToUrl($"/api/fulfillment/shipments/{nonExistentShipmentId}/record-delivery-failure");
            s.StatusCodeShouldBe(404);
        });
    }

    /// <summary>
    /// Cannot record a delivery failure for a Pending shipment — must return HTTP 400.
    /// The shipment must be dispatched (Shipped status) before a failure can be recorded.
    /// </summary>
    [Fact]
    public async Task RecordDeliveryFailure_For_Pending_Shipment_Returns_400()
    {
        // Arrange: create a shipment but do NOT assign or dispatch it
        var orderId = Guid.NewGuid();
        var shipmentId = await CreatePendingShipmentAsync(orderId);

        // Act + Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Post
                .Json(new { Reason = "Cannot fail a pending shipment" })
                .ToUrl($"/api/fulfillment/shipments/{shipmentId}/record-delivery-failure");
            s.StatusCodeShouldBe(400);
        });
    }

    // ---------------------------------------------------------------------------
    // Integration message publishing
    // ---------------------------------------------------------------------------

    /// <summary>
    /// After recording a delivery failure, Wolverine must publish a
    /// <see cref="Messages.Contracts.Fulfillment.ShipmentDeliveryFailed"/> integration message
    /// for downstream consumers (Orders saga, Storefront).
    /// Verifies the message carries correct OrderId, ShipmentId, and Reason.
    /// </summary>
    [Fact]
    public async Task RecordDeliveryFailure_Publishes_ShipmentDeliveryFailed_Integration_Message()
    {
        // Arrange
        var (orderId, shipmentId) = await CreateShippedShipmentAsync();
        const string failureReason = "Address not found — building demolished";

        // Act: execute and capture tracked session
        var command = new RecordDeliveryFailure(shipmentId, failureReason);
        var tracked = await _fixture.ExecuteAndWaitAsync(command);

        // Assert: integration message published in outgoing messages
        var sent = tracked.Sent.MessagesOf<IntegrationContracts.ShipmentDeliveryFailed>().ToList();
        sent.ShouldNotBeEmpty("ShipmentDeliveryFailed integration message was not published");

        var msg = sent.First();
        msg.OrderId.ShouldBe(orderId);
        msg.ShipmentId.ShouldBe(shipmentId);
        msg.Reason.ShouldBe(failureReason);
        msg.FailedAt.ShouldBeInRange(
            DateTimeOffset.UtcNow.AddSeconds(-30),
            DateTimeOffset.UtcNow.AddSeconds(5));
    }
}
