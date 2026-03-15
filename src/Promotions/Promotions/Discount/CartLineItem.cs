namespace Promotions.Discount;

/// <summary>
/// Represents a cart line item for discount calculation.
/// Snapshot pattern: captures item data at calculation time.
/// Phase 1: Simple SKU + quantity + price.
/// Phase 2+: Add category, vendor, eligibility flags.
/// </summary>
public sealed record CartLineItem(
    string Sku,
    int Quantity,
    decimal UnitPrice);
