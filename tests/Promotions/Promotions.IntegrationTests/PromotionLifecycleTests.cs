using Marten;
using Promotions.Coupon;
using Promotions.Promotion;
using Shouldly;

namespace Promotions.Api.IntegrationTests;

[Collection("Sequential")]
public class PromotionLifecycleTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public PromotionLifecycleTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreatePromotion_WithValidData_CreatesPromotionInDraftStatus()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var command = new CreatePromotion(
            Name: "Summer Sale 2026",
            Description: "Get 20% off summer supplies",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 20m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(30),
            UsageLimit: 100);

        // Act
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert - Query promotion directly from event stream
        using var session = _fixture.GetDocumentSession();
        var promotions = await session.Query<Promotions.Promotion.Promotion>().ToListAsync();

        var promotion = promotions.ShouldHaveSingleItem();
        promotion.Status.ShouldBe(PromotionStatus.Draft);
        promotion.Name.ShouldBe("Summer Sale 2026");
        promotion.Description.ShouldBe("Get 20% off summer supplies");
        promotion.DiscountType.ShouldBe(DiscountType.PercentageOff);
        promotion.DiscountValue.ShouldBe(20m);
        promotion.UsageLimit.ShouldBe(100);
    }

    [Fact]
    public async Task ActivatePromotion_FromDraft_ActivatesSuccessfully()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Flash Sale",
            Description: "10% off",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 10m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(7),
            UsageLimit: 100);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        // Get the created promotion
        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        var activateCmd = new ActivatePromotion(promotion.Id);

        // Act
        await _fixture.ExecuteAndWaitAsync(activateCmd);

        // Assert - Reload promotion
        var updated = await session.Events.AggregateStreamAsync<Promotions.Promotion.Promotion>(promotion.Id);
        updated.ShouldNotBeNull();
        updated.Status.ShouldBe(PromotionStatus.Active);
        updated.ActivatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ActivatePromotion_WhenAlreadyActive_Fails()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Test Promotion",
            Description: "Test",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 5m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(1),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        // Activate once
        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));

        // Act & Assert - attempt to activate again
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));
        });

        exception.Message.ShouldContain("Cannot activate promotion");
        exception.Message.ShouldContain("Active");
    }

    [Fact]
    public async Task IssueCoupon_ForActivePromotion_CreatesCoupon()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        // Create and activate promotion
        var createCmd = new CreatePromotion(
            Name: "Coupon Test Promotion",
            Description: "Test coupon issuance",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 20m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(14),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        await _fixture.ExecuteAndWaitAsync(new ActivatePromotion(promotion.Id));

        var issueCouponCmd = new IssueCoupon("SAVE20", promotion.Id);

        // Act
        await _fixture.ExecuteAndWaitAsync(issueCouponCmd);

        // Assert - Verify aggregate state
        var couponStreamId = Promotions.Coupon.Coupon.StreamId("SAVE20");
        var coupon = await session.Events.AggregateStreamAsync<Promotions.Coupon.Coupon>(couponStreamId);
        coupon.ShouldNotBeNull();
        coupon.Code.ShouldBe("SAVE20");
        coupon.Status.ShouldBe(CouponStatus.Issued);

        // Verify projection
        var lookupView = await session.LoadAsync<CouponLookupView>("SAVE20");
        lookupView.ShouldNotBeNull();
        lookupView.Code.ShouldBe("SAVE20");
        lookupView.Status.ShouldBe(CouponStatus.Issued);
        lookupView.PromotionId.ShouldBe(promotion.Id);
    }

    [Fact]
    public async Task IssueCoupon_ForInactivePromotion_Fails()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var createCmd = new CreatePromotion(
            Name: "Draft Promotion",
            Description: "Not activated",
            DiscountType: DiscountType.PercentageOff,
            DiscountValue: 10m,
            StartDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(7),
            UsageLimit: null);

        await _fixture.ExecuteAndWaitAsync(createCmd);

        using var session = _fixture.GetDocumentSession();
        var promotion = (await session.Query<Promotions.Promotion.Promotion>().ToListAsync()).Single();

        var issueCouponCmd = new IssueCoupon("INVALID", promotion.Id);

        // Act & Assert
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _fixture.ExecuteAndWaitAsync(issueCouponCmd);
        });

        exception.Message.ShouldContain("Cannot issue coupon for promotion");
        exception.Message.ShouldContain("Draft");
    }
}
