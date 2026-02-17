using System.Net;
using Marten;
using Orders.Placement;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Integration tests for the full order placement flow.
/// Tests the complete flow from CheckoutCompleted message to queryable order.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class OrderPlacementFlowTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public OrderPlacementFlowTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Tests the full order placement flow:
    /// 1. Send CheckoutCompleted message through Wolverine
    /// 2. Verify saga is created and persisted
    /// 3. Verify order is queryable via HTTP endpoint
    /// 
    /// **Validates: Requirements 1.1, 2.1, 4.1, 6.1**
    /// </summary>
    [Fact]
    public async Task Full_Order_Placement_Flow_Creates_Queryable_Order()
    {
        // Arrange: Create a valid CheckoutCompleted event
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(), // OrderId
            Guid.CreateVersion7(), // CheckoutId
            Guid.NewGuid(), // CustomerId
            new List<CheckoutLineItem>
            {
                new("SKU-001", 2, 19.99m),
                new("SKU-002", 1, 49.99m)
            },
            new ShippingAddress(
                "123 Main St",
                null,
                "Seattle",
                "WA",
                "98101",
                "USA"),
            "Standard",
            5.99m,
            "tok_visa_test",
            DateTimeOffset.UtcNow);

        // Act: Send the CheckoutCompleted message through Wolverine and wait for all side effects
        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        // Assert: Verify saga was created and persisted
        await using var session = _fixture.GetDocumentSession();
        
        // Query for orders by customer ID to find the created order
        var orders = await session.Query<Order>()
            .Where(o => o.CustomerId == checkoutCompleted.CustomerId)
            .ToListAsync();

        orders.ShouldNotBeEmpty();
        var order = orders.First();

        // Verify order properties match checkout data
        order.Status.ShouldBe(OrderStatus.Placed);
        order.CustomerId.ShouldBe(checkoutCompleted.CustomerId!.Value);
        order.ShippingMethod.ShouldBe(checkoutCompleted.ShippingMethod);
        order.PaymentMethodToken.ShouldBe(checkoutCompleted.PaymentMethodToken);
        order.LineItems.Count.ShouldBe(checkoutCompleted.Items.Count);
        order.PlacedAt.ShouldNotBe(default);

        // Verify line items
        order.LineItems[0].Sku.ShouldBe("SKU-001");
        order.LineItems[0].Quantity.ShouldBe(2);
        order.LineItems[0].UnitPrice.ShouldBe(19.99m);
        order.LineItems[0].LineTotal.ShouldBe(39.98m);

        order.LineItems[1].Sku.ShouldBe("SKU-002");
        order.LineItems[1].Quantity.ShouldBe(1);
        order.LineItems[1].UnitPrice.ShouldBe(49.99m);
        order.LineItems[1].LineTotal.ShouldBe(49.99m);

        // Verify total amount
        order.TotalAmount.ShouldBe(95.96m); // 89.97 + 5.99 shipping

        // Verify shipping address
        order.ShippingAddress.Street.ShouldBe("123 Main St");
        order.ShippingAddress.City.ShouldBe("Seattle");
        order.ShippingAddress.State.ShouldBe("WA");
        order.ShippingAddress.PostalCode.ShouldBe("98101");
        order.ShippingAddress.Country.ShouldBe("USA");

        // Act: Query the order via HTTP endpoint
        var response = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/orders/{order.Id}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var orderResponse = await response.ReadAsJsonAsync<OrderResponse>();

        // Assert: HTTP response matches persisted order
        orderResponse.ShouldNotBeNull();
        orderResponse.OrderId.ShouldBe(order.Id);
        orderResponse.CustomerId.ShouldBe(order.CustomerId);
        orderResponse.Status.ShouldBe(OrderStatus.Placed);
        orderResponse.TotalAmount.ShouldBe(order.TotalAmount);
        orderResponse.LineItems.Count.ShouldBe(order.LineItems.Count);
    }
}
