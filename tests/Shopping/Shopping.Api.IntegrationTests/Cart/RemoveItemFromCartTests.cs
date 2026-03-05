using Marten;
using Shopping.Cart;

namespace Shopping.Api.IntegrationTests.Cart;

/// <summary>
/// Alba-based tests for RemoveItemFromCart HTTP endpoint.
/// Tests DELETE /api/carts/{cartId}/items/{sku} scenarios.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class RemoveItemFromCartTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public RemoveItemFromCartTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DELETE_RemoveItem_ExistingItem_Succeeds()
    {
        // Arrange
        var cart = await CreateCartWithItems();

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}/items/SKU-001");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Verify item removed
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.Items.ShouldNotContainKey("SKU-001");
        updatedCart.Items.ShouldContainKey("SKU-002"); // Other item still exists
    }

    [Fact]
    public async Task DELETE_RemoveItem_NonExistentItem_Returns404()
    {
        // Arrange
        var cart = await CreateCartWithItems();

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}/items/NON-EXISTENT-SKU");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task DELETE_RemoveItem_FromEmptyCart_Returns404()
    {
        // Arrange
        var cart = await CreateCart();

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}/items/SKU-001");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task DELETE_RemoveItem_FromNonExistentCart_Returns404()
    {
        // Arrange
        var nonExistentCartId = Guid.CreateVersion7();

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{nonExistentCartId}/items/SKU-001");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task DELETE_RemoveItem_FromClearedCart_Returns400()
    {
        // Arrange - Create cart with items, then clear it
        var cart = await CreateCartWithItems();
        await _fixture.ExecuteAndWaitAsync(new ClearCart(cart.Id, "Testing"));

        // Act & Assert - Cannot modify terminal cart
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}/items/SKU-001");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Cannot modify a cart");
        });
    }

    [Fact]
    public async Task DELETE_RemoveItem_FromCheckedOutCart_Returns400()
    {
        // Arrange - Create cart with items, then checkout
        var cart = await CreateCartWithItems();
        await _fixture.ExecuteAndWaitAsync(new InitiateCheckout(cart.Id));

        // Act & Assert - Cannot modify terminal cart
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}/items/SKU-001");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Cannot modify a cart");
        });
    }

    [Fact]
    public async Task DELETE_RemoveLastItem_LeavesCartEmpty()
    {
        // Arrange - Cart with single item
        var cart = await CreateCart();
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));

        // Act - Remove the only item
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}/items/SKU-001");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Cart should be empty but still active
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.Items.ShouldBeEmpty();
        updatedCart.Status.ShouldBe(CartStatus.Active);
    }

    [Fact]
    public async Task DELETE_RemoveMultipleItems_Succeeds()
    {
        // Arrange
        var cart = await CreateCartWithItems();

        // Act - Remove first item
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}/items/SKU-001");
            x.StatusCodeShouldBe(204);
        });

        // Act - Remove second item
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}/items/SKU-002");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Cart should be empty
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.Items.ShouldBeEmpty();
    }

    private async Task<Shopping.Cart.Cart> CreateCart()
    {
        var customerId = Guid.CreateVersion7();
        await _fixture.ExecuteAndWaitAsync(new InitializeCart(customerId, null));

        using var session = _fixture.GetDocumentSession();
        return (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();
    }

    private async Task<Shopping.Cart.Cart> CreateCartWithItems()
    {
        var cart = await CreateCart();
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-002", 1, 29.99m));

        using var session = _fixture.GetDocumentSession();
        return await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id) ?? cart;
    }
}
