using Marten;
using Shopping.Cart;

namespace Shopping.Api.IntegrationTests.Cart;

/// <summary>
/// Alba-based HTTP integration tests for InitiateCheckout endpoint.
/// Verifies HTTP contract, response format, and Cart → Checkout transition.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class InitiateCheckoutHttpTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public InitiateCheckoutHttpTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task POST_InitiateCheckout_ReturnsCreationResponseWithCheckoutId()
    {
        // Arrange - Create cart with items
        var customerId = Guid.CreateVersion7();
        await _fixture.ExecuteAndWaitAsync(new InitializeCart(customerId, null));

        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));

        // Act - Initiate checkout via HTTP
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new InitiateCheckout(cart.Id))
                .ToUrl($"/api/carts/{cart.Id}/checkout");
            x.StatusCodeShouldBe(201);
            x.Header("Location").ShouldHaveValues();
        });

        // Assert - Verify response structure
        var response = result.ReadAsJson<CreationResponseDto>();
        response.ShouldNotBeNull();
        response.Value.ShouldNotBe(Guid.Empty);
        response.Url.ShouldStartWith("/api/checkouts/");

        // Verify cart transitioned to terminal state
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart.ShouldNotBeNull();
        updatedCart.Status.ShouldBe(CartStatus.CheckedOut);
        updatedCart.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public async Task POST_InitiateCheckout_WithEmptyCart_Returns400()
    {
        // Arrange - Create empty cart
        var customerId = Guid.CreateVersion7();
        await _fixture.ExecuteAndWaitAsync(new InitializeCart(customerId, null));

        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();

        // Act & Assert - Initiate checkout should fail
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new InitiateCheckout(cart.Id))
                .ToUrl($"/api/carts/{cart.Id}/checkout");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Cannot checkout an empty cart");
        });
    }

    [Fact]
    public async Task POST_InitiateCheckout_WithNonExistentCart_Returns404()
    {
        // Arrange - Non-existent cart ID
        var nonExistentCartId = Guid.CreateVersion7();

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new InitiateCheckout(nonExistentCartId))
                .ToUrl($"/api/carts/{nonExistentCartId}/checkout");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task POST_InitiateCheckout_WithAlreadyCheckedOutCart_Returns400()
    {
        // Arrange - Create cart with items and checkout once
        var customerId = Guid.CreateVersion7();
        await _fixture.ExecuteAndWaitAsync(new InitializeCart(customerId, null));

        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));
        await _fixture.ExecuteAndWaitAsync(new InitiateCheckout(cart.Id));

        // Act & Assert - Second checkout attempt should fail
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new InitiateCheckout(cart.Id))
                .ToUrl($"/api/carts/{cart.Id}/checkout");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Cannot initiate checkout for a cart that has been abandoned, cleared, or already checked out");
        });
    }

    [Fact]
    public async Task POST_InitiateCheckout_PublishesIntegrationMessage()
    {
        // Arrange - Create cart with items
        var customerId = Guid.CreateVersion7();
        await _fixture.ExecuteAndWaitAsync(new InitializeCart(customerId, null));

        using var session = _fixture.GetDocumentSession();
        var cart = (await session.Query<Shopping.Cart.Cart>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m));
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-002", 1, 9.99m));

        // Act - Initiate checkout
        var (tracked, result) = await _fixture.TrackedHttpCall(x =>
        {
            x.Post.Json(new InitiateCheckout(cart.Id))
                .ToUrl($"/api/carts/{cart.Id}/checkout");
            x.StatusCodeShouldBe(201);
        });

        // Assert - Verify Shopping.CheckoutInitiated was published
        // Note: TrackedSession captures outgoing messages before RabbitMQ transport
        var checkoutInitiatedMsg = tracked.Sent.SingleMessage<Messages.Contracts.Shopping.CheckoutInitiated>();

        checkoutInitiatedMsg.CartId.ShouldBe(cart.Id);
        checkoutInitiatedMsg.CustomerId.ShouldBe(customerId);
        checkoutInitiatedMsg.Items.Count.ShouldBe(2);
        checkoutInitiatedMsg.Items[0].Sku.ShouldBe("SKU-001");
        checkoutInitiatedMsg.Items[0].Quantity.ShouldBe(2);
        checkoutInitiatedMsg.Items[1].Sku.ShouldBe("SKU-002");
        checkoutInitiatedMsg.Items[1].Quantity.ShouldBe(1);
    }

    // DTO for deserializing CreationResponse<Guid>
    private record CreationResponseDto(Guid Value, string Url);
}
