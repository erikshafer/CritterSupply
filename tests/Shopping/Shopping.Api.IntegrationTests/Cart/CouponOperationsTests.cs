using Marten;
using Shopping.Cart;

namespace Shopping.Api.IntegrationTests.Cart;

/// <summary>
/// Integration tests for cart coupon operations.
/// Tests coupon application, removal, validation, and discount calculation.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class CouponOperationsTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CouponOperationsTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.CleanAllDocumentsAsync();
        _fixture.StubPricingClient.Clear();
        _fixture.StubPromotionsClient.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ApplyCoupon_ValidCoupon_SuccessfullyAppliesCouponWithDiscount()
    {
        // Arrange
        var cart = await CreateCartWithItems();
        _fixture.StubPromotionsClient.SetValidCoupon("SAVE15", 15m, "15% Off Sale");

        // Act
        var (tracked, result) = await _fixture.TrackedHttpCall(x =>
        {
            x.Post.Json(new ApplyCouponToCart(cart.Id, "SAVE15"))
                .ToUrl($"/api/carts/{cart.Id}/apply-coupon");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Coupon applied event should be created
        tracked.Sent.SingleMessage<Messages.Contracts.Shopping.CouponApplied>()
            .CouponCode.ShouldBe("SAVE15");

        // Assert - Cart should have coupon and discount
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.AppliedCouponCode.ShouldBe("SAVE15");
        updatedCart.AppliedDiscount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ApplyCoupon_InvalidCoupon_Returns400WithErrorMessage()
    {
        // Arrange
        var cart = await CreateCartWithItems();
        _fixture.StubPromotionsClient.SetInvalidCoupon("EXPIRED20", "Coupon has expired");

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ApplyCouponToCart(cart.Id, "EXPIRED20"))
                .ToUrl($"/api/carts/{cart.Id}/apply-coupon");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("expired");
        });

        // Assert - Cart should not have coupon
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.AppliedCouponCode.ShouldBeNull();
        updatedCart.AppliedDiscount.ShouldBe(0m);
    }

    [Fact]
    public async Task ApplyCoupon_NonExistentCoupon_Returns400()
    {
        // Arrange
        var cart = await CreateCartWithItems();
        // StubPromotionsClient has no configured coupons - simulates "not found"

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ApplyCouponToCart(cart.Id, "NOTFOUND"))
                .ToUrl($"/api/carts/{cart.Id}/apply-coupon");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("not found");
        });
    }

    [Fact]
    public async Task ApplyCoupon_ToEmptyCart_Returns400()
    {
        // Arrange - Empty cart
        var cart = await CreateCart();

        _fixture.StubPromotionsClient.SetValidCoupon("SAVE20", 20m);

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ApplyCouponToCart(cart.Id, "SAVE20"))
                .ToUrl($"/api/carts/{cart.Id}/apply-coupon");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("empty cart");
        });
    }

    [Fact]
    public async Task ApplyCoupon_ToTerminalCart_Returns400()
    {
        // Arrange - Cleared cart (terminal state)
        var cart = await CreateCartWithItems();
        await _fixture.ExecuteAndWaitAsync(new ClearCart(cart.Id, "Test clear"));

        _fixture.StubPromotionsClient.SetValidCoupon("SAVE10", 10m);

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ApplyCouponToCart(cart.Id, "SAVE10"))
                .ToUrl($"/api/carts/{cart.Id}/apply-coupon");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Cannot modify");
        });
    }

    [Fact]
    public async Task RemoveCoupon_FromCartWithCoupon_SuccessfullyRemovesCoupon()
    {
        // Arrange - Cart with applied coupon
        var cart = await CreateCartWithItems();
        _fixture.StubPromotionsClient.SetValidCoupon("SAVE25", 25m);
        await _fixture.ExecuteAndWaitAsync(new ApplyCouponToCart(cart.Id, "SAVE25"));

        // Act
        var (tracked, result) = await _fixture.TrackedHttpCall(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}/apply-coupon");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Coupon removed event should be created
        tracked.Sent.SingleMessage<Messages.Contracts.Shopping.CouponRemoved>()
            .CouponCode.ShouldBe("SAVE25");

        // Assert - Cart should not have coupon
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.AppliedCouponCode.ShouldBeNull();
        updatedCart.AppliedDiscount.ShouldBe(0m);
    }

    [Fact]
    public async Task RemoveCoupon_FromCartWithoutCoupon_Returns400()
    {
        // Arrange - Cart without coupon
        var cart = await CreateCartWithItems();

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/carts/{cart.Id}/apply-coupon");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("No coupon applied");
        });
    }

    [Fact]
    public async Task GetCart_WithAppliedCoupon_ReturnsDiscountInformation()
    {
        // Arrange - Cart with items at 29.99 each (stub default)
        var cart = await CreateCartWithItems();
        _fixture.StubPromotionsClient.SetValidCoupon("SAVE10", 10m);
        await _fixture.ExecuteAndWaitAsync(new ApplyCouponToCart(cart.Id, "SAVE10"));

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/carts/{cart.Id}");
            x.StatusCodeShouldBe(200);
        });

        var cartResponse = await result.ReadAsJsonAsync<CartResponse>();

        // Assert - Response should include coupon and discount info
        cartResponse.AppliedCouponCode.ShouldBe("SAVE10");
        cartResponse.AppliedDiscount.ShouldBeGreaterThan(0);
        cartResponse.DiscountedTotal.ShouldBeLessThan(cartResponse.TotalAmount);
        cartResponse.DiscountedTotal.ShouldBe(cartResponse.TotalAmount - cartResponse.AppliedDiscount);
    }

    [Fact]
    public async Task GetCart_WithoutCoupon_ReturnsZeroDiscount()
    {
        // Arrange - Cart without coupon
        var cart = await CreateCartWithItems();

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/carts/{cart.Id}");
            x.StatusCodeShouldBe(200);
        });

        var cartResponse = await result.ReadAsJsonAsync<CartResponse>();

        // Assert - Response should show no coupon
        cartResponse.AppliedCouponCode.ShouldBeNull();
        cartResponse.AppliedDiscount.ShouldBe(0m);
        cartResponse.DiscountedTotal.ShouldBe(cartResponse.TotalAmount);
    }

    [Fact]
    public async Task ApplyCoupon_CalculatesDiscountCorrectly()
    {
        // Arrange - Cart with 3 items at 29.99 each = 89.97 total
        var cart = await CreateCart();
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 3));

        // 20% off = 17.99 discount (89.97 * 0.20 rounded)
        _fixture.StubPromotionsClient.SetValidCoupon("SAVE20", 20m);

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ApplyCouponToCart(cart.Id, "SAVE20"))
                .ToUrl($"/api/carts/{cart.Id}/apply-coupon");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Discount should be 20% of total
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        var expectedDiscount = Math.Round(89.97m * 0.20m, 2);
        updatedCart!.AppliedDiscount.ShouldBe(expectedDiscount);
    }

    [Fact]
    public async Task ApplyCoupon_ThenRemove_ThenReapply_Succeeds()
    {
        // Arrange
        var cart = await CreateCartWithItems();
        _fixture.StubPromotionsClient.SetValidCoupon("SAVE15", 15m);

        // Act - Apply coupon
        await _fixture.ExecuteAndWaitAsync(new ApplyCouponToCart(cart.Id, "SAVE15"));

        // Act - Remove coupon
        await _fixture.ExecuteAndWaitAsync(new RemoveCouponFromCart(cart.Id));

        // Act - Reapply same coupon
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ApplyCouponToCart(cart.Id, "SAVE15"))
                .ToUrl($"/api/carts/{cart.Id}/apply-coupon");
            x.StatusCodeShouldBe(204);
        });

        // Assert - Coupon should be reapplied
        using var session = _fixture.GetDocumentSession();
        var updatedCart = await session.Events.AggregateStreamAsync<Shopping.Cart.Cart>(cart.Id);
        updatedCart!.AppliedCouponCode.ShouldBe("SAVE15");
        updatedCart.AppliedDiscount.ShouldBeGreaterThan(0);
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
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-001", 2));
        await _fixture.ExecuteAndWaitAsync(new AddItemToCart(cart.Id, "SKU-002", 1));
        return cart;
    }

    // DTO matching GetCart response shape (updated with coupon fields)
    private record CartResponse(
        Guid CartId,
        Guid? CustomerId,
        string? SessionId,
        DateTimeOffset InitializedAt,
        List<CartItemDto> Items,
        string Status,
        decimal TotalAmount,
        string? AppliedCouponCode,
        decimal AppliedDiscount,
        decimal DiscountedTotal);

    private record CartItemDto(string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);
}
