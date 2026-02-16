using Orders.Placement;
using Messages.Contracts.CustomerIdentity;
using ShoppingContracts = Messages.Contracts.Shopping;

namespace Orders.Api.IntegrationTests.Placement;

[Collection(IntegrationTestCollection.Name)]
public class CheckoutToOrderIntegrationTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CheckoutToOrderIntegrationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CheckoutCompleted_StartsOrderSaga()
    {
        // Arrange - Simulate Shopping BC completing checkout
        var orderId = Guid.CreateVersion7();
        var checkoutId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();

        var items = new List<ShoppingContracts.CheckoutLineItem>
        {
            new("SKU-001", 2, 19.99m),
            new("SKU-002", 1, 39.99m)
        };

        var shippingAddress = new AddressSnapshot(
            "123 Main St",
            "Apt 4B",
            "Seattle",
            "WA",
            "98101",
            "USA");

        var message = new ShoppingContracts.CheckoutCompleted(
            orderId,
            checkoutId,
            customerId,
            items,
            shippingAddress,
            "Standard Ground",
            5.99m,
            "tok_visa_4242",
            DateTimeOffset.UtcNow);

        // Act - Handle the integration message
        await _fixture.ExecuteAndWaitAsync(message);

        // Assert - Verify Order saga was created
        using var session = _fixture.GetDocumentSession();
        var order = await session.LoadAsync<Order>(orderId);

        order.ShouldNotBeNull();
        order.Id.ShouldBe(orderId);
        order.CustomerId.ShouldBe(customerId);
        order.Status.ShouldBe(OrderStatus.Placed);
        order.LineItems.Count.ShouldBe(2);
        order.LineItems[0].Sku.ShouldBe("SKU-001");
        order.LineItems[0].Quantity.ShouldBe(2);
        order.LineItems[0].UnitPrice.ShouldBe(19.99m);
        order.LineItems[1].Sku.ShouldBe("SKU-002");
        order.ShippingAddress.ShouldNotBeNull();
        order.ShippingAddress.Street.ShouldBe("123 Main St");
        order.ShippingAddress.City.ShouldBe("Seattle");
        order.ShippingAddress.State.ShouldBe("WA");
        order.ShippingMethod.ShouldBe("Standard Ground");
        order.PaymentMethodToken.ShouldBe("tok_visa_4242");
    }

    [Fact]
    public async Task CheckoutCompleted_MapsAllCheckoutDataToOrderSaga()
    {
        // Arrange - Simulate Shopping BC completing checkout with rich data
        var orderId = Guid.CreateVersion7();
        var checkoutId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();

        var items = new List<ShoppingContracts.CheckoutLineItem>
        {
            new("SKU-001", 1, 29.99m),
            new("SKU-002", 3, 9.99m)
        };

        var shippingAddress = new AddressSnapshot(
            "456 Oak Ave",
            "Suite 200",
            "Portland",
            "OR",
            "97201",
            "USA");

        var message = new ShoppingContracts.CheckoutCompleted(
            orderId,
            checkoutId,
            customerId,
            items,
            shippingAddress,
            "Express Shipping",
            12.99m,
            "tok_mastercard_5555",
            DateTimeOffset.UtcNow);

        // Act - Handle the integration message
        await _fixture.ExecuteAndWaitAsync(message);

        // Assert - Verify all checkout data is correctly mapped to Order saga
        using var session = _fixture.GetDocumentSession();
        var order = await session.LoadAsync<Order>(orderId);

        order.ShouldNotBeNull();
        order.Id.ShouldBe(orderId);
        order.CustomerId.ShouldBe(customerId);
        order.Status.ShouldBe(OrderStatus.Placed);

        // Verify line items mapping
        order.LineItems.Count.ShouldBe(2);
        order.LineItems[0].Sku.ShouldBe("SKU-001");
        order.LineItems[0].Quantity.ShouldBe(1);
        order.LineItems[0].UnitPrice.ShouldBe(29.99m);
        order.LineItems[1].Sku.ShouldBe("SKU-002");
        order.LineItems[1].Quantity.ShouldBe(3);
        order.LineItems[1].UnitPrice.ShouldBe(9.99m);

        // Verify shipping address mapping (AddressLine1 → Street, StateOrProvince → State)
        order.ShippingAddress.ShouldNotBeNull();
        order.ShippingAddress.Street.ShouldBe("456 Oak Ave");
        order.ShippingAddress.Street2.ShouldBe("Suite 200");
        order.ShippingAddress.City.ShouldBe("Portland");
        order.ShippingAddress.State.ShouldBe("OR");
        order.ShippingAddress.PostalCode.ShouldBe("97201");
        order.ShippingAddress.Country.ShouldBe("USA");

        // Verify shipping and payment details
        order.ShippingMethod.ShouldBe("Express Shipping");
        order.PaymentMethodToken.ShouldBe("tok_mastercard_5555");
        order.TotalAmount.ShouldBe(72.95m); // (29.99 * 1) + (9.99 * 3) + 12.99 = 29.99 + 29.97 + 12.99
    }
}
