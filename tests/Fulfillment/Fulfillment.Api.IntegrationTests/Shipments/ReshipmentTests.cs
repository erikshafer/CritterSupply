using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for Slice 30 (Reshipment Creation) and Slice 31 (Delivery Dispute).
/// Covers dual-stream writes, terminal state transitions, and cascade flows.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ReshipmentTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ReshipmentTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _fixture.FrozenClock.SetUtcNow(DateTimeOffset.UtcNow);
        return _fixture.CleanAllDocumentsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid shipmentId, Guid orderId)> CreateLostInTransitShipmentAsync()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Reship Way", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-RESHIP-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Complete work order → cascade labels → manifest → stage → hand to carrier
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-RESHIP"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Reship"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-RESHIP-001", 1, "A-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-RESHIP-001", 1));
        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipment.Id, "M-RESHIP"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipment.Id, "Lane-RESHIP", "1:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipment.Id, "UPS", true));

        // Advance clock and detect lost in transit
        _fixture.FrozenClock.Advance(TimeSpan.FromDays(8));
        await _fixture.ExecuteAndWaitAsync(new CheckForLostShipment(shipment.Id));

        return (shipment.Id, orderId);
    }

    private async Task<(Guid shipmentId, Guid orderId, string trackingNumber)> CreateDeliveredShipmentAsync()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 Dispute Ave", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-DISPUTE-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Full lifecycle to Delivered
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-DISP"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Disp"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-DISPUTE-001", 1, "A-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-DISPUTE-001", 1));

        await using var session2 = _fixture.GetDocumentSession();
        var labeled = await session2.LoadAsync<Shipment>(shipment.Id);
        var trackingNumber = labeled!.TrackingNumber!;

        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipment.Id, "M-DISP"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipment.Id, "Lane-DISP", "1:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipment.Id, "UPS", true));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERED", DateTimeOffset.UtcNow, null, null, null));

        return (shipment.Id, orderId, trackingNumber);
    }

    // --- Slice 30: Reshipment Creation ---

    [Fact]
    public async Task CreateReshipment_LostInTransit_Creates_New_Shipment_Stream()
    {
        var (shipmentId, orderId) = await CreateLostInTransitShipmentAsync();

        var tracked = await _fixture.ExecuteAndWaitAsync(
            new CreateReshipment(shipmentId, ReshipmentReasons.LostInTransit));

        // Original shipment should be in LostReplacementShipped
        await using var session = _fixture.GetDocumentSession();
        var original = await session.LoadAsync<Shipment>(shipmentId);
        original!.Status.ShouldBe(ShipmentStatus.LostReplacementShipped);

        // Integration event should be published
        var reshipMsgs = tracked.Sent.MessagesOf<IntegrationContracts.ReshipmentCreated>().ToList();
        reshipMsgs.ShouldNotBeEmpty("ReshipmentCreated integration event should be published");
        reshipMsgs.First().OrderId.ShouldBe(orderId);
        reshipMsgs.First().OriginalShipmentId.ShouldBe(shipmentId);
    }

    [Fact]
    public async Task CreateReshipment_Wrong_Status_Is_Rejected()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "300 Reject St", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-REJECT-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);

        // Assigned status — should be rejected
        await _fixture.ExecuteAndWaitAsync(new CreateReshipment(shipment.Id, ReshipmentReasons.LostInTransit));

        await using var session2 = _fixture.GetDocumentSession();
        var unchanged = await session2.LoadAsync<Shipment>(shipment.Id);
        unchanged!.Status.ShouldBe(ShipmentStatus.Assigned);
    }

    // --- Slice 31: Delivery Dispute ---

    [Fact]
    public async Task DisputeDelivery_Creates_Reshipment_Cascade()
    {
        var (shipmentId, orderId, _) = await CreateDeliveredShipmentAsync();

        var tracked = await _fixture.ExecuteAndWaitAsync(
            new DisputeDelivery(shipmentId, Guid.NewGuid()));

        // Original shipment should have DeliveryDisputed → ReshipmentCreated applied
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(shipmentId);
        events.Select(e => e.Data).OfType<DeliveryDisputed>().ShouldNotBeEmpty();
        events.Select(e => e.Data).OfType<ReshipmentCreated>().ShouldNotBeEmpty();

        // ReshipmentCreated integration event should be published
        var reshipMsgs = tracked.Sent.MessagesOf<IntegrationContracts.ReshipmentCreated>().ToList();
        reshipMsgs.ShouldNotBeEmpty("ReshipmentCreated should be published after dispute");
    }

    [Fact]
    public async Task DisputeDelivery_Wrong_Status_Is_Rejected()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "400 NoDispute", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-NODISP-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);

        // Assigned status — should be rejected
        await _fixture.ExecuteAndWaitAsync(new DisputeDelivery(shipment.Id, Guid.NewGuid()));

        await using var session2 = _fixture.GetDocumentSession();
        var unchanged = await session2.LoadAsync<Shipment>(shipment.Id);
        unchanged!.Status.ShouldBe(ShipmentStatus.Assigned);
    }
}
