using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for FulfillmentRequestedHandler (Slices 1-3).
/// Verifies UUID v5 deterministic stream key, routing engine integration,
/// and WorkOrder creation.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class FulfillmentRequestedHandlerTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public FulfillmentRequestedHandlerTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _fixture.FrozenClock.SetUtcNow(DateTimeOffset.UtcNow);
        return _fixture.CleanAllDocumentsAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private static IntegrationContracts.FulfillmentRequested BuildFulfillmentRequested(
        Guid orderId,
        Guid? customerId = null,
        string stateProvince = "CO") =>
        new(
            orderId,
            customerId ?? Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "500 Idempotent Blvd",
                City = "Denver",
                StateProvince = stateProvince,
                PostalCode = "80202",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-UUID5-001", 3) },
            "Standard",
            DateTimeOffset.UtcNow);

    /// <summary>
    /// Slice 1+2+3: FulfillmentRequested creates Shipment, assigns FC, creates WorkOrder.
    /// </summary>
    [Fact]
    public async Task FulfillmentRequested_Creates_Shipment_And_WorkOrder()
    {
        var orderId = Guid.NewGuid();
        var message = BuildFulfillmentRequested(orderId);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>()
            .FirstOrDefaultAsync(s => s.OrderId == orderId);

        shipment.ShouldNotBeNull();
        shipment.Status.ShouldBe(ShipmentStatus.Assigned);
        shipment.AssignedFulfillmentCenter.ShouldNotBeNullOrEmpty();

        // WorkOrder created at assigned FC
        var workOrderId = WorkOrder.StreamId(shipment.Id, shipment.AssignedFulfillmentCenter!);
        var workOrder = await session.LoadAsync<WorkOrder>(workOrderId);
        workOrder.ShouldNotBeNull();
        workOrder.ShipmentId.ShouldBe(shipment.Id);
        workOrder.Status.ShouldBe(WorkOrderStatus.Created);
    }

    /// <summary>Slice 2: East coast routes to NJ-FC.</summary>
    [Fact]
    public async Task FulfillmentRequested_EastCoast_Routes_To_NJ_FC()
    {
        var orderId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(BuildFulfillmentRequested(orderId, stateProvince: "NJ"));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        shipment.AssignedFulfillmentCenter.ShouldBe("NJ-FC");
    }

    /// <summary>Slice 2: West coast routes to WA-FC.</summary>
    [Fact]
    public async Task FulfillmentRequested_WestCoast_Routes_To_WA_FC()
    {
        var orderId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(BuildFulfillmentRequested(orderId, stateProvince: "CA"));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        shipment.AssignedFulfillmentCenter.ShouldBe("WA-FC");
    }

    /// <summary>Slice 2: Non-coast routes to OH-FC.</summary>
    [Fact]
    public async Task FulfillmentRequested_Midwest_Routes_To_OH_FC()
    {
        var orderId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(BuildFulfillmentRequested(orderId, stateProvince: "OH"));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        shipment.AssignedFulfillmentCenter.ShouldBe("OH-FC");
    }

    /// <summary>Idempotency: duplicate FulfillmentRequested is silently ignored.</summary>
    [Fact]
    public async Task FulfillmentRequested_Same_OrderId_Creates_Same_ShipmentId()
    {
        var orderId = Guid.NewGuid();
        var message = BuildFulfillmentRequested(orderId);

        await _fixture.ExecuteAndWaitAsync(message);
        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipments = await session.Query<Shipment>()
            .Where(s => s.OrderId == orderId)
            .ToListAsync();

        shipments.Count.ShouldBe(1);
    }

    /// <summary>Different OrderIds produce different ShipmentIds.</summary>
    [Fact]
    public async Task FulfillmentRequested_Different_OrderIds_Create_Different_ShipmentIds()
    {
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await _fixture.ExecuteAndWaitAsync(BuildFulfillmentRequested(orderId1, customerId));
        await _fixture.ExecuteAndWaitAsync(BuildFulfillmentRequested(orderId2, customerId));

        await using var session = _fixture.GetDocumentSession();
        var shipment1 = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId1);
        var shipment2 = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId2);

        shipment1.Id.ShouldNotBe(shipment2.Id);
    }

    /// <summary>ShipmentStatusView is created inline.</summary>
    [Fact]
    public async Task FulfillmentRequested_Creates_ShipmentStatusView()
    {
        var orderId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(BuildFulfillmentRequested(orderId));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var statusView = await session.LoadAsync<ShipmentStatusView>(shipment.Id);

        statusView.ShouldNotBeNull();
        statusView.OrderId.ShouldBe(orderId);
        statusView.Status.ShouldBe("Assigned");
        statusView.StatusHistory.Count.ShouldBeGreaterThanOrEqualTo(2);
    }
}
