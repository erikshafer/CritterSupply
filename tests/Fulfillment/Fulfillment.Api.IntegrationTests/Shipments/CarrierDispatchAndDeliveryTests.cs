using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for Track B: Carrier dispatch (Slices 10-12) and
/// delivery tracking (Slices 13-15).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CarrierDispatchAndDeliveryTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CarrierDispatchAndDeliveryTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Creates a shipment with a completed work order. The PackingCompleted → GenerateShippingLabel
    /// cascading policy will automatically label the shipment.
    /// </summary>
    private async Task<(Guid shipmentId, Guid orderId)> CreateLabeledShipmentAsync()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 Carrier Way",
                City = "Newark",
                StateProvince = "NJ",
                PostalCode = "07102",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-SHIP-001", 1) },
            "Ground",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Complete the work order — PackingCompleted cascades to GenerateShippingLabel
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-CARRIER"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Carrier"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-SHIP-001", 1, "A-01-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-SHIP-001", 1));

        return (shipment.Id, orderId);
    }

    /// <summary>Slice 10: Cascading policy generates shipping label and tracking number automatically.</summary>
    [Fact]
    public async Task PackingCompleted_Cascade_Creates_Label_And_Tracking()
    {
        var (shipmentId, _) = await CreateLabeledShipmentAsync();

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment.ShouldNotBeNull();
        shipment.Status.ShouldBe(ShipmentStatus.Labeled);
        shipment.Carrier.ShouldNotBeNullOrEmpty();
        shipment.TrackingNumber.ShouldNotBeNullOrEmpty();
    }

    /// <summary>Slice 10: Label generation publishes TrackingNumberAssigned integration event via cascade.</summary>
    [Fact]
    public async Task PackingCompleted_Cascade_Publishes_TrackingNumberAssigned()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 Carrier Way",
                City = "Newark",
                StateProvince = "NJ",
                PostalCode = "07102",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-SHIP-001", 1) },
            "Ground",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-TRACK"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Track"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-SHIP-001", 1, "A-01-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));

        // VerifyItemAtPack completes packing → cascades to GenerateShippingLabel
        var tracked = await _fixture.ExecuteAndWaitAsync(
            new VerifyItemAtPack(workOrderId, "SKU-SHIP-001", 1));

        var sent = tracked.Sent.MessagesOf<IntegrationContracts.TrackingNumberAssigned>().ToList();
        sent.ShouldNotBeEmpty("TrackingNumberAssigned should be published via cascade");
        sent.First().OrderId.ShouldBe(orderId);
    }

    /// <summary>Slice 11: Manifest and stage package.</summary>
    [Fact]
    public async Task Manifest_And_Stage_Package()
    {
        var (shipmentId, _) = await CreateLabeledShipmentAsync();

        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipmentId, "MANIFEST-001"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipmentId, "Lane-A", "2:00-3:00 PM"));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.Staged);
    }

    /// <summary>Slice 12: Carrier pickup confirmation publishes dual messages.</summary>
    [Fact]
    public async Task ConfirmCarrierPickup_Publishes_DualMessages()
    {
        var (shipmentId, orderId) = await CreateLabeledShipmentAsync();

        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipmentId, "M-001"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipmentId, "Lane-B", "1:00-2:00 PM"));

        var tracked = await _fixture.ExecuteAndWaitAsync(
            new ConfirmCarrierPickup(shipmentId, "UPS", true));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.HandedToCarrier);

        // Should publish new ShipmentHandedToCarrier
        var newMsg = tracked.Sent.MessagesOf<IntegrationContracts.ShipmentHandedToCarrier>().ToList();
        newMsg.ShouldNotBeEmpty("ShipmentHandedToCarrier should be published");
        newMsg.First().OrderId.ShouldBe(orderId);

        // MIGRATION: Should also publish legacy ShipmentDispatched
        var legacyMsg = tracked.Sent.MessagesOf<IntegrationContracts.ShipmentDispatched>().ToList();
        legacyMsg.ShouldNotBeEmpty("Legacy ShipmentDispatched should be dual-published");
        legacyMsg.First().OrderId.ShouldBe(orderId);
    }

    /// <summary>Slices 13-15: Carrier webhook events (in-transit → out-for-delivery → delivered).</summary>
    [Fact]
    public async Task CarrierWebhook_Delivery_Happy_Path()
    {
        var (shipmentId, orderId) = await CreateLabeledShipmentAsync();

        // Get tracking number (assigned via cascade)
        await using var session1 = _fixture.GetDocumentSession();
        var labeledShipment = await session1.LoadAsync<Shipment>(shipmentId);
        var trackingNumber = labeledShipment!.TrackingNumber!;

        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipmentId, "M-002"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipmentId, "Lane-C", "3:00-4:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipmentId, "UPS", true));

        // Slice 13: In-transit
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "IN_TRANSIT", DateTimeOffset.UtcNow, "Edison, NJ", null, null));

        await using var session2 = _fixture.GetDocumentSession();
        var transitShipment = await session2.LoadAsync<Shipment>(shipmentId);
        transitShipment!.Status.ShouldBe(ShipmentStatus.InTransit);
        transitShipment.LastScanLocation.ShouldBe("Edison, NJ");

        // Slice 14: Out for delivery
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "OUT_FOR_DELIVERY", DateTimeOffset.UtcNow, "Newark, NJ", null, null));

        await using var session3 = _fixture.GetDocumentSession();
        var ofdShipment = await session3.LoadAsync<Shipment>(shipmentId);
        ofdShipment!.Status.ShouldBe(ShipmentStatus.OutForDelivery);

        // Slice 15: Delivered
        var deliveredTracked = await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERED", DateTimeOffset.UtcNow, "Newark, NJ", null, null));

        await using var session4 = _fixture.GetDocumentSession();
        var deliveredShipment = await session4.LoadAsync<Shipment>(shipmentId);
        deliveredShipment!.Status.ShouldBe(ShipmentStatus.Delivered);
        deliveredShipment.DeliveredAt.ShouldNotBeNull();

        // Verify ShipmentDelivered integration event
        var deliveredMsgs = deliveredTracked.Sent.MessagesOf<IntegrationContracts.ShipmentDelivered>().ToList();
        deliveredMsgs.ShouldNotBeEmpty("ShipmentDelivered should be published");
        deliveredMsgs.First().OrderId.ShouldBe(orderId);
    }

    /// <summary>Delivery attempt failures with RTS after 3 attempts.</summary>
    [Fact]
    public async Task CarrierWebhook_DeliveryAttemptFailed_Three_Times_Triggers_RTS()
    {
        var (shipmentId, orderId) = await CreateLabeledShipmentAsync();

        await using var sessionLabel = _fixture.GetDocumentSession();
        var labeledShipment = await sessionLabel.LoadAsync<Shipment>(shipmentId);
        var trackingNumber = labeledShipment!.TrackingNumber!;

        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipmentId, "M-003"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipmentId, "Lane-D", "1:00-2:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipmentId, "UPS", true));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "IN_TRANSIT", DateTimeOffset.UtcNow, "Hub", null, null));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "OUT_FOR_DELIVERY", DateTimeOffset.UtcNow, "Local", null, null));

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

        // Verify ReturnToSenderInitiated published
        var rtsMsgs = rtsTracked.Sent.MessagesOf<IntegrationContracts.ReturnToSenderInitiated>().ToList();
        rtsMsgs.ShouldNotBeEmpty("ReturnToSenderInitiated should be published");
        rtsMsgs.First().OrderId.ShouldBe(orderId);
        rtsMsgs.First().TotalAttempts.ShouldBe(3);
    }

    /// <summary>Idempotency: duplicate carrier webhook for same attempt number is ignored.</summary>
    [Fact]
    public async Task CarrierWebhook_Duplicate_AttemptNumber_Is_Idempotent()
    {
        var (shipmentId, _) = await CreateLabeledShipmentAsync();

        await using var sessionLabel = _fixture.GetDocumentSession();
        var labeledShipment = await sessionLabel.LoadAsync<Shipment>(shipmentId);
        var trackingNumber = labeledShipment!.TrackingNumber!;

        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipmentId, "M-004"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipmentId, "Lane-E", "2:00-3:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipmentId, "UPS", true));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "IN_TRANSIT", DateTimeOffset.UtcNow, "Hub", null, null));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "OUT_FOR_DELIVERY", DateTimeOffset.UtcNow, "Local", null, null));

        // First attempt
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERY_ATTEMPTED", DateTimeOffset.UtcNow, "Door", "NI", 1));

        // Duplicate attempt
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERY_ATTEMPTED", DateTimeOffset.UtcNow, "Door", "NI", 1));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.DeliveryAttemptCount.ShouldBe(1, "Duplicate attempt should be ignored");
    }

    /// <summary>ShipmentStatusView tracks the full lifecycle.</summary>
    [Fact]
    public async Task ShipmentStatusView_Tracks_Full_Lifecycle()
    {
        var (shipmentId, _) = await CreateLabeledShipmentAsync();

        await using var session1 = _fixture.GetDocumentSession();
        var view = await session1.LoadAsync<ShipmentStatusView>(shipmentId);
        view.ShouldNotBeNull();
        view.Status.ShouldBe("Labeled");
        view.TrackingNumber.ShouldNotBeNullOrEmpty();
        view.Carrier.ShouldBe("UPS");
        view.StatusHistory.Count.ShouldBeGreaterThanOrEqualTo(3); // Pending, Assigned, Labeled + Tracking
    }

    /// <summary>Full end-to-end: Slices 1-15 happy path.</summary>
    [Fact]
    public async Task Complete_Fulfillment_Lifecycle_Slices_1_Through_15()
    {
        var (shipmentId, orderId) = await CreateLabeledShipmentAsync();

        await using var sessionLabel = _fixture.GetDocumentSession();
        var labeledShipment = await sessionLabel.LoadAsync<Shipment>(shipmentId);
        var trackingNumber = labeledShipment!.TrackingNumber!;

        // Slice 11: Manifest + Stage
        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipmentId, "M-E2E"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipmentId, "Lane-F", "4:00-5:00 PM"));

        // Slice 12: Carrier pickup
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipmentId, "UPS", true));

        // Slice 13: In-transit
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "IN_TRANSIT", DateTimeOffset.UtcNow, "Memphis Hub", null, null));

        // Slice 14: Out for delivery
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "OUT_FOR_DELIVERY", DateTimeOffset.UtcNow, "Newark", null, null));

        // Slice 15: Delivered
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERED", DateTimeOffset.UtcNow, "Newark", null, null));

        await using var session = _fixture.GetDocumentSession();
        var finalShipment = await session.LoadAsync<Shipment>(shipmentId);
        finalShipment!.Status.ShouldBe(ShipmentStatus.Delivered);
        finalShipment.DeliveredAt.ShouldNotBeNull();
        finalShipment.IsTerminal.ShouldBeTrue();
    }
}
