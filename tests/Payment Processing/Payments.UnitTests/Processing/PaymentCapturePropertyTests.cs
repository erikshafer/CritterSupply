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
    /// **Feature: payment-processing, Property 3: Successful capture updates Payment and publishes event**
    /// 
    /// *For any* Payment where the gateway returns success, the Payment status SHALL be Captured,
    /// the transaction ID SHALL be recorded, and a PaymentCapturedIntegration message SHALL be
    /// published with correct data.
    /// 
    /// **Validates: Requirements 2.2, 2.3, 2.4, 2.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidPaymentWithSuccessArbitrary)])]
    public bool Successful_Capture_Updates_Payment_And_Publishes_Message(
        PaymentRequested command,
        string transactionId)
    {
        // Arrange: Create a payment
        var payment = Payment.Create(command);
        var capturedAt = DateTimeOffset.UtcNow;

        // Act: Capture the payment
        var (updatedPayment, integrationMessage) = payment.Capture(transactionId, capturedAt);

        // Assert: Verify payment state
        // 2.2: Status is Captured
        var statusIsCaptured = updatedPayment.Status == PaymentStatus.Captured;

        // 2.3: Transaction ID is recorded
        var transactionIdRecorded = updatedPayment.TransactionId == transactionId;

        // 2.4: Capture timestamp is recorded
        var timestampRecorded = updatedPayment.ProcessedAt == capturedAt;

        // 2.5: Integration message has correct data
        var messageHasCorrectPaymentId = integrationMessage.PaymentId == payment.Id;
        var messageHasCorrectOrderId = integrationMessage.OrderId == command.OrderId;
        var messageHasCorrectAmount = integrationMessage.Amount == command.Amount;
        var messageHasCorrectTransactionId = integrationMessage.TransactionId == transactionId;
        var messageHasCorrectTimestamp = integrationMessage.CapturedAt == capturedAt;

        // Verify PaymentCaptured domain event is in pending events
        var hasCapturedEvent = updatedPayment.PendingEvents
            .OfType<PaymentCaptured>()
            .Any(e => e.PaymentId == payment.Id && e.TransactionId == transactionId);

        return statusIsCaptured
            && transactionIdRecorded
            && timestampRecorded
            && messageHasCorrectPaymentId
            && messageHasCorrectOrderId
            && messageHasCorrectAmount
            && messageHasCorrectTransactionId
            && messageHasCorrectTimestamp
            && hasCapturedEvent;
    }

    /// <summary>
    /// **Feature: payment-processing, Property 4: Failed capture updates Payment and publishes event with reason**
    /// 
    /// *For any* Payment where the gateway returns failure, the Payment status SHALL be Failed,
    /// the failure reason SHALL be recorded, and a PaymentFailedIntegration message SHALL be
    /// published containing the reason code.
    /// 
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(FailureDataArbitrary)])]
    public bool Failed_Capture_Updates_Payment_And_Publishes_Message_With_Reason(
        PaymentRequested command,
        string failureReason,
        bool isRetriable)
    {
        // Arrange: Create a payment
        var payment = Payment.Create(command);
        var failedAt = DateTimeOffset.UtcNow;

        // Act: Fail the payment
        var (updatedPayment, integrationMessage) = payment.Fail(failureReason, isRetriable, failedAt);

        // Assert: Verify payment state
        // 3.1: Status is Failed
        var statusIsFailed = updatedPayment.Status == PaymentStatus.Failed;

        // 3.2: Failure reason is recorded
        var reasonRecorded = updatedPayment.FailureReason == failureReason;

        // 3.3: Failure timestamp is recorded
        var timestampRecorded = updatedPayment.ProcessedAt == failedAt;

        // IsRetriable flag is recorded
        var retriableFlagRecorded = updatedPayment.IsRetriable == isRetriable;

        // 3.4: Integration message has correct data including reason
        var messageHasCorrectPaymentId = integrationMessage.PaymentId == payment.Id;
        var messageHasCorrectOrderId = integrationMessage.OrderId == command.OrderId;
        var messageHasCorrectReason = integrationMessage.FailureReason == failureReason;
        var messageHasCorrectRetriable = integrationMessage.IsRetriable == isRetriable;
        var messageHasCorrectTimestamp = integrationMessage.FailedAt == failedAt;

        // Verify PaymentFailed domain event is in pending events
        var hasFailedEvent = updatedPayment.PendingEvents
            .OfType<PaymentFailed>()
            .Any(e => e.PaymentId == payment.Id && e.FailureReason == failureReason);

        return statusIsFailed
            && reasonRecorded
            && timestampRecorded
            && retriableFlagRecorded
            && messageHasCorrectPaymentId
            && messageHasCorrectOrderId
            && messageHasCorrectReason
            && messageHasCorrectRetriable
            && messageHasCorrectTimestamp
            && hasFailedEvent;
    }
}


/// <summary>
/// Arbitrary that generates valid PaymentRequested commands and transaction IDs for capture tests.
/// </summary>
public static class ValidPaymentWithSuccessArbitrary
{
    public static Arbitrary<PaymentRequested> PaymentRequested()
    {
        var commandGen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(orderId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(customerId => Gen.Choose(100, 1000000)
                    .Select(cents => (decimal)cents / 100)
                    .SelectMany(amount => Gen.Elements("USD", "EUR", "GBP")
                        .SelectMany(currency => Gen.Elements("tok_success_visa", "tok_success_mc")
                            .Select(token => new Payments.Processing.PaymentRequested(
                                orderId,
                                customerId,
                                amount,
                                currency,
                                token))))));

        return commandGen.ToArbitrary();
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
    public static Arbitrary<PaymentRequested> PaymentRequested()
    {
        var commandGen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(orderId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(customerId => Gen.Choose(100, 1000000)
                    .Select(cents => (decimal)cents / 100)
                    .SelectMany(amount => Gen.Elements("USD", "EUR", "GBP")
                        .SelectMany(currency => Gen.Elements("tok_decline_visa", "tok_timeout_mc")
                            .Select(token => new Payments.Processing.PaymentRequested(
                                orderId,
                                customerId,
                                amount,
                                currency,
                                token))))));

        return commandGen.ToArbitrary();
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
