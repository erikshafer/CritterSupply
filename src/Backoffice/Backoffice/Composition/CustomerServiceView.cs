namespace Backoffice.Composition;

/// <summary>
/// Composed view for CS agents: customer info + order history
/// </summary>
public sealed record CustomerServiceView(
    Guid CustomerId,
    string Email,
    string FirstName,
    string LastName,
    DateTime CreatedAt,
    IReadOnlyList<OrderSummaryView> Orders);

/// <summary>
/// Order summary for CS customer view
/// </summary>
public sealed record OrderSummaryView(
    Guid OrderId,
    DateTime PlacedAt,
    string Status,
    decimal TotalAmount);
