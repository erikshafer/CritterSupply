using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Payments.Processing;
using Shouldly;

namespace Payments.UnitTests.Processing;

/// <summary>
/// Property-based tests for Payment capture and failure behavior.
/// </summary>
public class PaymentCapturePropertyTests
{
    /// <summary>
    /// **Feature: payment-processing, Property 3: PaymentCaptured event correctly updates Payment state**
    ///
    /// *For any* Payment with a PaymentCaptured event applied, the Payment status SHALL be Captured,
    /// the transaction ID SHALL be recorded, and the processed timestamp SHALL be set.
    ///
    /// **Validates: Requirements 2.2, 2.3, 2.4 - Event Sourcing Apply() logic**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidPaymentWithSuccessArbitrary)])]
    public bool PaymentCaptured_Event_Updates_Payment_State(
        PaymentInitiated initiatedEvent,
        string transactionId)
    {
        // Arrange: Create a payment from initiated event
        var payment = Payment.Create(initiatedEvent);
        var capturedAt = DateTimeOffset.UtcNow;
        var capturedEvent = new PaymentCaptured(payment.Id, transactionId, capturedAt);

        // Act: Apply the captured event
        var updatedPayment = payment.Apply(capturedEvent);

        // Assert: Verify payment state
        // 2.2: Status is Captured
        var statusIsCaptured = updatedPayment.Status == PaymentStatus.Captured;

        // 2.3: Transaction ID is recorded
        var transactionIdRecorded = updatedPayment.TransactionId == transactionId;

        // 2.4: Capture timestamp is recorded
        var timestampRecorded = updatedPayment.ProcessedAt == capturedAt;

        return statusIsCaptured
            && transactionIdRecorded
            && timestampRecorded;
    }

    /// <summary>
    /// **Feature: payment-processing, Property 4: PaymentFailed event correctly updates Payment state**
    ///
    /// *For any* Payment with a PaymentFailed event applied, the Payment status SHALL be Failed,
    /// the failure reason SHALL be recorded, and the isRetriable flag SHALL be set.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.3 - Event Sourcing Apply() logic**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(FailureDataArbitrary)])]
    public bool PaymentFailed_Event_Updates_Payment_State(
        PaymentInitiated initiatedEvent,
        string failureReason,
        bool isRetriable)
    {
        // Arrange: Create a payment from initiated event
        var payment = Payment.Create(initiatedEvent);
        var failedAt = DateTimeOffset.UtcNow;
        var failedEvent = new PaymentFailed(payment.Id, failureReason, isRetriable, failedAt);

        // Act: Apply the failed event
        var updatedPayment = payment.Apply(failedEvent);

        // Assert: Verify payment state
        // 3.1: Status is Failed
        var statusIsFailed = updatedPayment.Status == PaymentStatus.Failed;

        // 3.2: Failure reason is recorded
        var reasonRecorded = updatedPayment.FailureReason == failureReason;

        // 3.3: Failure timestamp is recorded
        var timestampRecorded = updatedPayment.ProcessedAt == failedAt;

        // IsRetriable flag is recorded
        var retriableFlagRecorded = updatedPayment.IsRetriable == isRetriable;

        return statusIsFailed
            && reasonRecorded
            && timestampRecorded
            && retriableFlagRecorded;
    }
}


/// <summary>
/// Arbitrary that generates valid PaymentInitiated events and transaction IDs for capture tests.
/// </summary>
public static class ValidPaymentWithSuccessArbitrary
{
    public static Arbitrary<PaymentInitiated> PaymentInitiated()
    {
        var eventGen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(paymentId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(orderId => ArbMap.Default.GeneratorFor<Guid>()
                    .Where(g => g != Guid.Empty)
                    .SelectMany(customerId => Gen.Choose(100, 1000000)
                        .Select(cents => (decimal)cents / 100)
                        .SelectMany(amount => Gen.Elements("USD", "EUR", "GBP")
                            .SelectMany(currency => Gen.Elements("tok_success_visa", "tok_success_mc")
                                .Select(token => new Payments.Processing.PaymentInitiated(
                                    paymentId,
                                    orderId,
                                    customerId,
                                    amount,
                                    currency,
                                    token,
                                    DateTimeOffset.UtcNow)))))));

        return eventGen.ToArbitrary();
    }

    public static Arbitrary<string> String()
    {
        var transactionIdGen = Gen.Choose(100000, 999999)
            .Select(n => $"txn_{n}");

        return transactionIdGen.ToArbitrary();
    }
}

/// <summary>
/// Arbitrary that generates failure data for failed capture tests.
/// </summary>
public static class FailureDataArbitrary
{
    public static Arbitrary<PaymentInitiated> PaymentInitiated()
    {
        var eventGen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(paymentId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(orderId => ArbMap.Default.GeneratorFor<Guid>()
                    .Where(g => g != Guid.Empty)
                    .SelectMany(customerId => Gen.Choose(100, 1000000)
                        .Select(cents => (decimal)cents / 100)
                        .SelectMany(amount => Gen.Elements("USD", "EUR", "GBP")
                            .SelectMany(currency => Gen.Elements("tok_decline_visa", "tok_timeout_mc")
                                .Select(token => new Payments.Processing.PaymentInitiated(
                                    paymentId,
                                    orderId,
                                    customerId,
                                    amount,
                                    currency,
                                    token,
                                    DateTimeOffset.UtcNow)))))));

        return eventGen.ToArbitrary();
    }

    public static Arbitrary<string> String()
    {
        var reasonGen = Gen.Elements(
            "card_declined",
            "insufficient_funds",
            "expired_card",
            "gateway_timeout",
            "network_error",
            "invalid_card");

        return reasonGen.ToArbitrary();
    }

    public static Arbitrary<bool> Bool() => ArbMap.Default.ArbFor<bool>();
}
