using Alba;
using Orders.Checkout;
using ShoppingContracts = Messages.Contracts.Shopping;

namespace Orders.Api.IntegrationTests.Checkout;

/// <summary>
/// Alba-based HTTP integration tests for CheckoutInitiatedHandler.
/// Verifies Shopping.CheckoutInitiated message handling creates Checkout aggregate
/// and can be retrieved via GET /api/checkouts/{id}.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CheckoutInitiatedHandlerHttpTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CheckoutInitiatedHandlerHttpTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GET_Checkout_AfterCheckoutInitiatedHandled_ReturnsCheckoutData()
    {
        // Arrange - Simulate Shopping BC publishing CheckoutInitiated
        var checkoutId = Guid.CreateVersion7();
        var cartId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var items = new List<ShoppingContracts.CheckoutLineItem>
        {
            new("DOG-BOWL-01", 2, 19.99m),
            new("CAT-TOY-01", 1, 9.99m)
        };

        var message = new ShoppingContracts.CheckoutInitiated(
            checkoutId,
            cartId,
            customerId,
            items,
            DateTimeOffset.UtcNow);

        // Act - Handle message (simulates RabbitMQ delivery)
        await _fixture.ExecuteAndWaitAsync(message);

        // Assert - Verify checkout can be retrieved via HTTP GET
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/checkouts/{checkoutId}");
            x.StatusCodeShouldBe(200);
        });

        var checkout = result.ReadAsJson<CheckoutDto>();
        checkout.ShouldNotBeNull();
        checkout.CheckoutId.ShouldBe(checkoutId);
        checkout.CartId.ShouldBe(cartId);
        checkout.CustomerId.ShouldBe(customerId);
        checkout.Items.Count.ShouldBe(2);
        checkout.Items[0].Sku.ShouldBe("DOG-BOWL-01");
        checkout.Items[0].Quantity.ShouldBe(2);
        checkout.Items[0].UnitPrice.ShouldBe(19.99m);
        checkout.Items[1].Sku.ShouldBe("CAT-TOY-01");
        checkout.Subtotal.ShouldBe(49.97m); // (2 * 19.99) + (1 * 9.99)
        checkout.Total.ShouldBe(49.97m); // No shipping cost yet
        checkout.IsCompleted.ShouldBeFalse();
        checkout.HasPaymentMethod.ShouldBeFalse();
    }

    [Fact]
    public async Task GET_Checkout_WithNonExistentId_Returns404()
    {
        // Arrange - Non-existent checkout ID
        var nonExistentId = Guid.CreateVersion7();

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/checkouts/{nonExistentId}");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task CheckoutInitiatedHandler_WithMultipleItems_CalculatesCorrectSubtotal()
    {
        // Arrange - CheckoutInitiated with multiple items
        var checkoutId = Guid.CreateVersion7();
        var items = new List<ShoppingContracts.CheckoutLineItem>
        {
            new("SKU-001", 3, 10.00m),
            new("SKU-002", 2, 15.50m),
            new("SKU-003", 1, 5.99m)
        };

        var message = new ShoppingContracts.CheckoutInitiated(
            checkoutId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            items,
            DateTimeOffset.UtcNow);

        // Act - Handle message and retrieve checkout
        await _fixture.ExecuteAndWaitAsync(message);

        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/checkouts/{checkoutId}");
            x.StatusCodeShouldBe(200);
        });

        // Assert - Verify subtotal calculation
        var checkout = result.ReadAsJson<CheckoutDto>();
        checkout.Items.Count.ShouldBe(3);
        checkout.Subtotal.ShouldBe(66.99m); // (3*10) + (2*15.50) + (1*5.99) = 30 + 31 + 5.99
    }

    [Fact]
    public async Task CheckoutInitiatedHandler_WithAnonymousCustomer_CreatesCheckoutWithNullCustomerId()
    {
        // Arrange - Anonymous checkout (null customerId)
        var checkoutId = Guid.CreateVersion7();
        var items = new List<ShoppingContracts.CheckoutLineItem>
        {
            new("SKU-001", 1, 25.00m)
        };

        var message = new ShoppingContracts.CheckoutInitiated(
            checkoutId,
            Guid.CreateVersion7(),
            null, // Anonymous
            items,
            DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteAndWaitAsync(message);

        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/checkouts/{checkoutId}");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        var checkout = result.ReadAsJson<CheckoutDto>();
        checkout.CustomerId.ShouldBeNull();
    }

    // DTO matching Orders.Checkout projection shape
    private record CheckoutDto(
        Guid CheckoutId,
        Guid CartId,
        Guid? CustomerId,
        List<CheckoutLineItemDto> Items,
        DateTimeOffset StartedAt,
        bool HasPaymentMethod,
        bool IsCompleted,
        decimal Subtotal,
        decimal Total);

    private record CheckoutLineItemDto(
        string Sku,
        int Quantity,
        decimal UnitPrice,
        decimal LineTotal);
}
