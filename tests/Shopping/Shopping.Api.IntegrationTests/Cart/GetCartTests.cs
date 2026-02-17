using Marten;
using Shopping.Cart;
using System.Net;
using System.Net.Http.Json;
using Shopping.Api.Cart;

namespace Shopping.Api.IntegrationTests.Cart;

[Collection(nameof(IntegrationTestCollection))]
public class GetCartTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public GetCartTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetCart_ReturnsCartWithItems()
    {
        // Arrange - Create a cart with items
        var customerId = Guid.CreateVersion7();
        var initCommand = new InitializeCart(customerId, null);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-002", 1, 29.99m));

        // Act
        var response = await _fixture.Host.Scenario(cfg =>
        {
            cfg.Get.Url($"/api/carts/{cart.Id}");
            cfg.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var result = await response.ReadAsJsonAsync<CartResponse>();

        // Assert
        result.ShouldNotBeNull();
        result.CartId.ShouldBe(cart.Id);
        result.CustomerId.ShouldBe(customerId);
        result.SessionId.ShouldBeNull();
        result.Status.ShouldBe("Active");
        result.Items.Count.ShouldBe(2);

        var item1 = result.Items.Single(i => i.Sku == "SKU-001");
        item1.Quantity.ShouldBe(2);
        item1.UnitPrice.ShouldBe(19.99m);
        item1.LineTotal.ShouldBe(39.98m);

        var item2 = result.Items.Single(i => i.Sku == "SKU-002");
        item2.Quantity.ShouldBe(1);
        item2.UnitPrice.ShouldBe(29.99m);
        item2.LineTotal.ShouldBe(29.99m);

        result.TotalAmount.ShouldBe(69.97m);
    }

    [Fact]
    public async Task GetCart_EmptyCart_ReturnsEmptyItemsList()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();
        var initCommand = new InitializeCart(customerId, null);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();

        // Act
        var response = await _fixture.Host.Scenario(cfg =>
        {
            cfg.Get.Url($"/api/carts/{cart.Id}");
            cfg.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var result = await response.ReadAsJsonAsync<CartResponse>();

        // Assert
        result.ShouldNotBeNull();
        result.Items.ShouldBeEmpty();
        result.TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task GetCart_NonExistentCart_Returns404()
    {
        // Arrange
        var nonExistentCartId = Guid.CreateVersion7();

        // Act & Assert
        await _fixture.Host.Scenario(cfg =>
        {
            cfg.Get.Url($"/api/carts/{nonExistentCartId}");
            cfg.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task GetCart_AnonymousCart_ReturnsSessionId()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString();
        var initCommand = new InitializeCart(null, sessionId);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();

        // Act
        var response = await _fixture.Host.Scenario(cfg =>
        {
            cfg.Get.Url($"/api/carts/{cart.Id}");
            cfg.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var result = await response.ReadAsJsonAsync<CartResponse>();

        // Assert
        result.ShouldNotBeNull();
        result.CustomerId.ShouldBeNull();
        result.SessionId.ShouldBe(sessionId);
    }
}
