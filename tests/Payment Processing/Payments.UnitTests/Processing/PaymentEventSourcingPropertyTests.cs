using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Payments.Processing;

namespace Payments.UnitTests.Processing;

/// <summary>
/// Property-based tests for Payment event sourcing reconstruction.
/// </summary>
public class PaymentEventSourcingPropertyTests
{
    /// <summary>
    /// **Feature: payment-processing, Property 8: Event sourcing state reconstruction**
    /// 
    /// *For any* Payment with persisted events, aggregating the event stream SHALL produce
    /// a Payment with state equivalent to the original Payment at the time of the last event.
    /// 
    /// **Validates: Requirements 6.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(EventSourcingArbitrary)])]
    public bool Event_Sourcing_Reconstruction_Produces_Equivalent_State(
        PaymentRequested command,
        PaymentOutcome outcome)
    {
        // Arrange: Create a payment and apply an outcome
        var payment = Payment.Create(command);
        var processedAt = DateTimeOffset.UtcNow;

        // Get the final payment state based on outcome
        Payment finalPayment;
        List<object> eventsToApply;

        switch (outcome)
        {
            case PaymentOutcome.Captured:
                var transactionId = $"txn_{Guid.NewGuid():N}";
                var (capturedPayment, _) = payment.Capture(transactionId, processedAt);
                finalPayment = capturedPayment;
                eventsToApply = capturedPayment.PendingEvents.ToList();
                break;

            case PaymentOutcome.Failed:
                var (failedPayment, _) = payment.Fail("card_declined", false, processedAt);
                finalPayment = failedPayment;
                eventsToApply = failedPayment.PendingEvents.ToList();
                break;

            case PaymentOutcome.FailedRetriable:
                var (retriablePayment, _) = payment.Fail("gateway_timeout", true, processedAt);
                finalPayment = retriablePayment;
                eventsToApply = retriablePayment.PendingEvents.ToList();
                break;

            default: // PendingOnly
                finalPayment = payment;
                eventsToApply = payment.PendingEvents.ToList();
                break;
        }

        // Act: Reconstruct payment from events (simulating Marten's event sourcing)
        Payment? reconstructedPayment = null;
        foreach (var @event in eventsToApply)
        {
            reconstructedPayment = @event switch
            {
                PaymentInitiated initiated => Payment.Create(initiated),
                PaymentCaptured captured when reconstructedPayment != null => reconstructedPayment.Apply(captured),
                PaymentFailed failed when reconstructedPayment != null => reconstructedPayment.Apply(failed),
                _ => reconstructedPayment
            };
        }

        // Assert: Verify reconstructed state matches original
        if (reconstructedPayment == null)
            return false;

        // Compare all relevant properties (excluding PendingEvents which is transient)
        var idMatches = reconstructedPayment.Id == finalPayment.Id;
        var orderIdMatches = reconstructedPayment.OrderId == finalPayment.OrderId;
        var customerIdMatches = reconstructedPayment.CustomerId == finalPayment.CustomerId;
        var amountMatches = reconstructedPayment.Amount == finalPayment.Amount;
        var currencyMatches = reconstructedPayment.Currency == finalPayment.Currency;
        var tokenMatches = reconstructedPayment.PaymentMethodToken == finalPayment.PaymentMethodToken;
        var statusMatches = reconstructedPayment.Status == finalPayment.Status;
        var transactionIdMatches = reconstructedPayment.TransactionId == finalPayment.TransactionId;
        var failureReasonMatches = reconstructedPayment.FailureReason == finalPayment.FailureReason;
        var isRetriableMatches = reconstructedPayment.IsRetriable == finalPayment.IsRetriable;
        var initiatedAtMatches = reconstructedPayment.InitiatedAt == finalPayment.InitiatedAt;
        var processedAtMatches = reconstructedPayment.ProcessedAt == finalPayment.ProcessedAt;

        return idMatches
            && orderIdMatches
            && customerIdMatches
            && amountMatches
            && currencyMatches
            && tokenMatches
            && statusMatches
            && transactionIdMatches
            && failureReasonMatches
            && isRetriableMatches
            && initiatedAtMatches
            && processedAtMatches;
    }
}

/// <summary>
/// Represents possible payment outcomes for event sourcing tests.
/// </summary>
public enum PaymentOutcome
{
    PendingOnly,
    Captured,
    Failed,
    FailedRetriable
}

/// <summary>
/// Arbitrary that generates data for event sourcing reconstruction tests.
/// </summary>
public static class EventSourcingArbitrary
{
    public static Arbitrary<PaymentRequested> PaymentRequested()
    {
        var commandGen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(orderId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(customerId => Gen.Choose(100, 1000000)
                    .Select(cents => (decimal)cents / 100)
                    .SelectMany(amount => Gen.Elements("USD", "EUR", "GBP", "CAD")
                        .SelectMany(currency => Gen.Elements("tok_visa", "tok_mastercard", "tok_amex")
                            .Select(token => new Payments.Processing.PaymentRequested(
                                orderId,
                                customerId,
                                amount,
                                currency,
                                token))))));

        return commandGen.ToArbitrary();
    }

    public static Arbitrary<PaymentOutcome> PaymentOutcome()
    {
        var outcomeGen = Gen.Elements(
            Processing.PaymentOutcome.PendingOnly,
            Processing.PaymentOutcome.Captured,
            Processing.PaymentOutcome.Failed,
            Processing.PaymentOutcome.FailedRetriable);

        return outcomeGen.ToArbitrary();
    }
}
