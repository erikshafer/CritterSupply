namespace Payments.Processing;

/// <summary>
/// Command from Orders context requesting payment capture.
/// </summary>
public sealed record PaymentRequested(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string PaymentMethodToken);
