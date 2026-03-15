using System.Security.Cryptography;
using System.Text;

namespace Promotions.Coupon;

/// <summary>
/// Event-sourced aggregate representing a single coupon instance.
/// Stream key: Deterministic Guid using UUID v5 (SHA-1, RFC 4122 §4.3) from coupon code string.
/// Phase 1: Single-use coupons only. Multi-use is future extension.
/// </summary>
public sealed record Coupon
{
    /// <summary>
    /// Stream ID — deterministic UUID v5 derived from coupon code.
    /// Generated via StreamId(code) factory method.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Unique coupon code string (e.g., "HOLIDAY2026", "SAVE15").
    /// Normalized to uppercase for case-insensitive lookups.
    /// </summary>
    public string Code { get; init; } = null!;

    /// <summary>
    /// Reference to the parent promotion.
    /// </summary>
    public Guid PromotionId { get; init; }

    /// <summary>
    /// Lifecycle state: Issued → Redeemed/Revoked/Expired.
    /// Phase 1: Single-use only (Redeemed is terminal).
    /// </summary>
    public CouponStatus Status { get; init; }

    /// <summary>
    /// When the coupon was issued.
    /// </summary>
    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// When the coupon was redeemed (null if not yet redeemed).
    /// </summary>
    public DateTimeOffset? RedeemedAt { get; init; }

    /// <summary>
    /// Order ID where the coupon was redeemed (null if not yet redeemed).
    /// </summary>
    public Guid? OrderId { get; init; }

    /// <summary>
    /// Customer ID who redeemed the coupon (null if not yet redeemed).
    /// </summary>
    public Guid? CustomerId { get; init; }

    /// <summary>
    /// Creates a new Coupon aggregate in Issued state.
    /// Called by IssueCouponHandler.
    /// </summary>
    public static Coupon Create(string code, Guid promotionId, DateTimeOffset issuedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code, nameof(code));

        return new Coupon
        {
            Id = StreamId(code),
            Code = code.ToUpperInvariant(),
            PromotionId = promotionId,
            Status = CouponStatus.Issued,
            IssuedAt = issuedAt
        };
    }

    /// <summary>
    /// Generates a deterministic UUID v5 stream ID from coupon code string.
    /// Uses RFC 4122 §4.3 (SHA-1 + URL namespace UUID).
    /// WHY UUID v5: Deterministic — same code always produces same UUID.
    /// Allows idempotent coupon issuance (duplicate codes don't create duplicate streams).
    /// NORMALIZATION: ToUpperInvariant() ensures "holiday2026" == "HOLIDAY2026".
    /// </summary>
    public static Guid StreamId(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code, nameof(code));

        // RFC 4122 URL namespace UUID
        var namespaceBytes = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8").ToByteArray();

        // Normalize coupon code to uppercase for case-insensitive determinism
        var nameBytes = Encoding.UTF8.GetBytes($"promotions:coupon:{code.ToUpperInvariant()}");

        // Compute SHA-1 hash (UUID v5 uses SHA-1 per RFC 4122 §4.3)
        var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);

        // Set version (4 bits): 0101 (5) at offset 48-51
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);

        // Set variant (2 bits): 10 at offset 64-65 (RFC 4122)
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        // Take first 16 bytes for UUID
        return new Guid(hash[..16]);
    }

    public Coupon Apply(CouponIssued @event) =>
        this with
        {
            Id = StreamId(@event.CouponCode),
            Code = @event.CouponCode.ToUpperInvariant(),
            PromotionId = @event.PromotionId,
            Status = CouponStatus.Issued,
            IssuedAt = @event.IssuedAt
        };

    public Coupon Apply(CouponRedeemed @event) =>
        this with
        {
            Status = CouponStatus.Redeemed,
            RedeemedAt = @event.RedeemedAt,
            OrderId = @event.OrderId,
            CustomerId = @event.CustomerId
        };

    public Coupon Apply(CouponRevoked @event) =>
        this with
        {
            Status = CouponStatus.Revoked
        };

    public Coupon Apply(CouponExpired @event) =>
        this with
        {
            Status = CouponStatus.Expired
        };
}
