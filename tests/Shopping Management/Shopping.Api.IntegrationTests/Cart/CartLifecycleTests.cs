using Marten;
using Shopping.Cart;

namespace Shopping.Api.IntegrationTests.Cart;

[Collection(nameof(IntegrationTestCollection))]
public class CartLifecycleTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CartLifecycleTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task InitializeCart_CreatesNewCartStream()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();
        var command = new InitializeCart(customerId, null);

        // Act
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert
        using var session = _fixture.GetDocumentSession();
        var carts = await session.Query<Shopping.Cart.Cart>().ToListAsync();
        
        carts.ShouldNotBeEmpty();
        var cart = carts.ShouldHaveSingleItem();
        cart.CustomerId.ShouldBe(customerId);
        cart.SessionId.ShouldBeNull();
        cart.Items.ShouldBeEmpty();
        cart.IsAbandoned.ShouldBeFalse();
        cart.IsCleared.ShouldBeFalse();
        cart.CheckoutInitiated.ShouldBeFalse();
    }

    [Fact]
    public async Task InitializeCart_ForAnonymousSession_CreatesCartWithSessionId()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString();
        var command = new InitializeCart(null, sessionId);

        // Act
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert
        using var session = _fixture.GetDocumentSession();
        var carts = await session.Query<Shopping.Cart.Cart>().ToListAsync();
        
        var cart = carts.ShouldHaveSingleItem();
        cart.CustomerId.ShouldBeNull();
        cart.SessionId.ShouldBe(sessionId);
    }

    [Fact]
    public async Task AddItemToCart_AddsNewItem()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();
        var initCommand = new InitializeCart(customerId, null);
        var tracked = await _fixture.ExecuteAndWaitAsync(initCommand);
        
        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();
        
        var addCommand = new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m);

        // Act
        await _fixture.ExecuteAndWaitAsync(addCommand);

        // Assert
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart.ShouldNotBeNull();
        updatedCart.Items.ShouldContainKey("SKU-001");
        updatedCart.Items["SKU-001"].Quantity.ShouldBe(2);
        updatedCart.Items["SKU-001"].UnitPrice.ShouldBe(19.99m);
    }

    [Fact]
    public async Task AddItemToCart_WhenItemExists_IncreasesQuantity()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();
        var initCommand = new InitializeCart(customerId, null);
        await _fixture.ExecuteAndWaitAsync(initCommand);
        
        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();
        
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));
        
        var addMoreCommand = new AddItemToCart(cart.Id, "SKU-001", 3, 19.99m);

        // Act
        await _fixture.ExecuteAndWaitAsync(addMoreCommand);

        // Assert
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart.ShouldNotBeNull();
        updatedCart.Items["SKU-001"].Quantity.ShouldBe(5); // 2 + 3
    }

    [Fact]
    public async Task RemoveItemFromCart_RemovesItem()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();
        var initCommand = new InitializeCart(customerId, null);
        await _fixture.ExecuteAndWaitAsync(initCommand);
        
        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();
        
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));
        
        var removeCommand = new RemoveItemFromCart(cart.Id, "SKU-001");

        // Act
        await _fixture.ExecuteAndWaitAsync(removeCommand);

        // Assert
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart.ShouldNotBeNull();
        updatedCart.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ChangeItemQuantity_UpdatesQuantity()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();
        var initCommand = new InitializeCart(customerId, null);
        await _fixture.ExecuteAndWaitAsync(initCommand);
        
        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();
        
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));
        
        var changeCommand = new ChangeItemQuantity(cart.Id, "SKU-001", 5);

        // Act
        await _fixture.ExecuteAndWaitAsync(changeCommand);

        // Assert
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart.ShouldNotBeNull();
        updatedCart.Items["SKU-001"].Quantity.ShouldBe(5);
    }

    [Fact]
    public async Task ClearCart_RemovesAllItems()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();
        var initCommand = new InitializeCart(customerId, null);
        await _fixture.ExecuteAndWaitAsync(initCommand);
        
        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();
        
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-002", 1, 9.99m));
        
        var clearCommand = new ClearCart(cart.Id, "User requested");

        // Act
        await _fixture.ExecuteAndWaitAsync(clearCommand);

        // Assert
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart.ShouldNotBeNull();
        updatedCart.Items.ShouldBeEmpty();
        updatedCart.IsCleared.ShouldBeTrue();
        updatedCart.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public async Task InitiateCheckout_TransitionsToCheckout()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();
        var initCommand = new InitializeCart(customerId, null);
        await _fixture.ExecuteAndWaitAsync(initCommand);
        
        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();
        
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));
        
        var initiateCommand = new InitiateCheckout(cart.Id);

        // Act
        await _fixture.ExecuteAndWaitAsync(initiateCommand);

        // Assert
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart.ShouldNotBeNull();
        updatedCart.CheckoutInitiated.ShouldBeTrue();
        updatedCart.IsTerminal.ShouldBeTrue();
        
        // Verify checkout stream was created
        var checkouts = await session.Query<Shopping.Checkout.Checkout>().ToListAsync();
        var checkout = checkouts.ShouldHaveSingleItem();
        checkout.CartId.ShouldBe(cart.Id);
        checkout.CustomerId.ShouldBe(customerId);
        checkout.Items.Count.ShouldBe(1);
        checkout.Items[0].Sku.ShouldBe("SKU-001");
    }

    [Fact]
    public async Task CannotModifyCart_AfterCheckoutInitiated()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();
        var initCommand = new InitializeCart(customerId, null);
        await _fixture.ExecuteAndWaitAsync(initCommand);
        
        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();
        
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));
        await _fixture.ExecuteAndWaitAsync(new InitiateCheckout(cart.Id));

        // Act & Assert - attempting to add item should fail validation in Before() method
        var (tracked, result) = await _fixture.TrackedHttpCall(x =>
        {
            x.Post.Json(new AddItemToCart(cart.Id, "SKU-002", 1, 9.99m))
                .ToUrl($"/api/carts/{cart.Id}/items");
            x.StatusCodeShouldBe(400);
        });
    }
}
