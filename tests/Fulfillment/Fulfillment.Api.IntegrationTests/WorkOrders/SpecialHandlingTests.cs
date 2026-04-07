using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.WorkOrders;

/// <summary>
/// Integration tests for Slices 36 (Cold Pack) and 37 (Hazmat Policy).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class SpecialHandlingTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public SpecialHandlingTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _fixture.FrozenClock.SetUtcNow(DateTimeOffset.UtcNow);
        return _fixture.CleanAllDocumentsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // --- Slice 36: Cold Pack ---

    [Fact]
    public async Task ApplyColdPack_PackingStarted_Appends_Event()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Cold St", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-COLD-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Progress to PackingStarted
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-COLD"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-Cold"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-COLD-001", 1, "A-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));

        // Apply cold pack
        await _fixture.ExecuteAndWaitAsync(
            new ApplyColdPack(workOrderId, ["SKU-COLD-001"], "GelPack"));

        await using var session2 = _fixture.GetDocumentSession();
        var events = await session2.Events.FetchStreamAsync(workOrderId);
        var coldPack = events.Select(e => e.Data).OfType<ColdPackApplied>().FirstOrDefault();
        coldPack.ShouldNotBeNull();
        coldPack.PackType.ShouldBe("GelPack");
    }

    [Fact]
    public async Task ApplyColdPack_Wrong_Status_Is_Rejected()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 No Cold", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-NOCOLD-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var workOrderId = WorkOrder.StreamId(shipment.Id, shipment.AssignedFulfillmentCenter!);

        // Work order is in Created status — not PackingStarted
        await _fixture.ExecuteAndWaitAsync(
            new ApplyColdPack(workOrderId, ["SKU-NOCOLD-001"], "GelPack"));

        await using var session2 = _fixture.GetDocumentSession();
        var events = await session2.Events.FetchStreamAsync(workOrderId);
        events.Select(e => e.Data).OfType<ColdPackApplied>().ShouldBeEmpty();
    }

    // --- Slice 37: Hazmat Policy ---

    [Fact]
    public async Task HazmatPolicy_Detects_Flagged_SKU()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Hazmat St", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem>
            {
                new("FLEA-SPRAY-001", 1),
                new("SKU-SAFE-001", 1)
            },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var workOrderId = WorkOrder.StreamId(shipment.Id, shipment.AssignedFulfillmentCenter!);

        var events = await session.Events.FetchStreamAsync(workOrderId);
        var hazmatFlags = events.Select(e => e.Data).OfType<HazmatItemFlagged>().ToList();
        hazmatFlags.ShouldNotBeEmpty("FLEA- prefix should trigger hazmat flagging");
        hazmatFlags.First().Sku.ShouldBe("FLEA-SPRAY-001");

        var restrictions = events.Select(e => e.Data).OfType<HazmatShippingRestrictionApplied>().ToList();
        restrictions.ShouldNotBeEmpty("Hazmat items should trigger shipping restriction");
        restrictions.First().RestrictedService.ShouldBe("AirShipping");
    }

    [Fact]
    public async Task HazmatPolicy_Safe_SKUs_No_Flagging()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId, Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "200 Safe St", City = "Newark",
                StateProvince = "NJ", PostalCode = "07102", Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SAFE-TOY-001", 1) },
            "Ground", DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var workOrderId = WorkOrder.StreamId(shipment.Id, shipment.AssignedFulfillmentCenter!);

        var events = await session.Events.FetchStreamAsync(workOrderId);
        events.Select(e => e.Data).OfType<HazmatItemFlagged>().ShouldBeEmpty();
        events.Select(e => e.Data).OfType<HazmatShippingRestrictionApplied>().ShouldBeEmpty();
    }
}
