namespace Payments.Processing;

/// <summary>
/// Integration message published when payment is captured.
/// Orders saga transitions to PaymentConfirmed.
/// </summary>
public sealed record PaymentCapturedIntegration(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string TransactionId,
    DateTimeOffset CapturedAt);
