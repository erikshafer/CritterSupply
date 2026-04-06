using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for the HTTP carrier webhook endpoint (Part 2A)
/// and the PackingCompleted → GenerateShippingLabel cascading policy (Part 2B).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CarrierWebhookEndpointTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CarrierWebhookEndpointTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid shipmentId, Guid orderId, string trackingNumber)> CreateShipmentWithTrackingAsync()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "300 Webhook Way",
                City = "Newark",
                StateProvince = "NJ",
                PostalCode = "07102",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-WEBHOOK-001", 1) },
            "Ground",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Complete work order — PackingCompleted cascades to GenerateShippingLabel
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-WH"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-WH"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-WEBHOOK-001", 1, "A-01-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-WEBHOOK-001", 1));

        await using var session2 = _fixture.GetDocumentSession();
        var labeled = await session2.LoadAsync<Shipment>(shipment.Id);
        var trackingNumber = labeled!.TrackingNumber!;

        // Manifest, stage, and hand to carrier
        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipment.Id, "M-WH"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipment.Id, "Lane-WH", "1:00-2:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipment.Id, "UPS", true));

        return (shipment.Id, orderId, trackingNumber);
    }

    /// <summary>
    /// Part 2A: HTTP POST to carrier-webhook appends a domain event.
    /// </summary>
    [Fact]
    public async Task CarrierWebhookEndpoint_Post_Appends_InTransit_Event()
    {
        var (shipmentId, _, trackingNumber) = await CreateShipmentWithTrackingAsync();

        var payload = new CarrierWebhookPayload(
            trackingNumber, "IN_TRANSIT", DateTimeOffset.UtcNow, "Edison, NJ", null, null);

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(payload).ToUrl("/api/fulfillment/carrier-webhook");
            x.StatusCodeShouldBe(202);
        });

        // Allow processing time
        await Task.Delay(500);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment.ShouldNotBeNull();
        shipment.Status.ShouldBe(ShipmentStatus.InTransit);
        shipment.LastScanLocation.ShouldBe("Edison, NJ");
    }

    /// <summary>
    /// Part 2B: PackingCompleted triggers GenerateShippingLabel cascading policy.
    /// After VerifyItemAtPack completes packing, the shipment should automatically
    /// get a label generated (Assigned → Labeled transition).
    /// </summary>
    [Fact]
    public async Task PackingCompleted_Cascading_Policy_Triggers_Label_Generation()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "400 Policy Blvd",
                City = "Newark",
                StateProvince = "NJ",
                PostalCode = "07102",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-POLICY-001", 1) },
            "Ground",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session1 = _fixture.GetDocumentSession();
        var shipment = await session1.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Full pick flow
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-POL"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-POL"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-POLICY-001", 1, "A-01-01"));

        // VerifyItemAtPack will trigger PackingCompleted, which should cascade to GenerateShippingLabel
        var tracked = await _fixture.ExecuteAndWaitAsync(
            new VerifyItemAtPack(workOrderId, "SKU-POLICY-001", 1));

        // Verify the work order is PackingCompleted
        await using var session2 = _fixture.GetDocumentSession();
        var wo = await session2.LoadAsync<WorkOrder>(workOrderId);
        wo!.Status.ShouldBe(WorkOrderStatus.PackingCompleted);

        // Verify the shipment got labeled automatically via cascading policy
        var updatedShipment = await session2.LoadAsync<Shipment>(shipment.Id);
        updatedShipment!.Status.ShouldBe(ShipmentStatus.Labeled,
            "PackingCompleted should cascade to GenerateShippingLabel, transitioning shipment to Labeled");
        updatedShipment.TrackingNumber.ShouldNotBeNullOrEmpty(
            "Tracking number should be assigned via cascading policy");
        updatedShipment.Carrier.ShouldNotBeNullOrEmpty();
    }
}
