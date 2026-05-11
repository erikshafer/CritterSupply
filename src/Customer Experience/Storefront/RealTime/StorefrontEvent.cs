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
[JsonDerivedType(typeof(ReturnStatusChanged), typeDiscriminator: "return-status-changed")]
[JsonDerivedType(typeof(ReturnExchangePaymentChanged), typeDiscriminator: "return-exchange-payment-changed")]
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

/// <summary>
/// Return status changed (requested → approved → received → completed/rejected/expired).
/// Provides real-time updates on return lifecycle to customer UI.
/// </summary>
public sealed record ReturnStatusChanged(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string NewStatus,
    string? Details,
    DateTimeOffset OccurredAt) : StorefrontEvent(OccurredAt), IStorefrontWebSocketMessage;

/// <summary>
/// Cross-product-exchange payment update — either the additional-payment
/// delta has been captured for a more-expensive replacement, or the
/// price-difference partial refund has been issued for a cheaper
/// replacement. M47.0 / Slice 3 surfaces the structured payment metadata
/// added in Slice 2 so the customer sees the new charge / refund line in
/// real time without re-querying Payments.
///
/// <para>
/// <see cref="PaymentKind"/> is <c>"Capture"</c> or <c>"Refund"</c> —
/// distinguishes the two flows over a single SignalR discriminator.
/// <see cref="PaymentReference"/> is the gateway transaction id /
/// reference (whichever the originating Payments-side message carried);
/// it is shown verbatim and selectable so customers can quote it to
/// support. <see cref="PaymentId"/> is the Payments-side stream id —
/// either the deterministic delta-capture stream (capture) or the
/// original order's payment stream (refund) — kept for future deep-link
/// to a receipt page.
/// </para>
/// </summary>
public sealed record ReturnExchangePaymentChanged(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string PaymentKind,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    string PaymentReference,
    DateTimeOffset OccurredAt) : StorefrontEvent(OccurredAt), IStorefrontWebSocketMessage;
