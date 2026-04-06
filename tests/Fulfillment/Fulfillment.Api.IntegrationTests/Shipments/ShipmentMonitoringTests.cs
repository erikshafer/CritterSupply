using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for P1 shipment monitoring (Slices 25-27).
/// Covers ghost shipment detection, lost in transit, and return to sender via webhook.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ShipmentMonitoringTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ShipmentMonitoringTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid shipmentId, Guid orderId, string trackingNumber)> CreateHandedToCarrierAsync()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Monitor Way",
                City = "Newark",
                StateProvince = "NJ",
                PostalCode = "07102",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-MON-001", 1) },
            "Ground",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Complete work order → cascade labels
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-MON"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Mon"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-MON-001", 1, "A-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-MON-001", 1));

        await using var session2 = _fixture.GetDocumentSession();
        var labeled = await session2.LoadAsync<Shipment>(shipment.Id);
        var trackingNumber = labeled!.TrackingNumber!;

        // Manifest, stage, hand to carrier
        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipment.Id, "M-MON"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipment.Id, "Lane-MON", "1:00-2:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipment.Id, "UPS", true));

        return (shipment.Id, orderId, trackingNumber);
    }

    // --- Slice 25: Ghost Shipment Detection ---

    /// <summary>Slice 25: Ghost shipment detected when no scan after handoff.</summary>
    [Fact]
    public async Task CheckForGhostShipment_Detects_Ghost()
    {
        var (shipmentId, _, _) = await CreateHandedToCarrierAsync();

        await _fixture.ExecuteAndWaitAsync(new CheckForGhostShipment(shipmentId));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.GhostShipmentInvestigation);
    }

    /// <summary>Slice 25: Ghost shipment resolved when InTransit scan arrives.</summary>
    [Fact]
    public async Task GhostShipment_Resolves_When_InTransit_Scan_Arrives()
    {
        var (shipmentId, _, trackingNumber) = await CreateHandedToCarrierAsync();

        // Detect ghost
        await _fixture.ExecuteAndWaitAsync(new CheckForGhostShipment(shipmentId));

        // InTransit scan resolves it
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "IN_TRANSIT", DateTimeOffset.UtcNow, "Edison, NJ", null, null));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.InTransit);
    }

    /// <summary>Slice 25: Idempotency — ghost check when already InTransit is a no-op.</summary>
    [Fact]
    public async Task CheckForGhostShipment_InTransit_Is_NoOp()
    {
        var (shipmentId, _, trackingNumber) = await CreateHandedToCarrierAsync();

        // First scan arrives
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "IN_TRANSIT", DateTimeOffset.UtcNow, "Hub", null, null));

        // Ghost check after InTransit — should be no-op
        await _fixture.ExecuteAndWaitAsync(new CheckForGhostShipment(shipmentId));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.InTransit);
    }

    // --- Slice 27: Return to Sender via Webhook ---

    /// <summary>Slice 27: RETURN_TO_SENDER webhook triggers ReturnToSenderInitiated.</summary>
    [Fact]
    public async Task CarrierWebhook_ReturnToSender_Triggers_RTS()
    {
        var (shipmentId, orderId, trackingNumber) = await CreateHandedToCarrierAsync();

        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "IN_TRANSIT", DateTimeOffset.UtcNow, "Hub", null, null));

        var tracked = await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "RETURN_TO_SENDER", DateTimeOffset.UtcNow, null, null, 7));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.ReturningToSender);

        // Verify integration event published
        var rtsMsgs = tracked.Sent.MessagesOf<IntegrationContracts.ReturnToSenderInitiated>().ToList();
        rtsMsgs.ShouldNotBeEmpty("ReturnToSenderInitiated should be published");
    }

    /// <summary>Slice 27: Idempotency — duplicate RTS webhook is ignored.</summary>
    [Fact]
    public async Task CarrierWebhook_Duplicate_ReturnToSender_Is_Idempotent()
    {
        var (shipmentId, _, trackingNumber) = await CreateHandedToCarrierAsync();

        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "IN_TRANSIT", DateTimeOffset.UtcNow, "Hub", null, null));

        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "RETURN_TO_SENDER", DateTimeOffset.UtcNow, null, null, 7));
        await _fixture.ExecuteAndWaitAsync(new CarrierWebhookPayload(
            trackingNumber, "RETURN_TO_SENDER", DateTimeOffset.UtcNow, null, null, 7));

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.LoadAsync<Shipment>(shipmentId);
        shipment!.Status.ShouldBe(ShipmentStatus.ReturningToSender);
    }
}
