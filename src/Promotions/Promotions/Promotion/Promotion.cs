namespace Promotions.Promotion;

/// <summary>
/// Event-sourced aggregate representing a promotion campaign.
/// Stream key: UUID v7 (time-ordered) for natural chronological ordering of promotions.
/// Phase 1: Manual activation, percentage discounts only.
/// Phase 2+: Scheduled messages, fixed amount discounts, free shipping.
/// </summary>
public sealed record Promotion
{
    /// <summary>
    /// Stream ID — UUID v7 (time-ordered).
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Display name for the promotion (e.g., "Holiday Sale 2026").
    /// </summary>
    public string Name { get; init; } = null!;

    /// <summary>
    /// Description of the promotion for admin UI and customer display.
    /// </summary>
    public string Description { get; init; } = null!;

    /// <summary>
    /// Type of discount offered (PercentageOff, FixedAmountOff, FreeShipping).
    /// Phase 1: PercentageOff only.
    /// </summary>
    public DiscountType DiscountType { get; init; }

    /// <summary>
    /// Discount value. Interpretation depends on DiscountType:
    /// - PercentageOff: 0-100 (e.g., 15 = 15% off)
    /// - FixedAmountOff: USD amount (e.g., 5.00 = $5 off) [Phase 2]
    /// - FreeShipping: ignored (presence of coupon grants benefit) [Phase 2]
    /// Phase 1: Using decimal (not Money VO) for simplicity.
    /// </summary>
    public decimal DiscountValue { get; init; }

    /// <summary>
    /// Lifecycle state: Draft → Active → (Paused ↔ Active) → Expired/Cancelled.
    /// </summary>
    public PromotionStatus Status { get; init; }

    /// <summary>
    /// Promotion validity period start (inclusive).
    /// </summary>
    public DateTimeOffset StartDate { get; init; }

    /// <summary>
    /// Promotion validity period end (inclusive).
    /// </summary>
    public DateTimeOffset EndDate { get; init; }

    /// <summary>
    /// Maximum total redemptions allowed (null = unlimited).
    /// Enforced via optimistic concurrency when recording redemptions.
    /// </summary>
    public int? UsageLimit { get; init; }

    /// <summary>
    /// Current number of times this promotion has been redeemed.
    /// Incremented via PromotionRedemptionRecorded event.
    /// </summary>
    public int CurrentRedemptionCount { get; init; }

    /// <summary>
    /// When the promotion was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the promotion was last activated.
    /// Null if never activated.
    /// </summary>
    public DateTimeOffset? ActivatedAt { get; init; }

    /// <summary>
    /// When the promotion was paused (if currently paused).
    /// Null if not currently paused.
    /// </summary>
    public DateTimeOffset? PausedAt { get; init; }

    /// <summary>
    /// Creates a new Promotion aggregate in Draft status.
    /// Called by CreatePromotionHandler.
    /// Phase 1: Manual activation only (no scheduled messages).
    /// </summary>
    public static Promotion Create(
        Guid id,
        string name,
        string description,
        DiscountType discountType,
        decimal discountValue,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        int? usageLimit,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(description, nameof(description));

        return new Promotion
        {
            Id = id,
            Name = name,
            Description = description,
            DiscountType = discountType,
            DiscountValue = discountValue,
            Status = PromotionStatus.Draft,
            StartDate = startDate,
            EndDate = endDate,
            UsageLimit = usageLimit,
            CreatedAt = createdAt
        };
    }

    public Promotion Apply(PromotionCreated @event) =>
        this with
        {
            Id = @event.PromotionId,
            Name = @event.Name,
            Description = @event.Description,
            DiscountType = @event.DiscountType,
            DiscountValue = @event.DiscountValue,
            Status = PromotionStatus.Draft,
            StartDate = @event.StartDate,
            EndDate = @event.EndDate,
            UsageLimit = @event.UsageLimit,
            CreatedAt = @event.CreatedAt
        };

    public Promotion Apply(PromotionActivated @event) =>
        this with
        {
            Status = PromotionStatus.Active,
            ActivatedAt = @event.ActivatedAt
        };

    public Promotion Apply(PromotionPaused @event) =>
        this with
        {
            Status = PromotionStatus.Paused,
            PausedAt = @event.PausedAt
        };

    public Promotion Apply(PromotionResumed @event) =>
        this with
        {
            Status = PromotionStatus.Active,
            PausedAt = null
        };

    public Promotion Apply(PromotionCancelled @event) =>
        this with
        {
            Status = PromotionStatus.Cancelled
        };

    public Promotion Apply(PromotionExpired @event) =>
        this with
        {
            Status = PromotionStatus.Expired
        };

    public Promotion Apply(PromotionRedemptionRecorded @event) =>
        this with
        {
            CurrentRedemptionCount = CurrentRedemptionCount + 1
        };

    public Promotion Apply(CouponBatchGenerated @event) =>
        this;  // No state change needed - batch metadata is in the event itself
}
