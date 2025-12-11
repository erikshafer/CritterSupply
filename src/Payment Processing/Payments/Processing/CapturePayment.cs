namespace Payments.Processing;

/// <summary>
/// Command from Orders context requesting capture of a previously authorized payment.
/// </summary>
public sealed record CapturePayment(
    Guid PaymentId,
    Guid OrderId,
    decimal? AmountToCapture = null); // null = capture full authorized amount
