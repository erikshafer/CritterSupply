using System.Text.Json.Serialization;

namespace Backoffice.RealTime;

/// <summary>
/// Base class for all Backoffice real-time events sent via SignalR.
/// Uses discriminated union pattern with JSON polymorphism for type-safe deserialization.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
[JsonDerivedType(typeof(LiveMetricUpdated), typeDiscriminator: "live-metric-updated")]
[JsonDerivedType(typeof(AlertCreated), typeDiscriminator: "alert-created")]
[JsonDerivedType(typeof(ActiveOrderIncremented), typeDiscriminator: "active-order-incremented")]
[JsonDerivedType(typeof(ActiveOrderDecremented), typeDiscriminator: "active-order-decremented")]
[JsonDerivedType(typeof(PendingReturnIncremented), typeDiscriminator: "pending-return-incremented")]
public abstract record BackofficeEvent(DateTimeOffset OccurredAt);

/// <summary>
/// Real-time metric update for executive dashboard.
/// Sent to role:executive group when key business metrics change.
/// </summary>
public sealed record LiveMetricUpdated(
    int ActiveOrders,
    int PendingReturns,
    int LowStockAlerts,
    decimal TodaysRevenue,
    DateTimeOffset OccurredAt) : BackofficeEvent(OccurredAt), IBackofficeWebSocketMessage;

/// <summary>
/// Alert notification for operations team.
/// Sent to role:operations-manager group when system issues or business exceptions occur.
/// </summary>
public sealed record AlertCreated(
    string Title,
    string Severity,
    string Message,
    DateTimeOffset OccurredAt) : BackofficeEvent(OccurredAt), IBackofficeWebSocketMessage;

/// <summary>
/// Active order counter incremented (order placed).
/// Sent to role:executive group to increment active order count.
/// </summary>
public sealed record ActiveOrderIncremented(
    Guid OrderId,
    DateTimeOffset OccurredAt) : BackofficeEvent(OccurredAt), IBackofficeWebSocketMessage;

/// <summary>
/// Active order counter decremented (order fulfilled).
/// Sent to role:executive group to decrement active order count.
/// </summary>
public sealed record ActiveOrderDecremented(
    Guid OrderId,
    DateTimeOffset OccurredAt) : BackofficeEvent(OccurredAt), IBackofficeWebSocketMessage;

/// <summary>
/// Pending return counter incremented (return requested).
/// Sent to role:executive group to increment pending returns count.
/// </summary>
public sealed record PendingReturnIncremented(
    Guid ReturnId,
    DateTimeOffset OccurredAt) : BackofficeEvent(OccurredAt), IBackofficeWebSocketMessage;
