namespace Payments.Processing;

/// <summary>
/// Domain event when payment is successfully captured.
/// Persisted to the Marten event store.
/// </summary>
public sealed record PaymentCaptured(
    Guid PaymentId,
    string TransactionId,
    DateTimeOffset CapturedAt);
