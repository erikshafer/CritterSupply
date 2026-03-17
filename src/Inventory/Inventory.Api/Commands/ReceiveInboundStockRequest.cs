namespace Inventory.Api.Commands;

/// <summary>
/// Request DTO for receiving inbound stock shipments.
/// </summary>
public sealed record ReceiveInboundStockRequest(
    int Quantity,
    string Source);
