using Pricing.Products;

namespace Pricing.UnitTests;

/// <summary>
/// Tests for ProductPrice aggregate Apply methods.
/// Each Apply method is a pure function: (CurrentState, Event) → NewState.
/// </summary>
public sealed class ProductPriceApplyTests
{
    private readonly DateTimeOffset _testTime = new(2026, 3, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _testUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_WithValidSku_ReturnsUnpricedAggregate()
    {
        // Arrange
        var sku = "DOG-FOOD-5LB";
        var registeredAt = _testTime;

        // Act
        var aggregate = ProductPrice.Create(sku, registeredAt);

        // Assert
        aggregate.Id.ShouldBe(ProductPrice.StreamId(sku));
        aggregate.Sku.ShouldBe("DOG-FOOD-5LB");
        aggregate.Status.ShouldBe(PriceStatus.Unpriced);
        aggregate.BasePrice.ShouldBeNull();
        aggregate.FloorPrice.ShouldBeNull();
        aggregate.CeilingPrice.ShouldBeNull();
        aggregate.PreviousBasePrice.ShouldBeNull();
        aggregate.PreviousPriceSetAt.ShouldBeNull();
        aggregate.PendingSchedule.ShouldBeNull();
        aggregate.RegisteredAt.ShouldBe(registeredAt);
        aggregate.LastChangedAt.ShouldBeNull();
    }

    [Fact]
    public void Create_NormalizesSkuToUppercase()
    {
        // Arrange
        var sku = "dog-food-5lb";

        // Act
        var aggregate = ProductPrice.Create(sku, _testTime);

        // Assert
        aggregate.Sku.ShouldBe("DOG-FOOD-5LB");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidSku_ThrowsArgumentException(string? invalidSku)
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => ProductPrice.Create(invalidSku!, _testTime));
    }

    [Fact]
    public void Apply_InitialPriceSet_TransitionsToPublished()
    {
        // Arrange
        var aggregate = ProductPrice.Create("DOG-FOOD-5LB", _testTime);
        var evt = new InitialPriceSet(
            aggregate.Id,
            aggregate.Sku,
            Money.Of(24.99m, "USD"),
            FloorPrice: Money.Of(15m, "USD"),
            CeilingPrice: Money.Of(50m, "USD"),
            SetBy: _testUserId,
            PricedAt: _testTime);

        // Act
        var result = aggregate.Apply(evt);

        // Assert
        result.Status.ShouldBe(PriceStatus.Published);
        result.BasePrice.ShouldBe(evt.Price);
        result.FloorPrice.ShouldBe(evt.FloorPrice);
        result.CeilingPrice.ShouldBe(evt.CeilingPrice);
        result.LastChangedAt.ShouldBe(evt.PricedAt);
    }

    [Fact]
    public void Apply_InitialPriceSet_WithNullFloorAndCeiling_SetsOnlyBasePrice()
    {
        // Arrange
        var aggregate = ProductPrice.Create("DOG-FOOD-5LB", _testTime);
        var evt = new InitialPriceSet(
            aggregate.Id,
            aggregate.Sku,
            Money.Of(24.99m, "USD"),
            FloorPrice: null,
            CeilingPrice: null,
            SetBy: _testUserId,
            PricedAt: _testTime);

        // Act
        var result = aggregate.Apply(evt);

        // Assert
        result.BasePrice.ShouldBe(evt.Price);
        result.FloorPrice.ShouldBeNull();
        result.CeilingPrice.ShouldBeNull();
    }

    [Fact]
    public void Apply_PriceChanged_UpdatesPriceAndPreviousPrice()
    {
        // Arrange
        var aggregate = CreatePublishedAggregate(basePrice: 24.99m);
        var evt = new PriceChanged(
            aggregate.Id,
            aggregate.Sku,
            OldPrice: Money.Of(24.99m, "USD"),
            NewPrice: Money.Of(19.99m, "USD"),
            PreviousPriceSetAt: _testTime,
            Reason: "Price drop promotion",
            ChangedBy: _testUserId,
            ChangedAt: _testTime.AddDays(1),
            BulkPricingJobId: null,
            SourceSuggestionId: null);

        // Act
        var result = aggregate.Apply(evt);

        // Assert
        result.BasePrice.ShouldBe(evt.NewPrice);
        result.PreviousBasePrice.ShouldBe(evt.OldPrice);
        result.PreviousPriceSetAt.ShouldBe(evt.PreviousPriceSetAt);
        result.LastChangedAt.ShouldBe(evt.ChangedAt);
    }

    [Fact]
    public void Apply_PriceChanged_WithBulkJobId_RecordsTraceability()
    {
        // Arrange
        var aggregate = CreatePublishedAggregate(basePrice: 24.99m);
        var bulkJobId = Guid.NewGuid();
        var evt = new PriceChanged(
            aggregate.Id,
            aggregate.Sku,
            OldPrice: Money.Of(24.99m, "USD"),
            NewPrice: Money.Of(19.99m, "USD"),
            PreviousPriceSetAt: _testTime,
            Reason: "Bulk price update",
            ChangedBy: _testUserId,
            ChangedAt: _testTime.AddDays(1),
            BulkPricingJobId: bulkJobId,
            SourceSuggestionId: null);

        // Act
        var result = aggregate.Apply(evt);

        // Assert - Apply methods don't store BulkJobId/SourceSuggestionId in state
        // (they're event properties for audit trail, not aggregate state)
        result.BasePrice.ShouldBe(evt.NewPrice);
        result.LastChangedAt.ShouldBe(evt.ChangedAt);
    }

    [Fact]
    public void Apply_PriceChangeScheduled_SetsPendingSchedule()
    {
        // Arrange
        var aggregate = CreatePublishedAggregate(basePrice: 24.99m);
        var scheduleId = Guid.NewGuid();
        var evt = new PriceChangeScheduled(
            aggregate.Id,
            aggregate.Sku,
            ScheduleId: scheduleId,
            ScheduledPrice: Money.Of(29.99m, "USD"),
            ScheduledFor: _testTime.AddDays(7),
            ScheduledBy: _testUserId,
            ScheduledAt: _testTime);

        // Act
        var result = aggregate.Apply(evt);

        // Assert
        result.PendingSchedule.ShouldNotBeNull();
        result.PendingSchedule!.ScheduleId.ShouldBe(scheduleId);
        result.PendingSchedule.ScheduledPrice.ShouldBe(evt.ScheduledPrice);
        result.PendingSchedule.ScheduledFor.ShouldBe(evt.ScheduledFor);
        result.PendingSchedule.ScheduledBy.ShouldBe(evt.ScheduledBy);
        result.PendingSchedule.ScheduledAt.ShouldBe(evt.ScheduledAt);
    }

    [Fact]
    public void Apply_ScheduledPriceChangeCancelled_ClearsPendingSchedule()
    {
        // Arrange
        var aggregate = CreatePublishedAggregateWithPendingSchedule();
        var evt = new ScheduledPriceChangeCancelled(
            aggregate.Id,
            aggregate.Sku,
            ScheduleId: aggregate.PendingSchedule!.ScheduleId,
            CancellationReason: "Promotion extended",
            CancelledBy: _testUserId,
            CancelledAt: _testTime.AddDays(1));

        // Act
        var result = aggregate.Apply(evt);

        // Assert
        result.PendingSchedule.ShouldBeNull();
    }

    [Fact]
    public void Apply_ScheduledPriceActivated_ActivatesPriceAndClearsPendingSchedule()
    {
        // Arrange
        var aggregate = CreatePublishedAggregateWithPendingSchedule();
        var evt = new ScheduledPriceActivated(
            aggregate.Id,
            aggregate.Sku,
            ScheduleId: aggregate.PendingSchedule!.ScheduleId,
            ActivatedPrice: Money.Of(29.99m, "USD"),
            ActivatedAt: _testTime.AddDays(7));

        // Act
        var result = aggregate.Apply(evt);

        // Assert
        result.BasePrice.ShouldBe(evt.ActivatedPrice);
        result.PreviousBasePrice.ShouldBe(aggregate.BasePrice);
        result.PreviousPriceSetAt.ShouldBe(aggregate.LastChangedAt ?? aggregate.RegisteredAt);
        result.PendingSchedule.ShouldBeNull();
        result.LastChangedAt.ShouldBe(evt.ActivatedAt);
    }

    [Fact]
    public void Apply_FloorPriceSet_UpdatesFloorPrice()
    {
        // Arrange
        var aggregate = CreatePublishedAggregate(basePrice: 24.99m, floorPrice: 15m);
        var evt = new FloorPriceSet(
            aggregate.Id,
            aggregate.Sku,
            OldFloorPrice: Money.Of(15m, "USD"),
            FloorPrice: Money.Of(18m, "USD"),
            SetBy: _testUserId,
            SetAt: _testTime.AddDays(1),
            ExpiresAt: null);

        // Act
        var result = aggregate.Apply(evt);

        // Assert
        result.FloorPrice.ShouldBe(evt.FloorPrice);
    }

    [Fact]
    public void Apply_CeilingPriceSet_UpdatesCeilingPrice()
    {
        // Arrange
        var aggregate = CreatePublishedAggregate(basePrice: 24.99m, ceilingPrice: 50m);
        var evt = new CeilingPriceSet(
            aggregate.Id,
            aggregate.Sku,
            OldCeilingPrice: Money.Of(50m, "USD"),
            CeilingPrice: Money.Of(45m, "USD"),
            SetBy: _testUserId,
            SetAt: _testTime.AddDays(1),
            ExpiresAt: null);

        // Act
        var result = aggregate.Apply(evt);

        // Assert
        result.CeilingPrice.ShouldBe(evt.CeilingPrice);
    }

    [Fact]
    public void Apply_PriceCorrected_UpdatesBasePriceAndPreviousPrice()
    {
        // Arrange
        var aggregate = CreatePublishedAggregate(basePrice: 24.99m);
        var evt = new PriceCorrected(
            aggregate.Id,
            aggregate.Sku,
            CorrectedPrice: Money.Of(23.99m, "USD"),
            PreviousPrice: Money.Of(24.99m, "USD"),
            CorrectionReason: "Data entry error",
            CorrectedBy: _testUserId,
            CorrectedAt: _testTime.AddDays(1));

        // Act
        var result = aggregate.Apply(evt);

        // Assert
        result.BasePrice.ShouldBe(evt.CorrectedPrice);
        result.PreviousBasePrice.ShouldBe(evt.PreviousPrice);
        result.LastChangedAt.ShouldBe(evt.CorrectedAt);
    }

    [Fact]
    public void Apply_PriceDiscontinued_TransitionsToDiscontinuedAndClearsPendingSchedule()
    {
        // Arrange
        var aggregate = CreatePublishedAggregateWithPendingSchedule();
        var evt = new PriceDiscontinued(
            aggregate.Id,
            aggregate.Sku,
            DiscontinuedAt: _testTime.AddDays(30));

        // Act
        var result = aggregate.Apply(evt);

        // Assert
        result.Status.ShouldBe(PriceStatus.Discontinued);
        result.PendingSchedule.ShouldBeNull();
    }

    [Fact]
    public void Apply_IsImmutable_DoesNotModifyOriginalAggregate()
    {
        // Arrange
        var original = CreatePublishedAggregate(basePrice: 24.99m);
        var evt = new PriceChanged(
            original.Id,
            original.Sku,
            OldPrice: Money.Of(24.99m, "USD"),
            NewPrice: Money.Of(19.99m, "USD"),
            PreviousPriceSetAt: _testTime,
            Reason: null,
            ChangedBy: _testUserId,
            ChangedAt: _testTime.AddDays(1),
            BulkPricingJobId: null,
            SourceSuggestionId: null);

        // Act
        var result = original.Apply(evt);

        // Assert
        original.BasePrice!.Amount.ShouldBe(24.99m);  // Original unchanged
        result.BasePrice!.Amount.ShouldBe(19.99m);    // New instance has updated value
    }

    // ========== Helper Methods ==========

    private ProductPrice CreatePublishedAggregate(
        decimal basePrice = 24.99m,
        decimal? floorPrice = null,
        decimal? ceilingPrice = null)
    {
        var aggregate = ProductPrice.Create("DOG-FOOD-5LB", _testTime);
        return aggregate.Apply(new InitialPriceSet(
            aggregate.Id,
            aggregate.Sku,
            Money.Of(basePrice, "USD"),
            floorPrice.HasValue ? Money.Of(floorPrice.Value, "USD") : null,
            ceilingPrice.HasValue ? Money.Of(ceilingPrice.Value, "USD") : null,
            _testUserId,
            _testTime));
    }

    private ProductPrice CreatePublishedAggregateWithPendingSchedule()
    {
        var aggregate = CreatePublishedAggregate(basePrice: 24.99m);
        return aggregate.Apply(new PriceChangeScheduled(
            aggregate.Id,
            aggregate.Sku,
            ScheduleId: Guid.NewGuid(),
            ScheduledPrice: Money.Of(29.99m, "USD"),
            ScheduledFor: _testTime.AddDays(7),
            ScheduledBy: _testUserId,
            ScheduledAt: _testTime));
    }
}
