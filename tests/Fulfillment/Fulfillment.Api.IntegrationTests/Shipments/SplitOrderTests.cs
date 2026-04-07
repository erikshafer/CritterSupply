using Fulfillment.Routing;
using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for Slice 32 (Multi-FC Split Order Routing).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class SplitOrderTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public SplitOrderTests(TestFixture fixture)
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
    public async Task SplitOrderIntoShipments_Creates_Multiple_Shipment_Streams()
    {
        var orderId = Guid.NewGuid();
        var address = new ShippingAddress("100 Split Way", null, "Newark", "NJ", "07102", "USA");
        var lineItems = new List<FulfillmentLineItem>
        {
            new("SKU-SPLIT-001", 2),
            new("SKU-SPLIT-002", 1)
        };

        var splits = new List<SplitProposal>
        {
            new("NJ-FC", new List<FulfillmentLineItem> { new("SKU-SPLIT-001", 2) }),
            new("OH-FC", new List<FulfillmentLineItem> { new("SKU-SPLIT-002", 1) })
        };

        var tracked = await _fixture.ExecuteAndWaitAsync(
            new SplitOrderIntoShipments(
                orderId, Guid.NewGuid(), address, lineItems, "Ground",
                DateTimeOffset.UtcNow, splits));

        // Verify OrderSplitIntoShipments integration event
        var splitMsgs = tracked.Sent.MessagesOf<IntegrationContracts.OrderSplitIntoShipments>().ToList();
        splitMsgs.ShouldNotBeEmpty("OrderSplitIntoShipments should be published");
        splitMsgs.First().ShipmentCount.ShouldBe(2);

        // Verify two shipment streams were created
        await using var session = _fixture.GetDocumentSession();
        var shipments = await session.Query<Shipment>()
            .Where(s => s.OrderId == orderId)
            .ToListAsync();

        shipments.Count.ShouldBe(2);
        shipments.Select(s => s.AssignedFulfillmentCenter)
            .ShouldBe(new[] { "NJ-FC", "OH-FC" }, ignoreOrder: true);
    }

    [Fact]
    public async Task SplitOrderIntoShipments_Idempotent_On_Duplicate()
    {
        var orderId = Guid.NewGuid();
        var address = new ShippingAddress("200 Idemp Way", null, "Newark", "NJ", "07102", "USA");
        var lineItems = new List<FulfillmentLineItem> { new("SKU-IDEMP-001", 1) };

        var splits = new List<SplitProposal>
        {
            new("NJ-FC", lineItems)
        };

        var command = new SplitOrderIntoShipments(
            orderId, Guid.NewGuid(), address, lineItems, "Ground",
            DateTimeOffset.UtcNow, splits);

        await _fixture.ExecuteAndWaitAsync(command);
        await _fixture.ExecuteAndWaitAsync(command);

        // Should still have exactly 1 shipment (idempotency via stream state check)
        await using var session = _fixture.GetDocumentSession();
        var shipments = await session.Query<Shipment>()
            .Where(s => s.OrderId == orderId)
            .ToListAsync();
        shipments.Count.ShouldBe(1);
    }
}
