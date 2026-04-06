using Marten;
using Messages.Contracts.Orders;
using Promotions.Coupon;
using Promotions.Promotion;
using Shouldly;

namespace Promotions.Api.IntegrationTests;

/// <summary>
/// Tests for coupon redemption workflow:
/// - RedeemCoupon happy path (DCB handler, M40.0)
/// - RedeemCoupon double-redemption (DCB Before() rejects)
/// - RedeemCoupon against non-active promotion (DCB Before() rejects)
/// - RedeemCoupon against promotion at usage cap (DCB Before() rejects)
/// - RedeemCoupon triggers choreography (PromotionRedemptionRecorded via CouponRedeemed)
/// - RevokeCoupon for issued/redeemed coupons
/// - RecordPromotionRedemption legacy command path (kept for backward compatibility)
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
            PromotionId: promotion.Id,
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
            PromotionId: promotion.Id,
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            RedeemedAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(firstRedemption);

        // Act: attempt second redemption — Before() returns ProblemDetails, pipeline stops
        var secondRedemption = new RedeemCoupon(
            CouponCode: "DOUBLE10",
            PromotionId: promotion.Id,
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            RedeemedAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(secondRedemption);

        // Assert: coupon should still be Redeemed (second redemption was rejected by Before())
        var coupon = await session.Events.AggregateStreamAsync<Promotions.Coupon.Coupon>(
            Promotions.Coupon.Coupon.StreamId("DOUBLE10"));
        coupon.ShouldNotBeNull();
        coupon.Status.ShouldBe(CouponStatus.Redeemed);
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
            PromotionId: promotion.Id,
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

        // Act: attempt second revocation — Before() returns ProblemDetails, pipeline stops
        await _fixture.ExecuteAndWaitAsync(new RevokeCoupon("DOUBLEREVOKE", "Second revoke"));

        // Assert: coupon should still be Revoked (second revocation was rejected by Before())
        var coupon = await session.Events.AggregateStreamAsync<Promotions.Coupon.Coupon>(
            Promotions.Coupon.Coupon.StreamId("DOUBLEREVOKE"));
        coupon.ShouldNotBeNull();
        coupon.Status.ShouldBe(CouponStatus.Revoked);
    }

    /// <summary>
    /// Legacy command path: RecordPromotionRedemption command still works.
    /// M40.0: This command is superseded by the DCB choreography pattern,
    /// but retained for backward compatibility.
    /// </summary>
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

    /// <summary>
    /// DCB boundary test: RedeemCoupon against a promotion that has reached its usage cap.
    /// The DCB Before() rejects the redemption because the boundary state shows
    /// CurrentRedemptionCount >= UsageLimit. Coupon stays in Issued status.
    /// </summary>
    [Fact]
    public async Task RedeemCoupon_WhenPromotionCapExceeded_Fails()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "DCB Cap Test",
            Description: "Test DCB cap enforcement",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 40m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(30),
            UsageLimit: 2); // Only 2 redemptions allowed

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));

        // Issue 3 coupons
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("DCBCAP-A", promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("DCBCAP-B", promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("DCBCAP-C", promotion.Id));

        // Redeem first two coupons (reaching the limit)
        await _fixture.ExecuteAndWaitAsync(new RedeemCoupon(
            "DCBCAP-A", promotion.Id, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new RedeemCoupon(
            "DCBCAP-B", promotion.Id, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));

        // Act: attempt third redemption — DCB Before() rejects (cap exceeded)
        await _fixture.ExecuteAndWaitAsync(new RedeemCoupon(
            "DCBCAP-C", promotion.Id, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));

        // Assert: third coupon should still be Issued (redemption rejected)
        var coupon = await session.Events.AggregateStreamAsync<Promotions.Coupon.Coupon>(
            Promotions.Coupon.Coupon.StreamId("DCBCAP-C"));
        coupon.ShouldNotBeNull();
        coupon.Status.ShouldBe(CouponStatus.Issued);
    }

    /// <summary>
    /// DCB boundary test: RedeemCoupon against a promotion that is not active (Draft).
    /// The DCB Before() rejects the redemption because the boundary state shows
    /// PromotionStatus != Active. Coupon stays in Issued status.
    /// </summary>
    [Fact]
    public async Task RedeemCoupon_WhenPromotionIsNotActive_Fails()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "DCB Draft Test",
            Description: "Test DCB rejects Draft promotion",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 10m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(7),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        // Activate to issue coupon, then test against a different draft promotion
        // Actually: issue coupon on Draft (allowed), don't activate, try to redeem
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("DCBDRAFT", promotion.Id));

        // Don't activate — promotion stays in Draft status

        // Act: attempt redemption — DCB Before() rejects (promotion not Active)
        await _fixture.ExecuteAndWaitAsync(new RedeemCoupon(
            "DCBDRAFT", promotion.Id, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));

        // Assert: coupon should still be Issued (redemption rejected by DCB)
        var coupon = await session.Events.AggregateStreamAsync<Promotions.Coupon.Coupon>(
            Promotions.Coupon.Coupon.StreamId("DCBDRAFT"));
        coupon.ShouldNotBeNull();
        coupon.Status.ShouldBe(CouponStatus.Issued);

        // Assert: promotion should still have 0 redemptions
        var updatedPromotion = await session.Events.AggregateStreamAsync<Promotions.Promotion.Promotion>(promotion.Id);
        updatedPromotion.ShouldNotBeNull();
        updatedPromotion.Status.ShouldBe(PromotionStatus.Draft);
        updatedPromotion.CurrentRedemptionCount.ShouldBe(0);
    }

    /// <summary>
    /// DCB choreography test: after RedeemCouponHandler (DCB) emits CouponRedeemed,
    /// RecordPromotionRedemptionHandler reacts via choreography and increments
    /// the Promotion's CurrentRedemptionCount.
    /// </summary>
    [Fact]
    public async Task RedeemCoupon_CausesPromotionRedemptionRecorded()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "DCB Choreography Test",
            Description: "Test choreography fires",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 20m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(30),
            UsageLimit: 10);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        await _fixture.ExecuteAndWaitAsync(new IssueCoupon("DCBCHOR", promotion.Id));

        // Act: redeem coupon via DCB handler
        await _fixture.ExecuteAndWaitAsync(new RedeemCoupon(
            "DCBCHOR", promotion.Id, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));

        // Assert: coupon is redeemed
        var coupon = await session.Events.AggregateStreamAsync<Promotions.Coupon.Coupon>(
            Promotions.Coupon.Coupon.StreamId("DCBCHOR"));
        coupon.ShouldNotBeNull();
        coupon.Status.ShouldBe(CouponStatus.Redeemed);

        // Assert: promotion redemption count incremented via choreography
        // RecordPromotionRedemptionHandler reacts to CouponRedeemed event
        var updatedPromotion = await session.Events.AggregateStreamAsync<Promotions.Promotion.Promotion>(promotion.Id);
        updatedPromotion.ShouldNotBeNull();
        updatedPromotion.CurrentRedemptionCount.ShouldBe(1);
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
