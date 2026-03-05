using Marten;
using Shopping.Cart;

namespace Shopping.Api.IntegrationTests.Cart;

/// <summary>
/// Alba-based validation tests for ChangeItemQuantity command.
/// Tests validation rules and edge cases.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class ChangeItemQuantityValidationTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ChangeItemQuantityValidationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PUT_ChangeQuantity_WithZeroQuantity_Returns400()
    {
        // Arrange
        var cart = await CreateCartWithItem("SKU-001", 5);

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Put.Json(new ChangeItemQuantity(cart.Id, "SKU-001", 0))
                .ToUrl($"/api/carts/{cart.Id}/items/SKU-001/quantity");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("New Quantity");
        });
    }

    [Fact]
    public async Task PUT_ChangeQuantity_WithNegativeQuantity_Returns400()
    {
        // Arrange
        var cart = await CreateCartWithItem("SKU-001", 5);

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Put.Json(new ChangeItemQuantity(cart.Id, "SKU-001", -3))
                .ToUrl($"/api/carts/{cart.Id}/items/SKU-001/quantity");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("New Quantity");
        });
    }

    [Fact]
    public async Task PUT_ChangeQuantity_ForNonExistentItem_Returns404()
    {
        // Arrange
        var cart = await CreateCartWithItem("SKU-001", 5);

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Put.Json(new ChangeItemQuantity(cart.Id, "NON-EXISTENT", 10))
                .ToUrl($"/api/carts/{cart.Id}/items/NON-EXISTENT/quantity");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task PUT_ChangeQuantity_WithEmptySku_Returns400()
    {
        // Arrange - Empty SKU is caught by FluentValidation before route parsing
        var cart = await CreateCart();

        // Act & Assert - Validation should catch empty SKU
        await _fixture.Host.Scenario(x =>
        {
            x.Put.Json(new ChangeItemQuantity(cart.Id, "", 10))
                .ToUrl($"/api/carts/{cart.Id}/items/ /quantity");  // Space as placeholder for empty SKU
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Sku");
        });
    }

    [Fact]
    public async Task PUT_ChangeQuantity_ToIncrease_Succeeds()
    {
        // Arrange
        var cart = await CreateCartWithItem("SKU-001", 2);

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Put.Json(new ChangeItemQuantity(cart.Id, "SKU-001", 10))
                .ToUrl($"/api/carts/{cart.Id}/items/SKU-001/quantity");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Verify quantity changed
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.Items["SKU-001"].Quantity.ShouldBe(10);
    }

    [Fact]
    public async Task PUT_ChangeQuantity_ToDecrease_Succeeds()
    {
        // Arrange
        var cart = await CreateCartWithItem("SKU-001", 10);

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Put.Json(new ChangeItemQuantity(cart.Id, "SKU-001", 3))
                .ToUrl($"/api/carts/{cart.Id}/items/SKU-001/quantity");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Verify quantity changed
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.Items["SKU-001"].Quantity.ShouldBe(3);
    }

    [Fact]
    public async Task PUT_ChangeQuantity_OnTerminalCart_Returns400()
    {
        // Arrange - Create cart with item, then clear it
        var cart = await CreateCartWithItem("SKU-001", 5);
        await _fixture.ExecuteAndWaitAsync(new ClearCart(cart.Id, "Testing"));

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Put.Json(new ChangeItemQuantity(cart.Id, "SKU-001", 10))
                .ToUrl($"/api/carts/{cart.Id}/items/SKU-001/quantity");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Cannot modify a cart");
        });
    }

    private async Task<Shopping.Cart.Cart> CreateCart()
    {
        var customerId = Guid.CreateVersion7();
        await _fixture.ExecuteAndWaitAsync(new InitializeCart(customerId, null));

        using var session = _fixture.GetDocumentSession();
        return (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();
    }

    private async Task<Shopping.Cart.Cart> CreateCartWithItem(string sku, int quantity)
    {
        var cart = await CreateCart();
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, sku, quantity, 19.99m));

        using var session = _fixture.GetDocumentSession();
        return await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id) ?? cart;
    }
}
