namespace Payments.Processing;

/// <summary>
/// Result from a payment gateway operation.
/// </summary>
public sealed record GatewayResult(
    bool Success,
    string? TransactionId,
    string? FailureReason,
    bool IsRetriable);
