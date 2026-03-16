using System.Text.Json.Serialization;

namespace Backoffice.RealTime;

/// <summary>
/// Base class for all Backoffice real-time events sent via SignalR.
/// Uses discriminated union pattern with JSON polymorphism for type-safe deserialization.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
[JsonDerivedType(typeof(LiveMetricUpdated), typeDiscriminator: "live-metric-updated")]
[JsonDerivedType(typeof(AlertCreated), typeDiscriminator: "alert-created")]
public abstract record BackofficeEvent(DateTimeOffset OccurredAt);

/// <summary>
/// Real-time metric update for executive dashboard.
/// Sent to role:executive group when key business metrics change.
/// </summary>
public sealed record LiveMetricUpdated(
    int OrderCount,
    decimal Revenue,
    decimal PaymentFailureRate,
    DateTimeOffset OccurredAt) : BackofficeEvent(OccurredAt), IBackofficeWebSocketMessage;

/// <summary>
/// Alert notification for operations team.
/// Sent to role:operations group when system issues or business exceptions occur.
/// </summary>
public sealed record AlertCreated(
    string AlertType,
    string Severity,
    string Message,
    DateTimeOffset OccurredAt) : BackofficeEvent(OccurredAt), IBackofficeWebSocketMessage;
