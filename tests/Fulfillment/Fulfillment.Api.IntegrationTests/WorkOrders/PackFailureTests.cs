using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.WorkOrders;

/// <summary>
/// Integration tests for P1 pack failure modes (Slices 20-22).
/// Covers pack discrepancy (wrong item/weight mismatch) and label generation failure.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class PackFailureTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public PackFailureTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid shipmentId, Guid workOrderId)> CreatePackingWorkOrderAsync()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Pack Failure Blvd",
                City = "Newark",
                StateProvince = "NJ",
                PostalCode = "07102",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem>
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

        // Complete picking
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-PACK"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Pack"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "DOG-FOOD-40LB", 1, "A-01"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "CAT-TOY-MOUSE", 2, "B-01"));

        // Start packing
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));

        return (shipment.Id, workOrderId);
    }

    // --- Slice 20: Wrong Item Scanned ---

    /// <summary>Slice 20: Wrong item scanned transitions to PackDiscrepancyPending.</summary>
    [Fact]
    public async Task ReportPackDiscrepancy_WrongItem_Transitions_To_PackDiscrepancyPending()
    {
        var (_, workOrderId) = await CreatePackingWorkOrderAsync();

        await _fixture.ExecuteAndWaitAsync(
            new ReportPackDiscrepancy(
                workOrderId,
                "WRONG-SKU-999",
                "DOG-FOOD-40LB",
                DiscrepancyType.WrongItem,
                "Scanned wrong item at pack station"));

        await using var session = _fixture.GetDocumentSession();
        var wo = await session.LoadAsync<WorkOrder>(workOrderId);
        wo.ShouldNotBeNull();
        wo.Status.ShouldBe(WorkOrderStatus.PackDiscrepancyPending);
    }

    // --- Slice 21: Weight Mismatch ---

    /// <summary>Slice 21: Weight mismatch transitions to PackDiscrepancyPending.</summary>
    [Fact]
    public async Task ReportPackDiscrepancy_WeightMismatch_Transitions_To_PackDiscrepancyPending()
    {
        var (_, workOrderId) = await CreatePackingWorkOrderAsync();

        await _fixture.ExecuteAndWaitAsync(
            new ReportPackDiscrepancy(
                workOrderId,
                "DOG-FOOD-40LB",
                null,
                DiscrepancyType.WeightMismatch,
                "Expected 40lb, measured 35lb"));

        await using var session = _fixture.GetDocumentSession();
        var wo = await session.LoadAsync<WorkOrder>(workOrderId);
        wo!.Status.ShouldBe(WorkOrderStatus.PackDiscrepancyPending);
    }

    /// <summary>Slices 20-21: Idempotency — discrepancy on wrong status is rejected.</summary>
    [Fact]
    public async Task ReportPackDiscrepancy_Wrong_Status_Is_Rejected()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 Test", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-A", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var workOrderId = WorkOrder.StreamId(shipment.Id, shipment.AssignedFulfillmentCenter!);

        // WO is in Created status — should be rejected
        await _fixture.ExecuteAndWaitAsync(
            new ReportPackDiscrepancy(workOrderId, "SKU-A", null,
                DiscrepancyType.WrongItem, "wrong item"));

        await using var session2 = _fixture.GetDocumentSession();
        var wo = await session2.LoadAsync<WorkOrder>(workOrderId);
        wo!.Status.ShouldBe(WorkOrderStatus.Created);
    }

    // --- Slice 22: Label Generation Failure ---
    // (Tested via unit test since the stub label generation never actually fails.
    //  The Apply(ShippingLabelGenerationFailed) is tested in unit tests.)
}
