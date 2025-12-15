namespace Inventory.Management;

/// <summary>
/// Domain event indicating returned items have been inspected and added back to inventory.
/// </summary>
public sealed record StockRestocked(
    Guid ReturnId,
    int Quantity,
    DateTimeOffset RestockedAt);
