namespace Inventory.Management;

/// <summary>
/// Domain event indicating new stock has arrived from a supplier or warehouse transfer.
/// </summary>
public sealed record StockReceived(
    int Quantity,
    string Source,
    DateTimeOffset ReceivedAt);
