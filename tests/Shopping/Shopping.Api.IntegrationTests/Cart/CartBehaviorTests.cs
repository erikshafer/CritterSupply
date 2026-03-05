using Marten;
using Shopping.Cart;

namespace Shopping.Api.IntegrationTests.Cart;

/// <summary>
/// Alba-based tests for cart business logic and behaviors.
/// Tests price handling, item accumulation, and other domain rules.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class CartBehaviorTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CartBehaviorTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddItem_SameSkuTwice_AccumulatesQuantity()
    {
        // Arrange
        var cart = await CreateCart();

        // Act - Add same SKU twice
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m))
                .ToUrl($"/api/carts/{cart.Id}/items");
            x.StatusCodeShouldBe(204);
        });

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(cart.Id, "SKU-001", 3, 19.99m))
                .ToUrl($"/api/carts/{cart.Id}/items");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Should accumulate to 5 total
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.Items["SKU-001"].Quantity.ShouldBe(5);
    }

    [Fact]
    public async Task AddItem_SameSkuWithDifferentPrices_UsesFirstPrice()
    {
        // Arrange
        var cart = await CreateCart();

        // Act - Add same SKU with different prices
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m))
                .ToUrl($"/api/carts/{cart.Id}/items");
            x.StatusCodeShouldBe(204);
        });

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(cart.Id, "SKU-001", 1, 29.99m))
                .ToUrl($"/api/carts/{cart.Id}/items");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Should keep first price (19.99) and accumulate quantity
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.Items["SKU-001"].Quantity.ShouldBe(3);
        updatedCart.Items["SKU-001"].UnitPrice.ShouldBe(19.99m);
    }

    [Fact]
    public async Task ClearCart_WithMultipleItems_RemovesAllItems()
    {
        // Arrange - Cart with multiple items
        var cart = await CreateCart();
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-002", 1, 29.99m));
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-003", 5, 9.99m));

        // Act - Clear cart via HTTP (DELETE doesn't support body, Reason will be null)
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}");
            x.StatusCodeShouldBe(204);
        });

        // Assert - All items removed, cart is terminal
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.Items.ShouldBeEmpty();
        updatedCart.Status.ShouldBe(CartStatus.Cleared);
        updatedCart.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public async Task ClearCart_WithEmptyCart_Succeeds()
    {
        // Arrange - Empty cart
        var cart = await CreateCart();

        // Act - Clear empty cart (DELETE doesn't support body, Reason will be null)
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Cart should be cleared (terminal)
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.Status.ShouldBe(CartStatus.Cleared);
        updatedCart.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public async Task ClearCart_TwiceInRow_Returns400OnSecondAttempt()
    {
        // Arrange - Create and clear cart
        var cart = await CreateCart();
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 1, 10.00m));
        await _fixture.ExecuteAndWaitAsync(new ClearCart(cart.Id, "First clear"));

        // Act & Assert - Second clear attempt should fail
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Cannot clear a cart");
        });
    }

    [Fact]
    public async Task GET_Cart_CalculatesTotalAmount_WithMultipleItems()
    {
        // Arrange - Cart with items
        var cart = await CreateCart();
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));  // 39.98
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-002", 1, 29.99m));  // 29.99
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-003", 3, 5.00m));   // 15.00

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/carts/{cart.Id}");
            x.StatusCodeShouldBe(200);
        });

        var cartResponse = await result.ReadAsJsonAsync<CartResponse>();

        // Assert - Total should be 84.97
        cartResponse.TotalAmount.ShouldBe(84.97m);
        cartResponse.Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task RemoveItem_ThenAddBack_Succeeds()
    {
        // Arrange - Cart with item
        var cart = await CreateCart();
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 5, 19.99m));

        // Act - Remove item
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}/items/SKU-001");
            x.StatusCodeShouldBe(204);
        });

        // Act - Add same item back
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(cart.Id, "SKU-001", 3, 19.99m))
                .ToUrl($"/api/carts/{cart.Id}/items");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Item should be back with new quantity
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.Items["SKU-001"].Quantity.ShouldBe(3);
    }

    [Fact]
    public async Task ChangeQuantity_ToSameValue_Succeeds()
    {
        // Arrange
        var cart = await CreateCart();
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 5, 19.99m));

        // Act - Change quantity to same value (idempotent)
        await _fixture.Host.Scenario(x =>
        {
            x.Put.Json(new ChangeItemQuantity(cart.Id, "SKU-001", 5))
                .ToUrl($"/api/carts/{cart.Id}/items/SKU-001/quantity");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Quantity unchanged
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.Items["SKU-001"].Quantity.ShouldBe(5);
    }

    private async Task<Shopping.Cart.Cart> CreateCart()
    {
        var customerId = Guid.CreateVersion7();
        await _fixture.ExecuteAndWaitAsync(new InitializeCart(customerId, null));

        using var session = _fixture.GetDocumentSession();
        return (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();
    }

    // DTO matching GetCart response shape
    private record CartResponse(
        Guid CartId,
        Guid? CustomerId,
        string? SessionId,
        string Status,
        List<CartItemDto> Items,
        decimal TotalAmount);

    private record CartItemDto(string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);
}
