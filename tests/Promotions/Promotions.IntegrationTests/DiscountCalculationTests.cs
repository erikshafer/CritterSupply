using Marten;
using Promotions.Coupon;
using Promotions.Discount;
using Promotions.Promotion;
using Shouldly;

namespace Promotions.Api.IntegrationTests;

/// <summary>
/// Tests for M30.0 discount calculation endpoint (HTTP POST /api/promotions/discounts/calculate).
/// Phase 1: Percentage discounts only, stub floor price enforcement.
/// Phase 2+: Pricing BC integration for real floor price clamping.
/// </summary>
[Collection("Sequential")]
public sealed class DiscountCalculationTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public DiscountCalculationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CalculateDiscount_WithNoCoupons_ReturnsZeroDiscount()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var request = new CalculateDiscount(
            CartItems: new List<CartLineItem>
            {
                new("SKU-001", 2, 19.99m),
                new("SKU-002", 1, 49.99m)
            },
            CouponCodes: Array.Empty<string>());

        // Act
        var (tracked, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(request).ToUrl("/api/promotions/discounts/calculate");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var response = result.ReadAsJson<CalculateDiscountResponse>();
        response.ShouldNotBeNull();
        response.TotalDiscount.ShouldBe(0m);
        response.OriginalTotal.ShouldBe(89.97m); // (19.99 * 2) + 49.99
        response.DiscountedTotal.ShouldBe(89.97m);
        response.LineItemDiscounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task CalculateDiscount_WithValidCoupon_AppliesPercentageDiscount()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        // Create and activate promotion
        var createCmd = new CreatePromotion(
            Name: "20% Off Discount Test",
            Description: "Test percentage discount",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 20m,
            StartDate: DateTimeOffset.UtcNow.AddHours(-1),
            EndDate: DateTimeOffset.UtcNow.AddDays(30),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("DISCOUNT20", promotion.Id));

        var request = new CalculateDiscount(
            CartItems: new List<CartLineItem>
            {
                new("SKU-A", 1, 100.00m),
                new("SKU-B", 2, 50.00m)
            },
            CouponCodes: new[] { "DISCOUNT20" });

        // Act
        var (tracked, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(request).ToUrl("/api/promotions/discounts/calculate");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var response = result.ReadAsJson<CalculateDiscountResponse>();
        response.ShouldNotBeNull();
        response.OriginalTotal.ShouldBe(200.00m); // 100 + (50 * 2)
        response.TotalDiscount.ShouldBe(40.00m); // 20% of 200 = 40
        response.DiscountedTotal.ShouldBe(160.00m);
        response.LineItemDiscounts.Count.ShouldBe(2);

        // Verify line item discounts
        var skuADiscount = response.LineItemDiscounts.Single(d => d.Sku == "SKU-A");
        skuADiscount.OriginalPrice.ShouldBe(100.00m);
        skuADiscount.DiscountedPrice.ShouldBe(80.00m);
        skuADiscount.DiscountAmount.ShouldBe(20.00m); // 20% of 100 = 20
        skuADiscount.AppliedCouponCode.ShouldBe("DISCOUNT20");

        var skuBDiscount = response.LineItemDiscounts.Single(d => d.Sku == "SKU-B");
        skuBDiscount.OriginalPrice.ShouldBe(50.00m);
        skuBDiscount.DiscountedPrice.ShouldBe(40.00m);
        skuBDiscount.DiscountAmount.ShouldBe(20.00m); // (20% of 50) * 2 = 20
    }

    [Fact]
    public async Task CalculateDiscount_WithInvalidCoupon_ReturnsZeroDiscount()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var request = new CalculateDiscount(
            CartItems: new List<CartLineItem>
            {
                new("SKU-X", 1, 75.00m)
            },
            CouponCodes: new[] { "INVALID999" });

        // Act
        var (tracked, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(request).ToUrl("/api/promotions/discounts/calculate");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var response = result.ReadAsJson<CalculateDiscountResponse>();
        response.ShouldNotBeNull();
        response.TotalDiscount.ShouldBe(0m);
        response.OriginalTotal.ShouldBe(75.00m);
        response.DiscountedTotal.ShouldBe(75.00m);
        response.LineItemDiscounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task CalculateDiscount_WithRedeemedCoupon_ReturnsZeroDiscount()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        // Create promotion and issue coupon
        var createCmd = new CreatePromotion(
            Name: "Redeemed Coupon Test",
            Description: "Test",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 25m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(7),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("USED25", promotion.Id));

        // Redeem the coupon
        await _fixture.ExecuteAndWaitAsync(new RedeemCoupon(
            "USED25",
            promotion.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow));

        var request = new CalculateDiscount(
            CartItems: new List<CartLineItem>
            {
                new("SKU-Y", 1, 100.00m)
            },
            CouponCodes: new[] { "USED25" });

        // Act
        var (tracked, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(request).ToUrl("/api/promotions/discounts/calculate");
            s.StatusCodeShouldBeOk();
        });

        // Assert - Redeemed coupon should return zero discount
        var response = result.ReadAsJson<CalculateDiscountResponse>();
        response.ShouldNotBeNull();
        response.TotalDiscount.ShouldBe(0m);
        response.OriginalTotal.ShouldBe(100.00m);
        response.DiscountedTotal.ShouldBe(100.00m);
        response.LineItemDiscounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task CalculateDiscount_WithExpiredPromotion_ReturnsZeroDiscount()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        // Create promotion with end date in the past
        var createCmd = new CreatePromotion(
            Name: "Expired Promo Test",
            Description: "Test",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 30m,
            StartDate: DateTimeOffset.UtcNow.AddDays(-10),
            EndDate: DateTimeOffset.UtcNow.AddHours(-1), // Expired
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("EXPIRED30", promotion.Id));

        var request = new CalculateDiscount(
            CartItems: new List<CartLineItem>
            {
                new("SKU-Z", 1, 200.00m)
            },
            CouponCodes: new[] { "EXPIRED30" });

        // Act
        var (tracked, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(request).ToUrl("/api/promotions/discounts/calculate");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var response = result.ReadAsJson<CalculateDiscountResponse>();
        response.ShouldNotBeNull();
        response.TotalDiscount.ShouldBe(0m);
        response.OriginalTotal.ShouldBe(200.00m);
        response.DiscountedTotal.ShouldBe(200.00m);
        response.LineItemDiscounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task CalculateDiscount_WithNotYetStartedPromotion_ReturnsZeroDiscount()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        // Create promotion with start date in the future
        var createCmd = new CreatePromotion(
            Name: "Future Promo Test",
            Description: "Test",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 15m,
            StartDate: DateTimeOffset.UtcNow.AddDays(1), // Not started yet
            EndDate: DateTimeOffset.UtcNow.AddDays(30),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("FUTURE15", promotion.Id));

        var request = new CalculateDiscount(
            CartItems: new List<CartLineItem>
            {
                new("SKU-FUTURE", 1, 150.00m)
            },
            CouponCodes: new[] { "FUTURE15" });

        // Act
        var (tracked, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(request).ToUrl("/api/promotions/discounts/calculate");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var response = result.ReadAsJson<CalculateDiscountResponse>();
        response.ShouldNotBeNull();
        response.TotalDiscount.ShouldBe(0m);
        response.OriginalTotal.ShouldBe(150.00m);
        response.DiscountedTotal.ShouldBe(150.00m);
        response.LineItemDiscounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task CalculateDiscount_WithCaseInsensitiveCouponCode_AppliesDiscount()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Case Test Promo",
            Description: "Test case insensitivity",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 10m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(14),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("CASE10", promotion.Id));

        // Request with lowercase coupon code
        var request = new CalculateDiscount(
            CartItems: new List<CartLineItem>
            {
                new("SKU-CASE", 1, 50.00m)
            },
            CouponCodes: new[] { "case10" }); // lowercase

        // Act
        var (tracked, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(request).ToUrl("/api/promotions/discounts/calculate");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var response = result.ReadAsJson<CalculateDiscountResponse>();
        response.ShouldNotBeNull();
        response.TotalDiscount.ShouldBe(5.00m); // 10% of 50
        response.DiscountedTotal.ShouldBe(45.00m);
        response.LineItemDiscounts.Count.ShouldBe(1);
        response.LineItemDiscounts[0].AppliedCouponCode.ShouldBe("CASE10"); // Normalized to uppercase
    }

    [Fact]
    public async Task CalculateDiscount_WithMultipleItems_CalculatesCorrectTotals()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Multi-Item Test",
            Description: "Test",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 15m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(30),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("MULTI15", promotion.Id));

        var request = new CalculateDiscount(
            CartItems: new List<CartLineItem>
            {
                new("SKU-001", 3, 29.99m), // 89.97
                new("SKU-002", 1, 10.01m), // 10.01
                new("SKU-003", 2, 45.50m)  // 91.00
            },
            CouponCodes: new[] { "MULTI15" });

        // Act
        var (tracked, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(request).ToUrl("/api/promotions/discounts/calculate");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var response = result.ReadAsJson<CalculateDiscountResponse>();
        response.ShouldNotBeNull();
        response.OriginalTotal.ShouldBe(190.98m); // 89.97 + 10.01 + 91.00

        // Calculate expected discount: 15% of each item's unit price * quantity
        // SKU-001: 15% of 29.99 = 4.4985, rounded to 4.50 * 3 = 13.50
        // SKU-002: 15% of 10.01 = 1.5015, rounded to 1.50 * 1 = 1.50
        // SKU-003: 15% of 45.50 = 6.825, banker's rounding to 6.82 * 2 = 13.64
        // Total discount: 28.64 (not 28.66 due to banker's rounding)
        response.TotalDiscount.ShouldBe(28.64m);
        response.DiscountedTotal.ShouldBe(162.34m);
        response.LineItemDiscounts.Count.ShouldBe(3);
    }
}
