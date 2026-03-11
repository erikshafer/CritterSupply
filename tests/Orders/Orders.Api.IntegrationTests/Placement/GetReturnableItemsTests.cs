using Marten;
using Marten.Linq;
using Messages.Contracts.Fulfillment;
using Orders.Placement;
using Shouldly;
using OrdersShippingAddress = Orders.Placement.ShippingAddress;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Integration tests for GET /api/orders/{orderId}/returnable-items (P0.5).
/// Verifies the endpoint returns line items for delivered orders,
/// 404 for non-existent orders, and 400 for orders not yet delivered.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class GetReturnableItemsTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public GetReturnableItemsTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // Helper: advance an order to Delivered status
    // ---------------------------------------------------------------------------

    private async Task<Order> CreateAndDeliverOrderAsync()
    {
        var customerId = Guid.NewGuid();
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            customerId,
            [new Orders.Placement.CheckoutLineItem("SKU-RET-001", 2, 19.99m),
             new Orders.Placement.CheckoutLineItem("SKU-RET-002", 1, 49.99m)],
            new OrdersShippingAddress("100 Return Rd", null, "Chicago", "IL", "60601", "USA"),
            "Standard",
            5.99m,
            "tok_visa_test",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        Order order;
        await using (var getSession = _fixture.GetDocumentSession())
        {
            order = (await getSession.Query<Order>()
                .Where(o => o.CustomerId == customerId)
                .ToListAsync()).First();
        }

        // Payment captured
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Payments.PaymentCaptured(
            Guid.NewGuid(), order.Id, 89.97m, "txn_ret_test", DateTimeOffset.UtcNow));

        // Inventory reserved + committed (single SKU per distinct item)
        var resId1 = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationConfirmed(
            order.Id, Guid.NewGuid(), resId1, "SKU-RET-001", "WH-RET-01", 2, DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationCommitted(
            order.Id, Guid.NewGuid(), resId1, "SKU-RET-001", "WH-RET-01", 2, DateTimeOffset.UtcNow));

        var resId2 = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationConfirmed(
            order.Id, Guid.NewGuid(), resId2, "SKU-RET-002", "WH-RET-01", 1, DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationCommitted(
            order.Id, Guid.NewGuid(), resId2, "SKU-RET-002", "WH-RET-01", 1, DateTimeOffset.UtcNow));

        // Shipped
        var shipmentId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ShipmentDispatched(
            order.Id, shipmentId, "FedEx", "FEDEX-RET-001", DateTimeOffset.UtcNow));

        // Delivered
        await _fixture.ExecuteAndWaitAsync(new ShipmentDelivered(
            order.Id, shipmentId, DateTimeOffset.UtcNow, "Test Recipient"));

        // Reload to confirm Delivered status
        await using var finalSession = _fixture.GetDocumentSession();
        var deliveredOrder = await finalSession.LoadAsync<Order>(order.Id);
        deliveredOrder.ShouldNotBeNull();
        deliveredOrder.Status.ShouldBe(OrderStatus.Delivered);

        return deliveredOrder;
    }

    // ===========================================================================
    // Happy path — Delivered order
    // ===========================================================================

    /// <summary>
    /// For a delivered order, the endpoint must return HTTP 200 with all line items
    /// mapped to ReturnableItem records (Sku, Quantity, UnitPrice, LineTotal).
    /// </summary>
    [Fact]
    public async Task GetReturnableItems_For_Delivered_Order_Returns_Items()
    {
        // Arrange: get an order to Delivered status
        var order = await CreateAndDeliverOrderAsync();

        // Act: call the endpoint
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/orders/{order.Id}/returnable-items");
            s.StatusCodeShouldBe(200);
        });

        var body = await response.ReadAsJsonAsync<ReturnableItemsResponse>();
        body.ShouldNotBeNull();
        body.OrderId.ShouldBe(order.Id);
        body.Items.ShouldNotBeEmpty();
        body.Items.Count.ShouldBe(2);

        var sku001 = body.Items.FirstOrDefault(i => i.Sku == "SKU-RET-001");
        sku001.ShouldNotBeNull();
        sku001.Quantity.ShouldBe(2);
        sku001.UnitPrice.ShouldBe(19.99m);

        var sku002 = body.Items.FirstOrDefault(i => i.Sku == "SKU-RET-002");
        sku002.ShouldNotBeNull();
        sku002.Quantity.ShouldBe(1);
        sku002.UnitPrice.ShouldBe(49.99m);
    }

    // ===========================================================================
    // Error cases
    // ===========================================================================

    /// <summary>
    /// Requesting returnable items for a non-existent order must return HTTP 404.
    /// </summary>
    [Fact]
    public async Task GetReturnableItems_For_NonExistent_Order_Returns_404()
    {
        var nonExistentOrderId = Guid.NewGuid();

        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/orders/{nonExistentOrderId}/returnable-items");
            s.StatusCodeShouldBe(404);
        });
    }

    /// <summary>
    /// Requesting returnable items for a Placed (not yet delivered) order must return HTTP 400.
    /// Items are only returnable after successful delivery.
    /// </summary>
    [Fact]
    public async Task GetReturnableItems_For_Placed_Order_Returns_400()
    {
        // Arrange: create an order but do NOT advance it to Delivered
        var customerId = Guid.NewGuid();
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            customerId,
            [new Orders.Placement.CheckoutLineItem("SKU-RET-003", 1, 9.99m)],
            new OrdersShippingAddress("200 Placed St", null, "Austin", "TX", "78701", "USA"),
            "Standard",
            5.99m,
            "tok_visa_test",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        Order order;
        await using (var getSession = _fixture.GetDocumentSession())
        {
            order = (await getSession.Query<Order>()
                .Where(o => o.CustomerId == customerId)
                .ToListAsync()).First();
        }

        // Order is in Placed status — not eligible for returns
        order.Status.ShouldBe(OrderStatus.Placed);

        // Act + Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/orders/{order.Id}/returnable-items");
            s.StatusCodeShouldBe(400);
        });
    }

    // ---------------------------------------------------------------------------
    // Response DTO (mirrors Orders.Api.Queries.ReturnableItemsResponse)
    // ---------------------------------------------------------------------------

    private sealed record ReturnableItemsResponse(
        Guid OrderId,
        IReadOnlyList<ReturnableItem> Items,
        DateTimeOffset? DeliveredAt);

    private sealed record ReturnableItem(
        string Sku,
        int Quantity,
        decimal UnitPrice,
        decimal LineTotal);
}
