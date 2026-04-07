using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for Slice 35 (Fulfillment Cancellation).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CancellationTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CancellationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _fixture.FrozenClock.SetUtcNow(DateTimeOffset.UtcNow);
        return _fixture.CleanAllDocumentsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CancelFulfillment_Assigned_Status_Cancels_Shipment_And_WorkOrder()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Cancel St", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-CANCEL-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        var tracked = await _fixture.ExecuteAndWaitAsync(
            new CancelFulfillment(shipment.Id, "Customer requested cancellation"));

        // Shipment should be cancelled
        await using var session2 = _fixture.GetDocumentSession();
        var cancelled = await session2.LoadAsync<Shipment>(shipment.Id);
        cancelled!.Status.ShouldBe(ShipmentStatus.FulfillmentCancelled);

        // WorkOrder should be cancelled
        var wo = await session2.LoadAsync<WorkOrder>(workOrderId);
        wo!.Status.ShouldBe(WorkOrderStatus.Cancelled);

        // Integration event should be published
        var cancelMsgs = tracked.Sent.MessagesOf<IntegrationContracts.FulfillmentCancelled>().ToList();
        cancelMsgs.ShouldNotBeEmpty("FulfillmentCancelled should be published");
    }

    [Fact]
    public async Task CancelFulfillment_HandedToCarrier_Is_Rejected()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 No Cancel", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-NOCANCEL-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Progress to HandedToCarrier
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-NC"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-NC"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-NOCANCEL-001", 1, "A-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-NOCANCEL-001", 1));
        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipment.Id, "M-NC"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipment.Id, "Lane-NC", "1:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipment.Id, "UPS", true));

        // Attempt cancel — should be rejected (HandedToCarrier)
        await _fixture.ExecuteAndWaitAsync(
            new CancelFulfillment(shipment.Id, "Too late"));

        await using var session2 = _fixture.GetDocumentSession();
        var unchanged = await session2.LoadAsync<Shipment>(shipment.Id);
        unchanged!.Status.ShouldBe(ShipmentStatus.HandedToCarrier);
    }

    [Fact]
    public async Task CancelFulfillment_Idempotent_After_Cancellation()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "300 Idemp Cancel", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-IDEMP-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);

        await _fixture.ExecuteAndWaitAsync(
            new CancelFulfillment(shipment.Id, "First cancel"));

        // Second cancel — should be idempotent (already in FulfillmentCancelled — terminal)
        await _fixture.ExecuteAndWaitAsync(
            new CancelFulfillment(shipment.Id, "Second cancel"));

        await using var session2 = _fixture.GetDocumentSession();
        var cancelled = await session2.LoadAsync<Shipment>(shipment.Id);
        cancelled!.Status.ShouldBe(ShipmentStatus.FulfillmentCancelled);
    }
}
