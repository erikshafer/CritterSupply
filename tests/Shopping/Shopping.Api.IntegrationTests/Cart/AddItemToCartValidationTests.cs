using Marten;
using Shopping.Cart;

namespace Shopping.Api.IntegrationTests.Cart;

/// <summary>
/// Alba-based validation tests for AddItemToCart command.
/// Tests "off the happy path" scenarios and FluentValidation rules.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class AddItemToCartValidationTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public AddItemToCartValidationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task POST_AddItem_WithZeroQuantity_Returns400()
    {
        // Arrange
        var cart = await CreateCart();

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(cart.Id, "SKU-001", 0))
                .ToUrl($"/api/carts/{cart.Id}/items");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Quantity");
        });
    }

    [Fact]
    public async Task POST_AddItem_WithNegativeQuantity_Returns400()
    {
        // Arrange
        var cart = await CreateCart();

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(cart.Id, "SKU-001", -5))
                .ToUrl($"/api/carts/{cart.Id}/items");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Quantity");
        });
    }

    [Fact]
    public async Task POST_AddItem_WithEmptySku_Returns400()
    {
        // Arrange
        var cart = await CreateCart();

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(cart.Id, "", 2))
                .ToUrl($"/api/carts/{cart.Id}/items");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Sku");
        });
    }

    [Fact]
    public async Task POST_AddItem_WithSkuTooLong_Returns400()
    {
        // Arrange
        var cart = await CreateCart();
        var longSku = new string('A', 51); // MaxLength is 50

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(cart.Id, longSku, 2))
                .ToUrl($"/api/carts/{cart.Id}/items");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Sku");
        });
    }

    [Fact]
    public async Task POST_AddItem_WithEmptyCartId_Returns400()
    {
        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(Guid.Empty, "SKU-001", 2))
                .ToUrl($"/api/carts/{Guid.Empty}/items");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Cart Id");
        });
    }

    [Fact]
    public async Task POST_AddItem_ToNonExistentCart_Returns404()
    {
        // Arrange
        var nonExistentCartId = Guid.CreateVersion7();

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(nonExistentCartId, "SKU-001", 2))
                .ToUrl($"/api/carts/{nonExistentCartId}/items");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task POST_AddItem_ToClearedCart_Returns400()
    {
        // Arrange - Create cart, add item, then clear it
        var cart = await CreateCart();
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 1));
        await _fixture.ExecuteAndWaitAsync(new ClearCart(cart.Id, "Testing"));

        // Act & Assert - Attempt to add item to cleared (terminal) cart
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(cart.Id, "SKU-002", 1))
                .ToUrl($"/api/carts/{cart.Id}/items");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Cannot modify a cart");
        });
    }

    [Fact]
    public async Task POST_AddItem_ToCheckedOutCart_Returns400()
    {
        // Arrange - Create cart, add item, then initiate checkout
        var cart = await CreateCart();
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 1));
        await _fixture.ExecuteAndWaitAsync(new InitiateCheckout(cart.Id));

        // Act & Assert - Attempt to add item to checked-out (terminal) cart
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AddItemToCart(cart.Id, "SKU-002", 1))
                .ToUrl($"/api/carts/{cart.Id}/items");
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
}
