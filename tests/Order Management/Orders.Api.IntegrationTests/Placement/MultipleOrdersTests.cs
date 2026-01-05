using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Marten;
using Orders.Placement;
using Shouldly;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Integration tests verifying multiple orders can be created correctly,
/// including for the same customer and with varying sizes.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class MultipleOrdersTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public MultipleOrdersTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Verifies that multiple orders can be created for the same customer
    /// and each order is distinct with its own identifier.
    /// </summary>
    [Fact]
    public async Task Same_Customer_Can_Have_Multiple_Orders()
    {
        // Arrange: Same customer, different carts
        var customerId = Guid.NewGuid();
        
        var checkout1 = CreateValidCheckout(customerId, [new CheckoutLineItem("SKU-001", 2, 19.99m)]);
        var checkout2 = CreateValidCheckout(customerId, [new CheckoutLineItem("SKU-002", 1, 49.99m)]);
        var checkout3 = CreateValidCheckout(customerId, [new CheckoutLineItem("SKU-003", 3, 9.99m)]);

        // Act: Create three orders for the same customer
        await _fixture.ExecuteAndWaitAsync(checkout1);
        await _fixture.ExecuteAndWaitAsync(checkout2);
        await _fixture.ExecuteAndWaitAsync(checkout3);

        // Assert: All three orders should exist with distinct IDs
        await using var session = _fixture.GetDocumentSession();
        var orders = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .ToListAsync();

        orders.Count.ShouldBe(3, "Customer should have exactly 3 orders");
        
        // All order IDs should be unique
        var uniqueIds = orders.Select(o => o.Id).Distinct().ToList();
        uniqueIds.Count.ShouldBe(3, "All order IDs should be unique");

        // Verify each order has correct line items
        orders.ShouldContain(o => o.LineItems.Any(li => li.Sku == "SKU-001"));
        orders.ShouldContain(o => o.LineItems.Any(li => li.Sku == "SKU-002"));
        orders.ShouldContain(o => o.LineItems.Any(li => li.Sku == "SKU-003"));
    }

    /// <summary>
    /// Verifies that orders with many line items (large orders) are handled correctly.
    /// Tests serialization and persistence of larger payloads.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task Large_Orders_With_Many_LineItems_Are_Handled_Correctly(int lineItemCount)
    {
        // Arrange: Create checkout with many line items
        var customerId = Guid.NewGuid();
        var lineItems = Enumerable.Range(1, lineItemCount)
            .Select(i => new CheckoutLineItem($"SKU-{i:D4}", i % 10 + 1, (decimal)(i * 1.99)))
            .ToList();

        var checkout = CreateValidCheckout(customerId, lineItems);
        var lineItemsTotal = lineItems.Sum(li => li.Quantity * li.PriceAtPurchase);
        var expectedTotal = lineItemsTotal + checkout.ShippingCost;

        // Act
        await _fixture.ExecuteAndWaitAsync(checkout);

        // Assert
        await using var session = _fixture.GetDocumentSession();
        var orders = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .ToListAsync();

        orders.Count.ShouldBe(1);
        var order = orders.First();

        order.LineItems.Count.ShouldBe(lineItemCount);
        order.TotalAmount.ShouldBe(expectedTotal);
        order.Status.ShouldBe(OrderStatus.Placed);

        // Verify all line items are preserved
        for (var i = 0; i < lineItemCount; i++)
        {
            var originalItem = lineItems[i];
            var orderItem = order.LineItems[i];
            
            orderItem.Sku.ShouldBe(originalItem.Sku);
            orderItem.Quantity.ShouldBe(originalItem.Quantity);
            orderItem.UnitPrice.ShouldBe(originalItem.PriceAtPurchase);
        }
    }

    /// <summary>
    /// Verifies that order total calculation is accurate across many random inputs.
    /// Property-based test for total amount correctness.
    /// </summary>
    [Property(MaxTest = 25, Arbitrary = [typeof(ValidCheckoutForTotalArbitrary)])]
    public async Task Order_Total_Is_Calculated_Correctly(CheckoutCompleted checkout)
    {
        // Calculate expected total (line items + shipping)
        var lineItemsTotal = checkout.LineItems.Sum(li => li.Quantity * li.PriceAtPurchase);
        var expectedTotal = lineItemsTotal + checkout.ShippingCost;

        // Act
        await _fixture.ExecuteAndWaitAsync(checkout);

        // Assert
        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == checkout.CustomerId)
            .FirstOrDefaultAsync();

        order.ShouldNotBeNull();
        order.TotalAmount.ShouldBe(expectedTotal, $"Total should be {expectedTotal} (line items: {lineItemsTotal} + shipping: {checkout.ShippingCost}) but was {order.TotalAmount}");

        // Verify each line total
        for (var i = 0; i < checkout.LineItems.Count; i++)
        {
            var checkoutItem = checkout.LineItems[i];
            var orderItem = order.LineItems[i];
            var expectedLineTotal = checkoutItem.Quantity * checkoutItem.PriceAtPurchase;
            
            orderItem.LineTotal.ShouldBe(expectedLineTotal);
        }

        // Clean up for next property iteration
        await _fixture.CleanAllDocumentsAsync();
    }

    /// <summary>
    /// Verifies that different carts result in different orders even with identical content.
    /// Each CheckoutCompleted should create a new order regardless of content similarity.
    /// </summary>
    [Fact]
    public async Task Identical_Content_Different_Carts_Creates_Separate_Orders()
    {
        // Arrange: Same content, different cart IDs
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem> { new("SKU-001", 2, 19.99m) };
        
        var checkout1 = new CheckoutCompleted(
            Guid.CreateVersion7(), // OrderId
            Guid.CreateVersion7(), // CheckoutId
            customerId,
            lineItems,
            new ShippingAddress("123 Main St", null, "Seattle", "WA", "98101", "USA"),
            "Standard",
            5.99m, // ShippingCost
            "tok_visa",
            DateTimeOffset.UtcNow);

        var checkout2 = new CheckoutCompleted(
            Guid.CreateVersion7(), // OrderId
            Guid.CreateVersion7(), // CheckoutId
            customerId,
            lineItems, // Same content
            new ShippingAddress("123 Main St", null, "Seattle", "WA", "98101", "USA"),
            "Standard",
            5.99m, // ShippingCost
            "tok_visa",
            DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteAndWaitAsync(checkout1);
        await _fixture.ExecuteAndWaitAsync(checkout2);

        // Assert: Two separate orders should exist
        await using var session = _fixture.GetDocumentSession();
        var orders = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .ToListAsync();

        orders.Count.ShouldBe(2, "Two separate orders should be created");
        orders[0].Id.ShouldNotBe(orders[1].Id, "Order IDs should be different");
    }

    private static CheckoutCompleted CreateValidCheckout(Guid customerId, IReadOnlyList<CheckoutLineItem> lineItems) =>
        new(
            OrderId: Guid.CreateVersion7(),
            CheckoutId: Guid.CreateVersion7(),
            CustomerId: customerId,
            LineItems: lineItems,
            ShippingAddress: new ShippingAddress("123 Main St", null, "Seattle", "WA", "98101", "USA"),
            ShippingMethod: "Standard",
            ShippingCost: 5.99m,
            PaymentMethodToken: "tok_visa",
            CompletedAt: DateTimeOffset.UtcNow);
}

/// <summary>
/// Arbitrary for generating valid CheckoutCompleted events for total calculation tests.
/// Uses unique customer IDs to avoid conflicts between property test iterations.
/// </summary>
public static class ValidCheckoutForTotalArbitrary
{
    public static Arbitrary<CheckoutCompleted> CheckoutCompleted()
    {
        var lineItemGen = Gen.Choose(1, 10)
            .SelectMany(quantity => Gen.Choose(100, 10000)
                .Select(priceInCents => new CheckoutLineItem(
                    $"SKU-{Guid.NewGuid():N}".Substring(0, 12),
                    quantity,
                    (decimal)priceInCents / 100)));

        var lineItemsGen = lineItemGen
            .ListOf()
            .Where(items => items.Count > 0 && items.Count <= 10)
            .Select(items => (IReadOnlyList<CheckoutLineItem>)items.ToList());

        var checkoutGen = lineItemsGen
            .Select(lineItems => new Orders.Placement.CheckoutCompleted(
                Guid.CreateVersion7(), // OrderId
                Guid.CreateVersion7(), // CheckoutId
                Guid.NewGuid(), // Unique customer ID per test
                lineItems,
                new ShippingAddress("123 Test St", null, "TestCity", "TS", "12345", "USA"),
                "Standard",
                5.99m, // ShippingCost
                "tok_test",
                DateTimeOffset.UtcNow));

        return checkoutGen.ToArbitrary();
    }
}
