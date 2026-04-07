using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.WorkOrders;

/// <summary>
/// Integration tests for WorkOrder lifecycle (Slices 4-9).
/// Full pick/pack happy path from wave release through packing completed.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class WorkOrderLifecycleTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public WorkOrderLifecycleTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _fixture.FrozenClock.SetUtcNow(DateTimeOffset.UtcNow);
        return _fixture.CleanAllDocumentsAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid shipmentId, Guid workOrderId, string fc)> CreateShipmentWithWorkOrderAsync(
        string stateProvince = "NJ",
        List<IntegrationContracts.FulfillmentLineItem>? lineItems = null)
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Warehouse Blvd",
                City = "Newark",
                StateProvince = stateProvince,
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

        return (shipment.Id, workOrderId, fc);
    }

    /// <summary>Slice 4: Wave release transitions WorkOrder to WaveReleased.</summary>
    [Fact]
    public async Task ReleaseWave_Transitions_To_WaveReleased()
    {
        var (_, workOrderId, _) = await CreateShipmentWithWorkOrderAsync();

        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-001"));

        await using var session = _fixture.GetDocumentSession();
        var wo = await session.LoadAsync<WorkOrder>(workOrderId);
        wo.ShouldNotBeNull();
        wo.Status.ShouldBe(WorkOrderStatus.WaveReleased);
        wo.WaveReleasedAt.ShouldNotBeNull();
    }

    /// <summary>Slice 5: Pick list assignment.</summary>
    [Fact]
    public async Task AssignPickList_Assigns_Picker()
    {
        var (_, workOrderId, _) = await CreateShipmentWithWorkOrderAsync();

        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-002"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Martinez"));

        await using var session = _fixture.GetDocumentSession();
        var wo = await session.LoadAsync<WorkOrder>(workOrderId);
        wo.ShouldNotBeNull();
        wo.Status.ShouldBe(WorkOrderStatus.PickListAssigned);
        wo.AssignedPicker.ShouldBe("P-Martinez");
    }

    /// <summary>Slices 6-7: Item picking with auto-completion.</summary>
    [Fact]
    public async Task RecordItemPick_All_Items_Triggers_PickCompleted()
    {
        var items = new List<IntegrationContracts.FulfillmentLineItem>
        {
            new("DOG-FOOD-40LB", 1),
            new("CAT-TOY-MOUSE", 2)
        };
        var (_, workOrderId, _) = await CreateShipmentWithWorkOrderAsync(lineItems: items);

        // Wave release and pick list assignment
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-003"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Garcia"));

        // Pick first item
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "DOG-FOOD-40LB", 1, "A-12-03"));

        await using var session1 = _fixture.GetDocumentSession();
        var woPartial = await session1.LoadAsync<WorkOrder>(workOrderId);
        woPartial!.Status.ShouldBe(WorkOrderStatus.PickStarted, "Should be PickStarted after first pick");

        // Pick second item (all items now picked)
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "CAT-TOY-MOUSE", 2, "B-05-01"));

        await using var session2 = _fixture.GetDocumentSession();
        var woComplete = await session2.LoadAsync<WorkOrder>(workOrderId);
        woComplete!.Status.ShouldBe(WorkOrderStatus.PickCompleted, "Should auto-complete when all items picked");
        woComplete.PickCompletedAt.ShouldNotBeNull();
    }

    /// <summary>Slices 8-9: Packing with auto-completion.</summary>
    [Fact]
    public async Task VerifyItemAtPack_All_Items_Triggers_PackingCompleted()
    {
        var items = new List<IntegrationContracts.FulfillmentLineItem>
        {
            new("DOG-FOOD-40LB", 1)
        };
        var (_, workOrderId, _) = await CreateShipmentWithWorkOrderAsync(lineItems: items);

        // Full pick flow
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-004"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Lee"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "DOG-FOOD-40LB", 1, "C-01-01"));

        // Start packing and verify
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "DOG-FOOD-40LB", 1));

        await using var session = _fixture.GetDocumentSession();
        var wo = await session.LoadAsync<WorkOrder>(workOrderId);
        wo!.Status.ShouldBe(WorkOrderStatus.PackingCompleted);
        wo.PackingCompletedAt.ShouldNotBeNull();
        wo.BillableWeightLbs.ShouldBeGreaterThan(0);
        wo.CartonSize.ShouldNotBeNullOrEmpty();
    }

    /// <summary>Full happy path: Slices 3-9.</summary>
    [Fact]
    public async Task Complete_WorkOrder_Lifecycle()
    {
        var items = new List<IntegrationContracts.FulfillmentLineItem>
        {
            new("SKU-A", 1),
            new("SKU-B", 1)
        };
        var (_, workOrderId, _) = await CreateShipmentWithWorkOrderAsync(lineItems: items);

        // Wave → Pick Assignment → Pick Items → Pack Items
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-FULL"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Full"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-A", 1, "D-01-01"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-B", 1, "D-02-02"));

        // Verify pick completed
        await using var session1 = _fixture.GetDocumentSession();
        var woPicked = await session1.LoadAsync<WorkOrder>(workOrderId);
        woPicked!.Status.ShouldBe(WorkOrderStatus.PickCompleted);

        // Pack
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-A", 1));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-B", 1));

        await using var session2 = _fixture.GetDocumentSession();
        var woFinal = await session2.LoadAsync<WorkOrder>(workOrderId);
        woFinal!.Status.ShouldBe(WorkOrderStatus.PackingCompleted);
    }

    /// <summary>Cannot release wave for a non-existent work order.</summary>
    [Fact]
    public async Task ReleaseWave_NonExistent_WorkOrder_Is_NoOp()
    {
        var fakeId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(fakeId, "WAVE-FAKE"));

        // Handler should return 404 via Before() — no crash
    }

    /// <summary>Cannot pick items from wrong SKU.</summary>
    [Fact]
    public async Task RecordItemPick_Wrong_SKU_Is_Rejected()
    {
        var items = new List<IntegrationContracts.FulfillmentLineItem>
        {
            new("DOG-FOOD-40LB", 1)
        };
        var (_, workOrderId, _) = await CreateShipmentWithWorkOrderAsync(lineItems: items);

        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-X"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-X"));

        // Try to pick a SKU not in the work order
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "WRONG-SKU", 1, "A-01-01"));

        // Should be rejected — status should still be PickListAssigned
        await using var session = _fixture.GetDocumentSession();
        var wo = await session.LoadAsync<WorkOrder>(workOrderId);
        wo!.Status.ShouldBe(WorkOrderStatus.PickListAssigned);
    }
}
