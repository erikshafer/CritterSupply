using Pricing.Products;

namespace Pricing.UnitTests;

/// <summary>
/// Tests for CurrentPriceViewProjection Apply methods.
/// Verifies inline projection correctly denormalizes ProductPrice events into CurrentPriceView.
/// </summary>
public sealed class CurrentPriceViewProjectionTests
{
    private readonly CurrentPriceViewProjection _projection = new();
    private readonly DateTimeOffset _testTime = new(2026, 3, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _testUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_WithInitialPriceSet_CreatesPublishedView()
    {
        // Arrange
        var evt = new InitialPriceSet(
            ProductPriceId: Guid.NewGuid(),
            Sku: "DOG-FOOD-5LB",
            Price: Money.Of(24.99m, "USD"),
            FloorPrice: Money.Of(15m, "USD"),
            CeilingPrice: Money.Of(50m, "USD"),
            SetBy: _testUserId,
            PricedAt: _testTime);

        // Act
        var view = _projection.Create(evt);

        // Assert
        view.Id.ShouldBe("DOG-FOOD-5LB");
        view.Sku.ShouldBe("DOG-FOOD-5LB");
        view.BasePrice.ShouldBe(24.99m);
        view.Currency.ShouldBe("USD");
        view.FloorPrice.ShouldBe(15m);
        view.CeilingPrice.ShouldBe(50m);
        view.PreviousBasePrice.ShouldBeNull();
        view.PreviousPriceSetAt.ShouldBeNull();
        view.Status.ShouldBe(PriceStatus.Published);
        view.HasPendingSchedule.ShouldBe(false);
        view.ScheduledChangeAt.ShouldBeNull();
        view.ScheduledPrice.ShouldBeNull();
        view.LastUpdatedAt.ShouldBe(_testTime);
    }

    [Fact]
    public void Create_WithNullFloorAndCeiling_CreatesViewWithNullConstraints()
    {
        // Arrange
        var evt = new InitialPriceSet(
            ProductPriceId: Guid.NewGuid(),
            Sku: "DOG-FOOD-5LB",
            Price: Money.Of(24.99m, "USD"),
            FloorPrice: null,
            CeilingPrice: null,
            SetBy: _testUserId,
            PricedAt: _testTime);

        // Act
        var view = _projection.Create(evt);

        // Assert
        view.FloorPrice.ShouldBeNull();
        view.CeilingPrice.ShouldBeNull();
    }

    [Fact]
    public void Apply_PriceChanged_UpdatesPriceAndPreviousPrice()
    {
        // Arrange
        var current = CreatePublishedView(basePrice: 24.99m);
        var evt = new PriceChanged(
            ProductPriceId: Guid.NewGuid(),
            Sku: "DOG-FOOD-5LB",
            OldPrice: Money.Of(24.99m, "USD"),
            NewPrice: Money.Of(19.99m, "USD"),
            PreviousPriceSetAt: _testTime,
            Reason: "Price drop",
            ChangedBy: _testUserId,
            ChangedAt: _testTime.AddDays(1),
            BulkPricingJobId: null,
            SourceSuggestionId: null);

        // Act
        var result = _projection.Apply(evt, current);

        // Assert
        result.BasePrice.ShouldBe(19.99m);
        result.PreviousBasePrice.ShouldBe(24.99m);
        result.PreviousPriceSetAt.ShouldBe(_testTime);
        result.LastUpdatedAt.ShouldBe(_testTime.AddDays(1));
    }

    [Fact]
    public void Apply_PriceChangeScheduled_SetsPendingScheduleFlags()
    {
        // Arrange
        var current = CreatePublishedView(basePrice: 24.99m);
        var scheduleId = Guid.NewGuid();
        var evt = new PriceChangeScheduled(
            ProductPriceId: Guid.NewGuid(),
            Sku: "DOG-FOOD-5LB",
            ScheduleId: scheduleId,
            ScheduledPrice: Money.Of(29.99m, "USD"),
            ScheduledFor: _testTime.AddDays(7),
            ScheduledBy: _testUserId,
            ScheduledAt: _testTime);

        // Act
        var result = _projection.Apply(evt, current);

        // Assert
        result.HasPendingSchedule.ShouldBe(true);
        result.ScheduledChangeAt.ShouldBe(_testTime.AddDays(7));
        result.ScheduledPrice.ShouldBe(29.99m);
        result.LastUpdatedAt.ShouldBe(_testTime);
    }

    [Fact]
    public void Apply_ScheduledPriceChangeCancelled_ClearsPendingSchedule()
    {
        // Arrange
        var current = CreateViewWithPendingSchedule();
        var evt = new ScheduledPriceChangeCancelled(
            ProductPriceId: Guid.NewGuid(),
            Sku: "DOG-FOOD-5LB",
            ScheduleId: Guid.NewGuid(),
            CancellationReason: "Promotion extended",
            CancelledBy: _testUserId,
            CancelledAt: _testTime.AddDays(1));

        // Act
        var result = _projection.Apply(evt, current);

        // Assert
        result.HasPendingSchedule.ShouldBe(false);
        result.ScheduledChangeAt.ShouldBeNull();
        result.ScheduledPrice.ShouldBeNull();
        result.LastUpdatedAt.ShouldBe(_testTime.AddDays(1));
    }

    [Fact]
    public void Apply_ScheduledPriceActivated_ActivatesPriceAndClearsPendingSchedule()
    {
        // Arrange
        var current = CreateViewWithPendingSchedule();
        var evt = new ScheduledPriceActivated(
            ProductPriceId: Guid.NewGuid(),
            Sku: "DOG-FOOD-5LB",
            ScheduleId: Guid.NewGuid(),
            ActivatedPrice: Money.Of(29.99m, "USD"),
            ActivatedAt: _testTime.AddDays(7));

        // Act
        var result = _projection.Apply(evt, current);

        // Assert
        result.BasePrice.ShouldBe(29.99m);
        result.PreviousBasePrice.ShouldBe(24.99m);  // Was current.BasePrice
        result.PreviousPriceSetAt.ShouldBe(_testTime);  // Was current.LastUpdatedAt
        result.HasPendingSchedule.ShouldBe(false);
        result.ScheduledChangeAt.ShouldBeNull();
        result.ScheduledPrice.ShouldBeNull();
        result.LastUpdatedAt.ShouldBe(_testTime.AddDays(7));
    }

    [Fact]
    public void Apply_FloorPriceSet_UpdatesFloorPrice()
    {
        // Arrange
        var current = CreatePublishedView(basePrice: 24.99m, floorPrice: 15m);
        var evt = new FloorPriceSet(
            ProductPriceId: Guid.NewGuid(),
            Sku: "DOG-FOOD-5LB",
            OldFloorPrice: Money.Of(15m, "USD"),
            FloorPrice: Money.Of(18m, "USD"),
            SetBy: _testUserId,
            SetAt: _testTime.AddDays(1),
            ExpiresAt: null);

        // Act
        var result = _projection.Apply(evt, current);

        // Assert
        result.FloorPrice.ShouldBe(18m);
        result.LastUpdatedAt.ShouldBe(_testTime.AddDays(1));
    }

    [Fact]
    public void Apply_CeilingPriceSet_UpdatesCeilingPrice()
    {
        // Arrange
        var current = CreatePublishedView(basePrice: 24.99m, ceilingPrice: 50m);
        var evt = new CeilingPriceSet(
            ProductPriceId: Guid.NewGuid(),
            Sku: "DOG-FOOD-5LB",
            OldCeilingPrice: Money.Of(50m, "USD"),
            CeilingPrice: Money.Of(45m, "USD"),
            SetBy: _testUserId,
            SetAt: _testTime.AddDays(1),
            ExpiresAt: null);

        // Act
        var result = _projection.Apply(evt, current);

        // Assert
        result.CeilingPrice.ShouldBe(45m);
        result.LastUpdatedAt.ShouldBe(_testTime.AddDays(1));
    }

    [Fact]
    public void Apply_PriceCorrected_UpdatesBasePriceAndPreviousPrice()
    {
        // Arrange
        var current = CreatePublishedView(basePrice: 24.99m);
        var evt = new PriceCorrected(
            ProductPriceId: Guid.NewGuid(),
            Sku: "DOG-FOOD-5LB",
            CorrectedPrice: Money.Of(23.99m, "USD"),
            PreviousPrice: Money.Of(24.99m, "USD"),
            CorrectionReason: "Data entry error",
            CorrectedBy: _testUserId,
            CorrectedAt: _testTime.AddDays(1));

        // Act
        var result = _projection.Apply(evt, current);

        // Assert
        result.BasePrice.ShouldBe(23.99m);
        result.PreviousBasePrice.ShouldBe(24.99m);
        result.LastUpdatedAt.ShouldBe(_testTime.AddDays(1));
    }

    [Fact]
    public void Apply_PriceDiscontinued_TransitionsToDiscontinuedAndClearsPendingSchedule()
    {
        // Arrange
        var current = CreateViewWithPendingSchedule();
        var evt = new PriceDiscontinued(
            ProductPriceId: Guid.NewGuid(),
            Sku: "DOG-FOOD-5LB",
            DiscontinuedAt: _testTime.AddDays(30));

        // Act
        var result = _projection.Apply(evt, current);

        // Assert
        result.Status.ShouldBe(PriceStatus.Discontinued);
        result.HasPendingSchedule.ShouldBe(false);
        result.ScheduledChangeAt.ShouldBeNull();
        result.ScheduledPrice.ShouldBeNull();
        result.LastUpdatedAt.ShouldBe(_testTime.AddDays(30));
    }

    [Fact]
    public void Apply_IsImmutable_DoesNotModifyOriginalView()
    {
        // Arrange
        var original = CreatePublishedView(basePrice: 24.99m);
        var evt = new PriceChanged(
            ProductPriceId: Guid.NewGuid(),
            Sku: "DOG-FOOD-5LB",
            OldPrice: Money.Of(24.99m, "USD"),
            NewPrice: Money.Of(19.99m, "USD"),
            PreviousPriceSetAt: _testTime,
            Reason: null,
            ChangedBy: _testUserId,
            ChangedAt: _testTime.AddDays(1),
            BulkPricingJobId: null,
            SourceSuggestionId: null);

        // Act
        var result = _projection.Apply(evt, original);

        // Assert
        original.BasePrice.ShouldBe(24.99m);  // Original unchanged
        result.BasePrice.ShouldBe(19.99m);    // New instance has updated value
    }

    [Fact]
    public void Projection_UsesSkuAsDocumentId()
    {
        // Arrange
        var evt = new InitialPriceSet(
            ProductPriceId: Guid.NewGuid(),
            Sku: "DOG-FOOD-5LB",
            Price: Money.Of(24.99m, "USD"),
            FloorPrice: null,
            CeilingPrice: null,
            SetBy: _testUserId,
            PricedAt: _testTime);

        // Act
        var view = _projection.Create(evt);

        // Assert
        view.Id.ShouldBe(view.Sku);
        view.Id.ShouldBe("DOG-FOOD-5LB");
    }

    // ========== Helper Methods ==========

    private CurrentPriceView CreatePublishedView(
        decimal basePrice = 24.99m,
        decimal? floorPrice = null,
        decimal? ceilingPrice = null)
    {
        return new CurrentPriceView
        {
            Id = "DOG-FOOD-5LB",
            Sku = "DOG-FOOD-5LB",
            BasePrice = basePrice,
            Currency = "USD",
            FloorPrice = floorPrice,
            CeilingPrice = ceilingPrice,
            Status = PriceStatus.Published,
            HasPendingSchedule = false,
            LastUpdatedAt = _testTime
        };
    }

    private CurrentPriceView CreateViewWithPendingSchedule()
    {
        return new CurrentPriceView
        {
            Id = "DOG-FOOD-5LB",
            Sku = "DOG-FOOD-5LB",
            BasePrice = 24.99m,
            Currency = "USD",
            Status = PriceStatus.Published,
            HasPendingSchedule = true,
            ScheduledChangeAt = _testTime.AddDays(7),
            ScheduledPrice = 29.99m,
            LastUpdatedAt = _testTime
        };
    }
}
