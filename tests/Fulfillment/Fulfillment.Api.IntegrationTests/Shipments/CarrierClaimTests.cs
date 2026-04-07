using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for Slices 33-34 (Carrier Claim Filing + Resolution).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CarrierClaimTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CarrierClaimTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _fixture.FrozenClock.SetUtcNow(DateTimeOffset.UtcNow);
        return _fixture.CleanAllDocumentsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> CreateLostInTransitShipmentAsync()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Claim St", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-CLAIM-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-CLM"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Claim"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-CLAIM-001", 1, "A-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-CLAIM-001", 1));
        await _fixture.ExecuteAndWaitAsync(new ManifestShipment(shipment.Id, "M-CLM"));
        await _fixture.ExecuteAndWaitAsync(new StagePackage(shipment.Id, "Lane-CLM", "1:00 PM"));
        await _fixture.ExecuteAndWaitAsync(new ConfirmCarrierPickup(shipment.Id, "UPS", true));

        _fixture.FrozenClock.Advance(TimeSpan.FromDays(8));
        await _fixture.ExecuteAndWaitAsync(new CheckForLostShipment(shipment.Id));

        return shipment.Id;
    }

    // --- Slice 33: Carrier Claim Filing ---

    [Fact]
    public async Task FileCarrierClaim_LostInTransit_Appends_Event()
    {
        var shipmentId = await CreateLostInTransitShipmentAsync();

        await _fixture.ExecuteAndWaitAsync(
            new FileCarrierClaim(shipmentId, "UPS", "LostPackage"));

        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(shipmentId);
        events.Select(e => e.Data).OfType<CarrierClaimFiled>().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task FileCarrierClaim_Wrong_Status_Is_Rejected()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 No Claim", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-NOCLAIM-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);

        // Assigned status — should be rejected
        await _fixture.ExecuteAndWaitAsync(
            new FileCarrierClaim(shipment.Id, "UPS", "LostPackage"));

        await using var session2 = _fixture.GetDocumentSession();
        var events = await session2.Events.FetchStreamAsync(shipment.Id);
        events.Select(e => e.Data).OfType<CarrierClaimFiled>().ShouldBeEmpty();
    }

    // --- Slice 34: Carrier Claim Resolution ---

    [Fact]
    public async Task ResolveCarrierClaim_After_Filing_Appends_Resolved()
    {
        var shipmentId = await CreateLostInTransitShipmentAsync();

        await _fixture.ExecuteAndWaitAsync(
            new FileCarrierClaim(shipmentId, "UPS", "LostPackage"));
        await _fixture.ExecuteAndWaitAsync(
            new ResolveCarrierClaim(shipmentId, "Paid", 150.00m));

        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(shipmentId);
        var resolved = events.Select(e => e.Data).OfType<CarrierClaimResolved>().FirstOrDefault();
        resolved.ShouldNotBeNull();
        resolved.Resolution.ShouldBe("Paid");
        resolved.AmountUSD.ShouldBe(150.00m);
    }

    [Fact]
    public async Task ResolveCarrierClaim_Without_Filing_Is_Rejected()
    {
        var shipmentId = await CreateLostInTransitShipmentAsync();

        // No claim filed — should be rejected
        await _fixture.ExecuteAndWaitAsync(
            new ResolveCarrierClaim(shipmentId, "Paid", 150.00m));

        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(shipmentId);
        events.Select(e => e.Data).OfType<CarrierClaimResolved>().ShouldBeEmpty();
    }
}
