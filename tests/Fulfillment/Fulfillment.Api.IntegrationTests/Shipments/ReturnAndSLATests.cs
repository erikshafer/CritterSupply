using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for P1 return received + SLA escalation (Slices 28-29).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ReturnAndSLATests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ReturnAndSLATests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        // Reset frozen clock to real time for non-time-dependent tests
        _fixture.FrozenClock.SetUtcNow(DateTimeOffset.UtcNow);
        return _fixture.CleanAllDocumentsAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    // --- Slice 28: Receive Return at Warehouse ---

    /// <summary>Slice 28: Receive return transitions to ReturnReceived.</summary>
    [Fact]
    public async Task ReceiveReturnAtWarehouse_Transitions_To_ReturnReceived()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Return Way",
                City = "Newark",
                StateProvince = "NJ",
                PostalCode = "07102",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-RET-001", 1) },
            "Ground",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session1 = _fixture.GetDocumentSession();
        var shipment = await session1.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Complete work order → cascade labels
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-RET"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Ret"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-RET-001", 1, "A-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-RET-001", 1));

        await using var session2 = _fixture.GetDocumentSession();
        var labeled = await session2.LoadAsync<Shipment>(shipment.Id);
        var trackingNumber = labeled!.TrackingNumber!;

        // Manifest, stage, hand to carrier
        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipment.Id, "M-RET"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipment.Id, "Lane-RET", "1:00-2:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipment.Id, "UPS", true));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "IN_TRANSIT", DateTimeOffset.UtcNow, "Hub", null, null));

        // Three delivery attempts → RTS
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERY_ATTEMPTED", DateTimeOffset.UtcNow, "Door", "NI", 1));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERY_ATTEMPTED", DateTimeOffset.UtcNow, "Door", "NI", 2));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "DELIVERY_ATTEMPTED", DateTimeOffset.UtcNow, "Door", "NI", 3));

        // Now in ReturningToSender — receive the return
        await _fixture.ExecuteAndWaitAsync(
            new ReceiveReturnAtWarehouse(shipment.Id, "NJ-FC"));

        await using var session3 = _fixture.GetDocumentSession();
        var finalShipment = await session3.LoadAsync<Shipment>(shipment.Id);
        finalShipment!.Status.ShouldBe(ShipmentStatus.ReturnReceived);
    }

    /// <summary>Slice 28: Receive return on wrong status is rejected.</summary>
    [Fact]
    public async Task ReceiveReturnAtWarehouse_Wrong_Status_Is_Rejected()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 Reject", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-RET-002", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);

        // Shipment is in Assigned status — should be rejected
        await _fixture.ExecuteAndWaitAsync(
            new ReceiveReturnAtWarehouse(shipment.Id, "NJ-FC"));

        await using var session2 = _fixture.GetDocumentSession();
        var finalShipment = await session2.LoadAsync<Shipment>(shipment.Id);
        finalShipment!.Status.ShouldBe(ShipmentStatus.Assigned);
    }

    // --- Slice 29: SLA Monitoring ---

    /// <summary>Slice 29: SLA check on a fresh work order is a no-op (not enough time).</summary>
    [Fact]
    public async Task CheckWorkOrderSLA_Fresh_WorkOrder_No_Escalation()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 SLA St", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-SLA-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var workOrderId = WorkOrder.StreamId(shipment.Id, shipment.AssignedFulfillmentCenter!);

        // SLA check on a fresh work order — no escalation expected
        await _fixture.ExecuteAndWaitAsync(new CheckWorkOrderSLA(workOrderId));

        await using var session2 = _fixture.GetDocumentSession();
        var wo = await session2.LoadAsync<WorkOrder>(workOrderId);
        wo!.EscalationThresholdsMet.ShouldBeEmpty();
    }

    /// <summary>Slice 29: SLA check on completed work order is a no-op.</summary>
    [Fact]
    public async Task CheckWorkOrderSLA_Completed_WorkOrder_Is_NoOp()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 SLA St", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-SLA-002", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Complete the work order
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-SLA"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-SLA"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-SLA-002", 1, "A-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-SLA-002", 1));

        // SLA check on completed WO — no escalation
        await _fixture.ExecuteAndWaitAsync(new CheckWorkOrderSLA(workOrderId));

        await using var session2 = _fixture.GetDocumentSession();
        var wo = await session2.LoadAsync<WorkOrder>(workOrderId);
        wo!.EscalationThresholdsMet.ShouldBeEmpty();
    }
}
