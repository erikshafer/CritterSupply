namespace Payments.Processing;

/// <summary>
/// Command from Orders context requesting payment authorization (funds held but not captured).
/// </summary>
public sealed record AuthorizePayment(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string PaymentMethodToken);
