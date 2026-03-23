namespace Returns.ReturnProcessing;

/// <summary>
/// Read model projected from Fulfillment.ShipmentDelivered events.
/// Tracks which orders are eligible for returns and their window expiry.
/// Keyed by OrderId for quick lookup during return request validation.
/// </summary>
public sealed record ReturnEligibilityWindow
{
    public Guid Id { get; init; } // Same as OrderId
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public DateTimeOffset DeliveredAt { get; init; }
    public DateTimeOffset WindowExpiresAt { get; init; }
    public IReadOnlyList<EligibleLineItem> EligibleItems { get; init; } = [];
    public bool IsExpired => DateTimeOffset.UtcNow > WindowExpiresAt;

    public static readonly int ReturnWindowDays = 30;
}

public sealed record EligibleLineItem(
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);
