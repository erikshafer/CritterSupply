using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for time-based slices using FrozenSystemClock.
/// Covers Slice 26 (lost-in-transit with 7-day threshold) and Slice 29 (SLA escalation).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class TimeBasedMonitoringTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public TimeBasedMonitoringTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        // Reset clock to now for each test
        _fixture.FrozenClock.SetUtcNow(DateTimeOffset.UtcNow);
        return _fixture.CleanAllDocumentsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid shipmentId, Guid orderId, string trackingNumber)> CreateHandedToCarrierAsync()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Time Test Way",
                City = "Newark",
                StateProvince = "NJ",
                PostalCode = "07102",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-TIME-001", 1) },
            "Ground",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Complete work order → cascade labels
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-TIME"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Time"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-TIME-001", 1, "A-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-TIME-001", 1));

        await using var session2 = _fixture.GetDocumentSession();
        var labeled = await session2.LoadAsync<Shipment>(shipment.Id);
        var trackingNumber = labeled!.TrackingNumber!;

        // Manifest, stage, hand to carrier
        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipment.Id, "M-TIME"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipment.Id, "Lane-TIME", "1:00-2:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipment.Id, "UPS", true));

        return (shipment.Id, orderId, trackingNumber);
    }

    // --- Slice 26: Lost-in-Transit with Time Advancement ---

    /// <summary>
    /// Slice 26: Advance clock 8 days after handoff. CheckForLostShipment should detect
    /// the shipment as lost and append ShipmentLostInTransit.
    /// </summary>
    [Fact]
    public async Task CheckForLostShipment_After_8_Days_Detects_Lost()
    {
        var (shipmentId, orderId, _) = await CreateHandedToCarrierAsync();

        // Advance clock 8 days past handoff
        _fixture.FrozenClock.Advance(TimeSpan.FromDays(8));

        var tracked = await _fixture.ExecuteAndWaitAsync(new CheckForLostShipment(shipmentId));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.LostInTransit);

        // Verify integration event published
        var lostMsgs = tracked.Sent.MessagesOf<IntegrationContracts.ShipmentLostInTransit>().ToList();
        lostMsgs.ShouldNotBeEmpty("ShipmentLostInTransit integration event should be published");
        lostMsgs.First().OrderId.ShouldBe(orderId);
    }

    /// <summary>
    /// Slice 26: Clock at 5 days — within threshold. CheckForLostShipment should NOT
    /// mark as lost.
    /// </summary>
    [Fact]
    public async Task CheckForLostShipment_At_5_Days_Does_Not_Detect_Lost()
    {
        var (shipmentId, _, _) = await CreateHandedToCarrierAsync();

        // Advance clock only 5 days — below the 7-day threshold
        _fixture.FrozenClock.Advance(TimeSpan.FromDays(5));

        await _fixture.ExecuteAndWaitAsync(new CheckForLostShipment(shipmentId));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.HandedToCarrier);
    }

    // --- Slice 29: SLA Escalation with Time Advancement ---

    /// <summary>
    /// Slice 29: Advance clock past 50% of 4-hour SLA window. CheckSLAThresholds should
    /// append SLAEscalationRaised with Threshold 50.
    /// </summary>
    [Fact]
    public async Task CheckWorkOrderSLA_Past_50_Percent_Raises_Escalation()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 SLA Time St", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-SLA-TIME-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var workOrderId = WorkOrder.StreamId(shipment.Id, shipment.AssignedFulfillmentCenter!);

        // Advance clock past 50% of 4-hour SLA window (2.5 hours)
        _fixture.FrozenClock.Advance(TimeSpan.FromHours(2.5));

        await _fixture.ExecuteAndWaitAsync(new CheckWorkOrderSLA(workOrderId));

        await using var session2 = _fixture.GetDocumentSession();
        var wo = await session2.LoadAsync<WorkOrder>(workOrderId);
        wo!.EscalationThresholdsMet.ShouldContain(50);
        wo.EscalationThresholdsMet.ShouldNotContain(75);
        wo.EscalationThresholdsMet.ShouldNotContain(100);
    }

    /// <summary>
    /// Slice 29: Advance clock past 100% of SLA window — full breach.
    /// </summary>
    [Fact]
    public async Task CheckWorkOrderSLA_Past_100_Percent_Breaches_SLA()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 SLA Time St", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-SLA-TIME-002", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var workOrderId = WorkOrder.StreamId(shipment.Id, shipment.AssignedFulfillmentCenter!);

        // Advance clock past 100% of 4-hour SLA window (5 hours)
        _fixture.FrozenClock.Advance(TimeSpan.FromHours(5));

        await _fixture.ExecuteAndWaitAsync(new CheckWorkOrderSLA(workOrderId));

        await using var session2 = _fixture.GetDocumentSession();
        var wo = await session2.LoadAsync<WorkOrder>(workOrderId);
        wo!.EscalationThresholdsMet.ShouldContain(50);
        wo.EscalationThresholdsMet.ShouldContain(75);

        // SLABreached is a separate event type — not tracked in EscalationThresholdsMet.
        // Verify it was appended to the event stream.
        var events = await session2.Events.FetchStreamAsync(workOrderId);
        events.Select(e => e.Data).OfType<SLABreached>().ShouldNotBeEmpty("SLABreached event should be appended at 100%");
    }
}
