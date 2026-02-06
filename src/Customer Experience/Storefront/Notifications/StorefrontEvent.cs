using System.Text.Json.Serialization;

namespace Storefront.Notifications;

/// <summary>
/// Discriminated union for SSE events pushed to Blazor frontend.
/// Base type allows multiplexing multiple event types over single SSE stream.
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
    DateTimeOffset OccurredAt) : StorefrontEvent(OccurredAt);

/// <summary>
/// Order status progressed (placed → payment captured → shipped).
/// </summary>
public sealed record OrderStatusChanged(
    Guid OrderId,
    Guid CustomerId,
    string NewStatus,
    DateTimeOffset OccurredAt) : StorefrontEvent(OccurredAt);

/// <summary>
/// Shipment tracking update (dispatched → in transit → delivered).
/// </summary>
public sealed record ShipmentStatusChanged(
    Guid ShipmentId,
    Guid OrderId,
    Guid CustomerId,
    string NewStatus,
    string? TrackingNumber,
    DateTimeOffset OccurredAt) : StorefrontEvent(OccurredAt);
