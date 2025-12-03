using System.Net;
using Alba;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Orders.Placement;
using Shouldly;
using Wolverine;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Integration tests for the full order placement flow.
/// Tests the complete flow from CheckoutCompleted message to queryable order.
/// </summary>
public class OrderPlacementFlowTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public OrderPlacementFlowTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

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
        var checkout = new CheckoutCompleted(
            CartId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            LineItems: new List<CheckoutLineItem>
            {
                new("SKU-001", 2, 19.99m),
                new("SKU-002", 1, 49.99m)
            },
            ShippingAddress: new ShippingAddress(
                "123 Main St",
                null,
                "Seattle",
                "WA",
                "98101",
                "USA"),
            ShippingMethod: "Standard",
            PaymentMethodToken: "tok_visa_test",
            AppliedDiscounts: null,
            CompletedAt: DateTimeOffset.UtcNow);

        // Act: Send the CheckoutCompleted message through Wolverine
        // Use a scope since IMessageBus is a scoped service
        using var scope = _fixture.Host.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(checkout);

        // Assert: Verify saga was created and persisted
        await using var session = _fixture.GetDocumentSession();
        
        // Query for orders by customer ID to find the created order
        var orders = await session.Query<Order>()
            .Where(o => o.CustomerId == checkout.CustomerId)
            .ToListAsync();

        orders.ShouldNotBeEmpty();
        var order = orders.First();

        // Verify order properties match checkout data
        order.Status.ShouldBe(OrderStatus.Placed);
        order.CustomerId.ShouldBe(checkout.CustomerId);
        order.ShippingMethod.ShouldBe(checkout.ShippingMethod);
        order.PaymentMethodToken.ShouldBe(checkout.PaymentMethodToken);
        order.LineItems.Count.ShouldBe(checkout.LineItems.Count);
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
        order.TotalAmount.ShouldBe(89.97m);

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
