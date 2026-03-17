using System.Security.Cryptography;
using System.Text;

namespace Pricing.Products;

/// <summary>
/// Event-sourced aggregate representing the authoritative price record for a single SKU.
/// Stream key: Deterministic Guid using UUID v5 (SHA-1, RFC 4122 §4.3) from SKU string.
/// See ADR 0016 for rationale (why not UUID v7, why not MD5).
/// </summary>
public sealed record ProductPrice
{
    /// <summary>
    /// Stream ID — deterministic UUID v5 derived from SKU.
    /// Generated via StreamId(sku) factory method.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Canonical SKU string (normalized to uppercase).
    /// </summary>
    public string Sku { get; init; } = null!;

    /// <summary>
    /// Lifecycle state: Unpriced → Published → Discontinued (terminal).
    /// </summary>
    public PriceStatus Status { get; init; }

    /// <summary>
    /// Current base price. Null until InitialPriceSet.
    /// </summary>
    public Money? BasePrice { get; init; }

    /// <summary>
    /// Minimum allowed base price (internal margin protection).
    /// Phase 1: Single floor price. Phase 2+: separate MapPrice for vendor MAP obligations.
    /// </summary>
    public Money? FloorPrice { get; init; }

    /// <summary>
    /// Maximum allowed base price (MAP constraint or policy ceiling).
    /// </summary>
    public Money? CeilingPrice { get; init; }

    /// <summary>
    /// Previous base price (for Was/Now display in BFF).
    /// Set on every PriceChanged event.
    /// </summary>
    public Money? PreviousBasePrice { get; init; }

    /// <summary>
    /// When the previous price was current (UTC).
    /// Used for Was/Now TTL calculation (30 days from price drop, per event modeling doc).
    /// </summary>
    public DateTimeOffset? PreviousPriceSetAt { get; init; }

    /// <summary>
    /// Pending scheduled price change. Null if no schedule active.
    /// Only one schedule allowed at a time (invariant enforced in handlers).
    /// </summary>
    public ScheduledPriceChange? PendingSchedule { get; init; }

    /// <summary>
    /// When the ProductPrice stream was created (from ProductAdded integration event).
    /// </summary>
    public DateTimeOffset RegisteredAt { get; init; }

    /// <summary>
    /// When the price was last changed (PriceChanged, ScheduledPriceActivated, PriceCorrected).
    /// Null if never changed (only InitialPriceSet).
    /// </summary>
    public DateTimeOffset? LastChangedAt { get; init; }

    /// <summary>
    /// Creates a new ProductPrice aggregate in Unpriced state.
    /// Called by ProductAddedHandler (integration event).
    /// </summary>
    public static ProductPrice Create(string sku, DateTimeOffset registeredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku, nameof(sku));

        return new ProductPrice
        {
            Id = StreamId(sku),
            Sku = sku.ToUpperInvariant(),
            Status = PriceStatus.Unpriced,
            RegisteredAt = registeredAt
        };
    }

    /// <summary>
    /// Generates a deterministic UUID v5 stream ID from SKU string.
    /// Uses RFC 4122 §4.3 (SHA-1 + URL namespace UUID).
    /// WHY NOT UUID v7: v7 is timestamp-random, cannot produce same value twice from same input.
    /// WHY NOT MD5: not RFC 4122-compliant, no namespace isolation. See ADR 0016.
    /// NORMALIZATION: ToUpperInvariant() ensures "dog-food-5lb" == "DOG-FOOD-5LB".
    /// </summary>
    public static Guid StreamId(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku, nameof(sku));

        // RFC 4122 URL namespace UUID
        var namespaceBytes = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8").ToByteArray();

        // Normalize SKU to uppercase for case-insensitive determinism
        var nameBytes = Encoding.UTF8.GetBytes($"pricing:{sku.ToUpperInvariant()}");

        // Compute SHA-1 hash (UUID v5 uses SHA-1 per RFC 4122 §4.3)
        var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);

        // Set version (4 bits): 0101 (5) at offset 48-51
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);

        // Set variant (2 bits): 10 at offset 64-65 (RFC 4122)
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        // Take first 16 bytes for UUID
        return new Guid(hash[..16]);
    }

    public ProductPrice Apply(ProductRegistered @event) =>
        this with
        {
            Id = @event.ProductPriceId,
            Sku = @event.Sku.ToUpperInvariant(),
            Status = PriceStatus.Unpriced,
            RegisteredAt = @event.RegisteredAt
        };

    public ProductPrice Apply(InitialPriceSet @event) =>
        this with
        {
            Status = PriceStatus.Published,
            BasePrice = @event.Price,
            FloorPrice = @event.FloorPrice,
            CeilingPrice = @event.CeilingPrice,
            LastChangedAt = @event.PricedAt
        };

    public ProductPrice Apply(PriceChanged @event) =>
        this with
        {
            BasePrice = @event.NewPrice,
            PreviousBasePrice = @event.OldPrice,
            PreviousPriceSetAt = @event.PreviousPriceSetAt,
            LastChangedAt = @event.ChangedAt
        };

    public ProductPrice Apply(PriceChangeScheduled @event) =>
        this with
        {
            PendingSchedule = new ScheduledPriceChange
            {
                ScheduleId = @event.ScheduleId,
                ScheduledPrice = @event.ScheduledPrice,
                ScheduledFor = @event.ScheduledFor,
                ScheduledBy = @event.ScheduledBy,
                ScheduledAt = @event.ScheduledAt
            }
        };

    public ProductPrice Apply(ScheduledPriceChangeCancelled @event) =>
        this with
        {
            PendingSchedule = null
        };

    public ProductPrice Apply(ScheduledPriceActivated @event) =>
        this with
        {
            BasePrice = @event.ActivatedPrice,
            PreviousBasePrice = BasePrice,
            PreviousPriceSetAt = LastChangedAt ?? RegisteredAt,
            PendingSchedule = null,
            LastChangedAt = @event.ActivatedAt
        };

    public ProductPrice Apply(FloorPriceSet @event) =>
        this with
        {
            FloorPrice = @event.FloorPrice
        };

    public ProductPrice Apply(CeilingPriceSet @event) =>
        this with
        {
            CeilingPrice = @event.CeilingPrice
        };

    public ProductPrice Apply(PriceCorrected @event) =>
        this with
        {
            BasePrice = @event.CorrectedPrice,
            PreviousBasePrice = @event.PreviousPrice,
            LastChangedAt = @event.CorrectedAt
        };

    public ProductPrice Apply(PriceDiscontinued @event) =>
        this with
        {
            Status = PriceStatus.Discontinued,
            PendingSchedule = null
        };
}
