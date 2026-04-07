using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for Slices 38 (Rate Dispute) and 39 (3PL Handoff).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class RateDisputeAnd3PLTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public RateDisputeAnd3PLTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _fixture.FrozenClock.SetUtcNow(DateTimeOffset.UtcNow);
        return _fixture.CleanAllDocumentsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid shipmentId, string trackingNumber)> CreateDeliveredShipmentAsync()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Rate St", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-RATE-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-RATE"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Rate"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-RATE-001", 1, "A-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-RATE-001", 1));

        await using var session2 = _fixture.GetDocumentSession();
        var labeled = await session2.LoadAsync<Shipment>(shipment.Id);
        var trackingNumber = labeled!.TrackingNumber!;

        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipment.Id, "M-RATE"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipment.Id, "Lane-RATE", "1:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipment.Id, "UPS", true));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERED", DateTimeOffset.UtcNow, null, null, null));

        return (shipment.Id, trackingNumber);
    }

    // --- Slice 38: Rate Dispute ---

    [Fact]
    public async Task RaiseRateDispute_Delivered_Appends_Event()
    {
        var (shipmentId, _) = await CreateDeliveredShipmentAsync();

        await _fixture.ExecuteAndWaitAsync(
            new RaiseRateDispute(shipmentId, "RD-001", 5.5m, 8.2m, "UPS"));

        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(shipmentId);
        var raised = events.Select(e => e.Data).OfType<RateDisputeRaised>().FirstOrDefault();
        raised.ShouldNotBeNull();
        raised.DisputeId.ShouldBe("RD-001");
        raised.ClaimedWeight.ShouldBe(8.2m);
    }

    [Fact]
    public async Task ResolveRateDispute_After_Raise_Appends_Resolution()
    {
        var (shipmentId, _) = await CreateDeliveredShipmentAsync();

        await _fixture.ExecuteAndWaitAsync(
            new RaiseRateDispute(shipmentId, "RD-002", 5.5m, 8.2m, "UPS"));
        await _fixture.ExecuteAndWaitAsync(
            new ResolveRateDispute(shipmentId, "RD-002", "Settled", 25.00m));

        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(shipmentId);
        var resolved = events.Select(e => e.Data).OfType<RateDisputeResolved>().FirstOrDefault();
        resolved.ShouldNotBeNull();
        resolved.Resolution.ShouldBe("Settled");
        resolved.AdjustedAmountUSD.ShouldBe(25.00m);
    }

    [Fact]
    public async Task RaiseRateDispute_Wrong_Status_Is_Rejected()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 No Rate", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-NORATE-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);

        // Assigned status — should be rejected
        await _fixture.ExecuteAndWaitAsync(
            new RaiseRateDispute(shipment.Id, "RD-FAIL", 5.5m, 8.2m, "UPS"));

        await using var session2 = _fixture.GetDocumentSession();
        var events = await session2.Events.FetchStreamAsync(shipment.Id);
        events.Select(e => e.Data).OfType<RateDisputeRaised>().ShouldBeEmpty();
    }

    // --- Slice 39: 3PL Handoff ---

    [Fact]
    public async Task HandoffTo3PL_TXFC_Transitions_To_HandedToThirdParty()
    {
        // Route to TX (use a state that goes to "OH-FC" in the stub engine,
        // but we need TX-FC for 3PL. We'll manually create the shipment.)
        var orderId = Guid.NewGuid();
        var shipmentId = Shipment.StreamId(orderId);
        var now = DateTimeOffset.UtcNow;

        await using (var session = _fixture.GetDocumentSession())
        {
            var fulfillmentRequested = new FulfillmentRequested(
                orderId, Guid.NewGuid(),
                new ShippingAddress("100 3PL Way", null, "Austin", "TX", "78701", "USA"),
                new List<FulfillmentLineItem> { new("SKU-3PL-001", 1) },
                "Ground", now);
            var fcAssigned = new FulfillmentCenterAssigned("TX-FC", now);
            session.Events.StartStream<Shipment>(shipmentId, fulfillmentRequested, fcAssigned);

            var workOrderId = WorkOrder.StreamId(shipmentId, "TX-FC");
            var woCreated = new WorkOrderCreated(
                workOrderId, shipmentId, "TX-FC",
                new List<WorkOrderLineItem> { new("SKU-3PL-001", 1) }, now);
            session.Events.StartStream<WorkOrder>(workOrderId, woCreated);

            await session.SaveChangesAsync();
        }

        await _fixture.ExecuteAndWaitAsync(
            new HandoffToThirdPartyLogistics(shipmentId, "3PL-Partner-TX", "EXT-ORD-001"));

        await using var session2 = _fixture.GetDocumentSession();
        var shipment = await session2.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.HandedToThirdParty);
    }

    [Fact]
    public async Task HandoffTo3PL_NonTXFC_Is_Rejected()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 No 3PL", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-NO3PL-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);

        // NJ-FC — should be rejected (only TX-FC supports 3PL)
        await _fixture.ExecuteAndWaitAsync(
            new HandoffToThirdPartyLogistics(shipment.Id, "3PL-Wrong", "EXT-ORD-002"));

        await using var session2 = _fixture.GetDocumentSession();
        var unchanged = await session2.LoadAsync<Shipment>(shipment.Id);
        unchanged!.Status.ShouldBe(ShipmentStatus.Assigned);
    }
}
