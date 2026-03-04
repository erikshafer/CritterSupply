using System.Text.Json.Serialization;

namespace Storefront.RealTime;

/// <summary>
/// Discriminated union for real-time events pushed to Blazor frontend via SignalR.
/// Base type allows multiplexing multiple event types over single SignalR connection.
/// Wrapped in CloudEvents envelope by Wolverine's SignalR transport.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
[JsonDerivedType(typeof(CartUpdated), typeDiscriminator: "cart-updated")]
[JsonDerivedType(typeof(OrderStatusChanged), typeDiscriminator: "order-status-changed")]
[JsonDerivedType(typeof(ShipmentStatusChanged), typeDiscriminator: "shipment-status-changed")]
public abstract record StorefrontEvent(DateTimeOffset OccurredAt);

/// <summary>
/// Cart state changed (item added/removed/quantity changed).
/// </summary>
public sealed record CartUpdated(
    Guid CartId,
    Guid CustomerId,
    int ItemCount,
    decimal TotalAmount,
    DateTimeOffset OccurredAt) : StorefrontEvent(OccurredAt), IStorefrontWebSocketMessage;

/// <summary>
/// Order status progressed (placed → payment captured → shipped).
/// </summary>
public sealed record OrderStatusChanged(
    Guid OrderId,
    Guid CustomerId,
    string NewStatus,
    DateTimeOffset OccurredAt) : StorefrontEvent(OccurredAt), IStorefrontWebSocketMessage;

/// <summary>
/// Shipment tracking update (dispatched → in transit → delivered).
/// </summary>
public sealed record ShipmentStatusChanged(
    Guid ShipmentId,
    Guid OrderId,
    Guid CustomerId,
    string NewStatus,
    string? TrackingNumber,
    DateTimeOffset OccurredAt) : StorefrontEvent(OccurredAt), IStorefrontWebSocketMessage;
