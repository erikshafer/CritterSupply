using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.WorkOrders;

/// <summary>
/// Integration tests for P1 pick failure modes (Slices 16-19).
/// Covers short pick, alternative bin resolution, reroute, and backorder.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class PickFailureTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public PickFailureTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid shipmentId, Guid workOrderId, string fc)> CreatePickingWorkOrderAsync(
        List<IntegrationContracts.FulfillmentLineItem>? lineItems = null)
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Pick Failure Blvd",
                City = "Newark",
                StateProvince = "NJ",
                PostalCode = "07102",
                Country = "USA"
            },
            lineItems ?? new List<IntegrationContracts.FulfillmentLineItem>
            {
                new("DOG-FOOD-40LB", 1),
                new("CAT-TOY-MOUSE", 2)
            },
            "Ground",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Get to PickStarted state
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-FAIL"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Fail"));
        await _fixture.ExecuteAndWaitAsync(new StartPicking(workOrderId));

        return (shipment.Id, workOrderId, fc);
    }

    // --- Slice 16: Short Pick ---

    /// <summary>Slice 16: Short pick transitions to ShortPickPending.</summary>
    [Fact]
    public async Task ReportShortPick_Transitions_To_ShortPickPending()
    {
        var (_, workOrderId, _) = await CreatePickingWorkOrderAsync();

        await _fixture.ExecuteAndWaitAsync(
            new ReportShortPick(workOrderId, "DOG-FOOD-40LB", "A-12-03", 1));

        await using var session = _fixture.GetDocumentSession();
        var wo = await session.LoadAsync<WorkOrder>(workOrderId);
        wo.ShouldNotBeNull();
        wo.Status.ShouldBe(WorkOrderStatus.ShortPickPending);
    }

    /// <summary>Slice 16: Short pick on wrong status is rejected.</summary>
    [Fact]
    public async Task ReportShortPick_Wrong_Status_Is_Rejected()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 Reject St", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-A", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var workOrderId = WorkOrder.StreamId(shipment.Id, shipment.AssignedFulfillmentCenter!);

        // Work order is in Created status — should be rejected
        await _fixture.ExecuteAndWaitAsync(new ReportShortPick(workOrderId, "SKU-A", "A-01", 1));

        await using var session2 = _fixture.GetDocumentSession();
        var wo = await session2.LoadAsync<WorkOrder>(workOrderId);
        wo!.Status.ShouldBe(WorkOrderStatus.Created);
    }

    /// <summary>Slice 16: Idempotency — duplicate short pick doesn't change state.</summary>
    [Fact]
    public async Task ReportShortPick_Duplicate_Still_ShortPickPending()
    {
        var (_, workOrderId, _) = await CreatePickingWorkOrderAsync();

        await _fixture.ExecuteAndWaitAsync(
            new ReportShortPick(workOrderId, "DOG-FOOD-40LB", "A-12-03", 1));
        // Second report — status guard will reject since already ShortPickPending
        await _fixture.ExecuteAndWaitAsync(
            new ReportShortPick(workOrderId, "DOG-FOOD-40LB", "A-12-03", 1));

        await using var session = _fixture.GetDocumentSession();
        var wo = await session.LoadAsync<WorkOrder>(workOrderId);
        wo!.Status.ShouldBe(WorkOrderStatus.ShortPickPending);
    }

    // --- Slice 17: Resume Pick ---

    /// <summary>Slice 17: Resume pick from alternative bin resolves short pick.</summary>
    [Fact]
    public async Task ResumePick_Resolves_ShortPick()
    {
        var items = new List<IntegrationContracts.FulfillmentLineItem>
        {
            new("DOG-FOOD-40LB", 1)
        };
        var (_, workOrderId, _) = await CreatePickingWorkOrderAsync(lineItems: items);

        await _fixture.ExecuteAndWaitAsync(
            new ReportShortPick(workOrderId, "DOG-FOOD-40LB", "A-12-03", 1));

        await _fixture.ExecuteAndWaitAsync(
            new ResumePick(workOrderId, "DOG-FOOD-40LB", 1, "B-05-01"));

        await using var session = _fixture.GetDocumentSession();
        var wo = await session.LoadAsync<WorkOrder>(workOrderId);
        wo.ShouldNotBeNull();
        // All items picked, should auto-complete
        wo.Status.ShouldBe(WorkOrderStatus.PickCompleted);
    }

    /// <summary>Slice 17: Compensation — resume pick on wrong status is rejected.</summary>
    [Fact]
    public async Task ResumePick_Wrong_Status_Is_Rejected()
    {
        var (_, workOrderId, _) = await CreatePickingWorkOrderAsync();

        // Not in ShortPickPending — should be rejected
        await _fixture.ExecuteAndWaitAsync(
            new ResumePick(workOrderId, "DOG-FOOD-40LB", 1, "B-05-01"));

        await using var session = _fixture.GetDocumentSession();
        var wo = await session.LoadAsync<WorkOrder>(workOrderId);
        wo!.Status.ShouldBe(WorkOrderStatus.PickStarted);
    }

    // --- Slice 18: Reroute ---

    /// <summary>Slice 18: Reroute closes original WO, reroutes shipment, creates new WO.</summary>
    [Fact]
    public async Task RerouteShipment_Creates_New_WorkOrder_At_New_FC()
    {
        var (shipmentId, workOrderId, _) = await CreatePickingWorkOrderAsync();

        await _fixture.ExecuteAndWaitAsync(
            new ReportShortPick(workOrderId, "DOG-FOOD-40LB", "A-12-03", 1));

        await _fixture.ExecuteAndWaitAsync(
            new RerouteShipment(workOrderId, "OH-FC"));

        await using var session = _fixture.GetDocumentSession();

        // Original WO should be closed
        var originalWo = await session.LoadAsync<WorkOrder>(workOrderId);
        originalWo!.Status.ShouldBe(WorkOrderStatus.PickExceptionClosed);

        // Shipment should be rerouted back to Assigned
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.AssignedFulfillmentCenter.ShouldBe("OH-FC");

        // New WO should exist at new FC
        var newWorkOrderId = WorkOrder.StreamId(shipmentId, "OH-FC");
        var newWo = await session.LoadAsync<WorkOrder>(newWorkOrderId);
        newWo.ShouldNotBeNull();
        newWo.FulfillmentCenterId.ShouldBe("OH-FC");
        newWo.Status.ShouldBe(WorkOrderStatus.Created);
    }

    // --- Slice 19: Backorder ---

    /// <summary>Slice 19: Backorder closes WO and transitions shipment to Backordered.</summary>
    [Fact]
    public async Task CreateBackorder_Transitions_Shipment_To_Backordered()
    {
        var (shipmentId, workOrderId, _) = await CreatePickingWorkOrderAsync();

        await _fixture.ExecuteAndWaitAsync(
            new ReportShortPick(workOrderId, "DOG-FOOD-40LB", "A-12-03", 1));

        var tracked = await _fixture.ExecuteAndWaitAsync(
            new CreateBackorder(workOrderId, "No stock at any FC"));

        await using var session = _fixture.GetDocumentSession();

        // WO should be closed
        var wo = await session.LoadAsync<WorkOrder>(workOrderId);
        wo!.Status.ShouldBe(WorkOrderStatus.PickExceptionClosed);

        // Shipment should be backordered
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.Backordered);

        // BackorderCreated integration event published
        var backorderMsgs = tracked.Sent.MessagesOf<IntegrationContracts.BackorderCreated>().ToList();
        backorderMsgs.ShouldNotBeEmpty("BackorderCreated should be published");
    }

    /// <summary>Slice 19: Idempotency — backorder on wrong status is rejected.</summary>
    [Fact]
    public async Task CreateBackorder_Wrong_Status_Is_Rejected()
    {
        var (_, workOrderId, _) = await CreatePickingWorkOrderAsync();

        // Not in ShortPickPending — should be rejected
        await _fixture.ExecuteAndWaitAsync(
            new CreateBackorder(workOrderId, "No stock"));

        await using var session = _fixture.GetDocumentSession();
        var wo = await session.LoadAsync<WorkOrder>(workOrderId);
        wo!.Status.ShouldBe(WorkOrderStatus.PickStarted);
    }
}
