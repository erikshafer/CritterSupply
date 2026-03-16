namespace Backoffice.RealTime;

/// <summary>
/// Marker interface for messages sent to Backoffice users via SignalR.
/// Messages implementing this interface are automatically routed to the SignalR transport.
/// </summary>
/// <remarks>
/// This pattern enables Wolverine to discover and route real-time events to the SignalR hub
/// without coupling the domain logic to SignalR infrastructure.
///
/// Example usage in integration message handlers:
/// - OrderPlacedAdminHandler publishes LiveMetricUpdated (role:executive group)
/// - PaymentFailedAdminHandler publishes AlertCreated (role:operations group)
///
/// SignalR groups are role-based: role:cs-agent, role:warehouse-clerk, role:operations-manager, role:executive, role:system-admin
/// </remarks>
public interface IBackofficeWebSocketMessage
{
}
