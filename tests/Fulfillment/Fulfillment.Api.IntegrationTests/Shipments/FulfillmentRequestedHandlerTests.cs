using Fulfillment.Shipments;
using Marten;
using Marten.Linq;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for FulfillmentRequestedHandler (P0.5).
/// Verifies the UUID v5 deterministic stream key ensures exactly-once shipment creation
/// under at-least-once delivery semantics.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class FulfillmentRequestedHandlerTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public FulfillmentRequestedHandlerTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static IntegrationContracts.FulfillmentRequested BuildFulfillmentRequested(
        Guid orderId,
        Guid? customerId = null) =>
        new(
            orderId,
            customerId ?? Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "500 Idempotent Blvd",
                City = "Denver",
                StateProvince = "CO",
                PostalCode = "80202",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-UUID5-001", 3) },
            "Standard",
            DateTimeOffset.UtcNow);

    /// <summary>
    /// When the same FulfillmentRequested message is received twice for the same OrderId
    /// (at-least-once delivery duplication), the UUID v5 deterministic stream key must
    /// ensure only ONE shipment stream is created in Marten.
    /// The second handler invocation detects the existing stream via FetchStreamStateAsync
    /// and returns early — no exception thrown, no duplicate event appended.
    /// The net result: exactly one shipment per OrderId.
    /// </summary>
    [Fact]
    public async Task FulfillmentRequested_Same_OrderId_Creates_Same_ShipmentId()
    {
        // Arrange: build the same FulfillmentRequested twice (simulating message duplication)
        var orderId = Guid.NewGuid();
        var message = BuildFulfillmentRequested(orderId);

        // Act: send the integration message twice — the second is a true no-op (stream exists guard)
        await _fixture.ExecuteAndWaitAsync(message);
        await _fixture.ExecuteAndWaitAsync(message); // duplicate — handler returns early idempotently

        // Assert: only ONE shipment exists for this OrderId
        await using var session = _fixture.GetDocumentSession();
        var shipments = await session.Query<Shipment>()
            .Where(s => s.OrderId == orderId)
            .ToListAsync();

        shipments.Count.ShouldBe(1,
            "UUID v5 idempotency must ensure exactly one shipment stream per OrderId");
        shipments[0].OrderId.ShouldBe(orderId);
        shipments[0].Status.ShouldBe(ShipmentStatus.Pending);
    }

    /// <summary>
    /// Two different OrderIds must always produce different ShipmentIds.
    /// Verifies the UUID v5 namespace+input uniqueness property.
    /// </summary>
    [Fact]
    public async Task FulfillmentRequested_Different_OrderIds_Create_Different_ShipmentIds()
    {
        // Arrange
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // Act
        await _fixture.ExecuteAndWaitAsync(BuildFulfillmentRequested(orderId1, customerId));
        await _fixture.ExecuteAndWaitAsync(BuildFulfillmentRequested(orderId2, customerId));

        // Assert: two separate streams with unique IDs
        await using var session = _fixture.GetDocumentSession();
        var shipment1 = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId1);
        var shipment2 = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId2);

        shipment1.Id.ShouldNotBe(shipment2.Id,
            "Different OrderIds must produce different UUID v5 stream keys");
        shipment1.OrderId.ShouldBe(orderId1);
        shipment2.OrderId.ShouldBe(orderId2);
    }
}
