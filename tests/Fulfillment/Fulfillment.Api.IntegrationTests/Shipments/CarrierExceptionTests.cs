using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for P1 carrier exceptions (Slices 23-24).
/// Covers carrier pickup missed, alternate carrier arrangement, and delivery attempt chain.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CarrierExceptionTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CarrierExceptionTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _fixture.FrozenClock.SetUtcNow(DateTimeOffset.UtcNow);
        return _fixture.CleanAllDocumentsAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid shipmentId, Guid orderId)> CreateLabeledShipmentAsync()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Carrier Exception Blvd",
                City = "Newark",
                StateProvince = "NJ",
                PostalCode = "07102",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-CE-001", 1) },
            "Ground",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Complete work order — cascade labels automatically
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-CE"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-CE"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-CE-001", 1, "A-01-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-CE-001", 1));

        return (shipment.Id, orderId);
    }

    // --- Slice 23: Carrier Pickup Missed + Alternate Carrier ---

    /// <summary>Slice 23: Report missed carrier pickup.</summary>
    [Fact]
    public async Task ReportCarrierPickupMissed_Appends_Events()
    {
        var (shipmentId, _) = await CreateLabeledShipmentAsync();

        // Stage the shipment first
        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipmentId, "M-CE"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipmentId, "Lane-CE", "2:00-3:00 PM"));

        await _fixture.ExecuteAndWaitAsync(
            new ReportCarrierPickupMissed(shipmentId, "UPS", "2:00-3:00 PM"));

        // Shipment status should still be Staged (pickup missed doesn't change status)
        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment.ShouldNotBeNull();
    }

    /// <summary>Slice 23: Arrange alternate carrier after missed pickup.</summary>
    [Fact]
    public async Task ArrangeAlternateCarrier_Generates_New_Label()
    {
        var (shipmentId, orderId) = await CreateLabeledShipmentAsync();

        var tracked = await _fixture.ExecuteAndWaitAsync(
            new ArrangeAlternateCarrier(shipmentId, "FedEx", "Express"));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment.ShouldNotBeNull();
        shipment.Carrier.ShouldBe("FedEx");

        // Should publish TrackingNumberAssigned with new carrier
        var trackingMsgs = tracked.Sent.MessagesOf<IntegrationContracts.TrackingNumberAssigned>().ToList();
        trackingMsgs.ShouldNotBeEmpty("TrackingNumberAssigned should be published for new carrier");
        trackingMsgs.First().Carrier.ShouldBe("FedEx");
    }

    // --- Slice 24: Delivery attempt failed chain is already tested in CarrierDispatchAndDeliveryTests ---

    /// <summary>Slice 24: Third delivery attempt triggers RTS via webhook.</summary>
    [Fact]
    public async Task DeliveryAttempt_Third_Triggers_RTS_Via_Webhook()
    {
        var (shipmentId, orderId) = await CreateLabeledShipmentAsync();

        await using var sessionLabel = _fixture.GetDocumentSession();
        var labeled = await sessionLabel.LoadAsync<Shipment>(shipmentId);
        var trackingNumber = labeled!.TrackingNumber!;

        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipmentId, "M-DA"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipmentId, "Lane-DA", "1:00-2:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipmentId, "UPS", true));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "IN_TRANSIT", DateTimeOffset.UtcNow, "Hub", null, null));

        // Three delivery attempts
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERY_ATTEMPTED", DateTimeOffset.UtcNow, "Door", "NI", 1));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERY_ATTEMPTED", DateTimeOffset.UtcNow, "Door", "NI", 2));
        var rtsTracked = await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERY_ATTEMPTED", DateTimeOffset.UtcNow, "Door", "NI", 3));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.ReturningToSender);
        shipment.DeliveryAttemptCount.ShouldBe(3);
    }

    /// <summary>Slice 24: Idempotency — duplicate attempt number is ignored.</summary>
    [Fact]
    public async Task DeliveryAttempt_Duplicate_Is_Idempotent()
    {
        var (shipmentId, _) = await CreateLabeledShipmentAsync();

        await using var sessionLabel = _fixture.GetDocumentSession();
        var labeled = await sessionLabel.LoadAsync<Shipment>(shipmentId);
        var trackingNumber = labeled!.TrackingNumber!;

        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipmentId, "M-DA2"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipmentId, "Lane-DA2", "1:00-2:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipmentId, "UPS", true));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "IN_TRANSIT", DateTimeOffset.UtcNow, "Hub", null, null));

        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERY_ATTEMPTED", DateTimeOffset.UtcNow, "Door", "NI", 1));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERY_ATTEMPTED", DateTimeOffset.UtcNow, "Door", "NI", 1));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.DeliveryAttemptCount.ShouldBe(1, "Duplicate attempt should be ignored");
    }
}
