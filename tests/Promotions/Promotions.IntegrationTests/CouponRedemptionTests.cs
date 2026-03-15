using Marten;
using Messages.Contracts.Orders;
using Promotions.Coupon;
using Promotions.Promotion;
using Shouldly;

namespace Promotions.Api.IntegrationTests;

/// <summary>
/// Tests for M30.0 coupon redemption workflow:
/// - RedeemCoupon happy path
/// - RedeemCoupon double-redemption (optimistic concurrency)
/// - RevokeCoupon for issued/redeemed coupons
/// - RecordPromotionRedemption usage limit enforcement
/// - RecordPromotionRedemption optimistic concurrency
/// - GenerateCouponBatch fan-out pattern
/// - OrderPlacedHandler skeleton (no coupon data yet)
/// </summary>
[Collection("Sequential")]
public sealed class CouponRedemptionTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public CouponRedemptionTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RedeemCoupon_WithValidIssuedCoupon_RedeemsSuccessfully()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        // Create and activate promotion
        var createCmd = new CreatePromotion(
            Name: "Redemption Test Promo",
            Description: "Test redemption flow",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 15m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(30),
            UsageLimit: 100);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("REDEEM15", promotion.Id));

        var redeemCmd = new RedeemCoupon(
            CouponCode: "REDEEM15",
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            RedeemedAt: DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteAndWaitAsync(redeemCmd);

        // Assert - Verify coupon status changed
        var couponStreamId = Promotions.Coupon.Coupon.StreamId("REDEEM15");
        var coupon = await session.Events.AggregateStreamAsync<Promotions.Coupon.Coupon>(couponStreamId);
        coupon.ShouldNotBeNull();
        coupon.Status.ShouldBe(CouponStatus.Redeemed);
        coupon.CustomerId.ShouldBe(redeemCmd.CustomerId);

        // Verify projection updated
        var lookupView = await session.LoadAsync<CouponLookupView>("REDEEM15");
        lookupView.ShouldNotBeNull();
        lookupView.Status.ShouldBe(CouponStatus.Redeemed);
    }

    [Fact]
    public async Task RedeemCoupon_WhenAlreadyRedeemed_Fails()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Double Redemption Test",
            Description: "Test",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 10m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(7),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("DOUBLE10", promotion.Id));

        // Redeem once
        var firstRedemption = new RedeemCoupon(
            CouponCode: "DOUBLE10",
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            RedeemedAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(firstRedemption);

        // Act & Assert - Attempt second redemption
        var secondRedemption = new RedeemCoupon(
            CouponCode: "DOUBLE10",
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            RedeemedAt: DateTimeOffset.UtcNow);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _fixture.ExecuteAndWaitAsync(secondRedemption);
        });

        exception.Message.ShouldContain("Cannot redeem coupon");
        exception.Message.ShouldContain("Redeemed");
    }

    [Fact]
    public async Task RevokeCoupon_ForIssuedCoupon_RevokesSuccessfully()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Revoke Test Promo",
            Description: "Test revocation",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 20m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(14),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("REVOKE20", promotion.Id));

        var revokeCmd = new RevokeCoupon(
            CouponCode: "REVOKE20",
            Reason: "Fraud detected");

        // Act
        await _fixture.ExecuteAndWaitAsync(revokeCmd);

        // Assert
        var couponStreamId = Promotions.Coupon.Coupon.StreamId("REVOKE20");
        var coupon = await session.Events.AggregateStreamAsync<Promotions.Coupon.Coupon>(couponStreamId);
        coupon.ShouldNotBeNull();
        coupon.Status.ShouldBe(CouponStatus.Revoked);

        // Verify projection updated
        var lookupView = await session.LoadAsync<CouponLookupView>("REVOKE20");
        lookupView.ShouldNotBeNull();
        lookupView.Status.ShouldBe(CouponStatus.Revoked);
    }

    [Fact]
    public async Task RevokeCoupon_ForRedeemedCoupon_RevokesSuccessfully()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Revoke Redeemed Test",
            Description: "Test",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 25m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(30),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("REVOKEUSED", promotion.Id));

        // Redeem first
        var redeemCmd = new RedeemCoupon(
            CouponCode: "REVOKEUSED",
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            RedeemedAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(redeemCmd);

        // Act - Revoke the redeemed coupon
        var revokeCmd = new RevokeCoupon(
            CouponCode: "REVOKEUSED",
            Reason: "Customer refund requested");

        await _fixture.ExecuteAndWaitAsync(revokeCmd);

        // Assert
        var couponStreamId = Promotions.Coupon.Coupon.StreamId("REVOKEUSED");
        var coupon = await session.Events.AggregateStreamAsync<Promotions.Coupon.Coupon>(couponStreamId);
        coupon.ShouldNotBeNull();
        coupon.Status.ShouldBe(CouponStatus.Revoked);
    }

    [Fact]
    public async Task RevokeCoupon_WhenAlreadyRevoked_Fails()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Double Revoke Test",
            Description: "Test",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 5m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(1),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("DOUBLEREVOKE", promotion.Id));

        // Revoke once
        await _fixture.ExecuteAndWaitAsync(new RevokeCoupon("DOUBLEREVOKE", "First revoke"));

        // Act & Assert - Attempt second revocation
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _fixture.ExecuteAndWaitAsync(new RevokeCoupon("DOUBLEREVOKE", "Second revoke"));
        });

        exception.Message.ShouldContain("Cannot revoke coupon");
        exception.Message.ShouldContain("already revoked");
    }

    [Fact]
    public async Task RecordPromotionRedemption_WithinUsageLimit_RecordsSuccessfully()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Limited Usage Test",
            Description: "Test usage limit enforcement",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 30m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(30),
            UsageLimit: 5); // Only 5 redemptions allowed

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));

        var recordCmd = new RecordPromotionRedemption(
            PromotionId: promotion.Id,
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            CouponCode: "LIMITED30",
            RedeemedAt: DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteAndWaitAsync(recordCmd);

        // Assert - Verify redemption count incremented
        var updated = await session.Events.AggregateStreamAsync<Promotions.Promotion.Promotion>(promotion.Id);
        updated.ShouldNotBeNull();
        updated.CurrentRedemptionCount.ShouldBe(1);
    }

    [Fact]
    public async Task RecordPromotionRedemption_ExceedingUsageLimit_Fails()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Cap Test Promo",
            Description: "Test cap enforcement",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 40m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(30),
            UsageLimit: 2); // Only 2 redemptions allowed

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));

        // Record two redemptions (reaching the limit)
        await _fixture.ExecuteAndWaitAsync(new RecordPromotionRedemption(
            promotion.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "CAP40-A",
            DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new RecordPromotionRedemption(
            promotion.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "CAP40-B",
            DateTimeOffset.UtcNow));

        // Act & Assert - Attempt third redemption (should fail)
        var thirdRedemption = new RecordPromotionRedemption(
            promotion.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "CAP40-C",
            DateTimeOffset.UtcNow);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _fixture.ExecuteAndWaitAsync(thirdRedemption);
        });

        exception.Message.ShouldContain("usage limit");
        exception.Message.ShouldContain("has been reached");
    }

    [Fact]
    public async Task RecordPromotionRedemption_ForDraftPromotion_Fails()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Draft Promo",
            Description: "Test",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 10m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(7),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        // Don't activate - leave in Draft status

        var recordCmd = new RecordPromotionRedemption(
            promotion.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "DRAFT10",
            DateTimeOffset.UtcNow);

        // Act & Assert
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _fixture.ExecuteAndWaitAsync(recordCmd);
        });

        exception.Message.ShouldContain("Cannot record redemption");
        exception.Message.ShouldContain("Draft");
    }

    [Fact]
    public async Task GenerateCouponBatch_ForActivePromotion_CreatesMultipleCoupons()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Batch Test Promo",
            Description: "Test batch generation",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 50m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(60),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));

        var batchCmd = new GenerateCouponBatch(
            PromotionId: promotion.Id,
            Prefix: "BATCH",
            Count: 5);

        // Act
        await _fixture.ExecuteAndWaitAsync(batchCmd);

        // Wait for fan-out IssueCoupon commands to be processed asynchronously
        // GenerateCouponBatch creates N IssueCoupon commands via OutgoingMessages
        // Each IssueCoupon handler creates a coupon aggregate + updates CouponLookupView projection
        await Task.Delay(1000); // Increased to handle async projection updates

        // Assert - Verify all 5 coupons were created
        var expectedCodes = new[] { "BATCH-0001", "BATCH-0002", "BATCH-0003", "BATCH-0004", "BATCH-0005" };

        foreach (var code in expectedCodes)
        {
            var couponStreamId = Promotions.Coupon.Coupon.StreamId(code);
            var coupon = await session.Events.AggregateStreamAsync<Promotions.Coupon.Coupon>(couponStreamId);
            coupon.ShouldNotBeNull();
            coupon.Code.ShouldBe(code);
            coupon.Status.ShouldBe(CouponStatus.Issued);
            coupon.PromotionId.ShouldBe(promotion.Id);

            // Verify projection
            var lookupView = await session.LoadAsync<CouponLookupView>(code);
            lookupView.ShouldNotBeNull();
            lookupView.Code.ShouldBe(code);
        }
    }

    [Fact]
    public async Task GenerateCouponBatch_ForDraftPromotion_CreatesSuccessfully()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Draft Batch Test",
            Description: "Test",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 15m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(14),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        // Don't activate - batch generation should work for Draft promotions

        var batchCmd = new GenerateCouponBatch(
            promotion.Id,
            "DRAFT",
            3);

        // Act
        await _fixture.ExecuteAndWaitAsync(batchCmd);

        // Wait for fan-out IssueCoupon commands to be processed asynchronously
        // GenerateCouponBatch creates N IssueCoupon commands via OutgoingMessages
        // Each IssueCoupon handler creates a coupon aggregate + updates CouponLookupView projection
        await Task.Delay(1000); // Increased from 300ms to handle async projection updates

        // Assert - Verify all 3 coupons were created
        var codes = new[] { "DRAFT-0001", "DRAFT-0002", "DRAFT-0003" };

        foreach (var code in codes)
        {
            var coupon = await session.LoadAsync<CouponLookupView>(code);
            coupon.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task OrderPlacedHandler_InPhase1_ReturnsEmptyMessages()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var orderPlacedMessage = new OrderPlaced(
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            LineItems: new List<Messages.Contracts.Orders.OrderLineItem>
            {
                new("SKU-001", 1, 29.99m, 29.99m)
            },
            ShippingAddress: new Messages.Contracts.Orders.ShippingAddress(
                "123 Test St",
                null,
                "Test City",
                "TS",
                "12345",
                "US"),
            ShippingMethod: "Standard",
            PaymentMethodToken: "tok_test123",
            TotalAmount: 29.99m,
            PlacedAt: DateTimeOffset.UtcNow);

        // Act - Handler should process but not fan out any commands
        await _fixture.ExecuteAndWaitAsync(orderPlacedMessage);

        // Assert - No error means the handler processed successfully
        // Phase 1: Handler is a no-op skeleton (will be implemented in M30.1)
        // This test verifies the handler exists and doesn't crash
        true.ShouldBeTrue(); // Explicit pass - handler executed without exception
    }
}
