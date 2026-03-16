namespace Backoffice.Clients;

/// <summary>
/// HTTP client for querying Fulfillment BC (admin use)
/// </summary>
public interface IFulfillmentClient
{
    /// <summary>
    /// Get shipments for an order (CS workflow: "Where is my order?" inquiry)
    /// </summary>
    Task<IReadOnlyList<ShipmentDto>> GetShipmentsForOrderAsync(
        Guid orderId,
        CancellationToken ct = default);
}

/// <summary>
/// Shipment DTO from Fulfillment BC
/// </summary>
public sealed record ShipmentDto(
    Guid Id,
    Guid OrderId,
    DateTime DispatchedAt,
    string Status,
    string Carrier,
    string TrackingNumber,
    DateTime? DeliveredAt,
    string? DeliveryNote);
