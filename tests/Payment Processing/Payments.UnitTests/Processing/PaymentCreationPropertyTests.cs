using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Payments.Processing;
using Shouldly;

namespace Payments.UnitTests.Processing;

/// <summary>
/// Property-based tests for Payment aggregate creation.
/// </summary>
public class PaymentCreationPropertyTests
{
    /// <summary>
    /// **Feature: payment-processing, Property 1: Payment creation produces valid Payment with Pending status**
    /// 
    /// *For any* valid PaymentRequested command, creating a Payment SHALL produce a Payment with
    /// status Pending, a unique identifier, and an initiation timestamp.
    /// 
    /// **Validates: Requirements 1.1, 1.3, 1.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidPaymentRequestedArbitrary)])]
    public bool Payment_Creation_Produces_Valid_Payment_With_Pending_Status(PaymentRequested command)
    {
        // Act: Create payment from valid command
        var payment = Payment.Create(command);

        // Assert: Verify payment has required properties
        // 1.1: Unique identifier (non-empty GUID)
        var hasValidId = payment.Id != Guid.Empty;

        // 1.3: Status is Pending
        var hasPendingStatus = payment.Status == PaymentStatus.Pending;

        // 1.4: Initiation timestamp is recorded and reasonable
        var hasValidTimestamp = payment.InitiatedAt != default
            && payment.InitiatedAt <= DateTimeOffset.UtcNow
            && payment.InitiatedAt > DateTimeOffset.UtcNow.AddMinutes(-1);

        // Verify pending events contain PaymentInitiated
        var hasInitiatedEvent = payment.PendingEvents.Count == 1
            && payment.PendingEvents[0] is PaymentInitiated initiated
            && initiated.PaymentId == payment.Id;

        return hasValidId && hasPendingStatus && hasValidTimestamp && hasInitiatedEvent;
    }

    /// <summary>
    /// **Feature: payment-processing, Property 2: Payment preserves all PaymentRequested data**
    /// 
    /// *For any* valid PaymentRequested command, the resulting Payment SHALL contain the order
    /// identifier, customer identifier, amount, currency, and payment method token from the
    /// original command.
    /// 
    /// **Validates: Requirements 1.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidPaymentRequestedArbitrary)])]
    public bool Payment_Preserves_All_PaymentRequested_Data(PaymentRequested command)
    {
        // Act: Create payment from valid command
        var payment = Payment.Create(command);

        // Assert: Verify all data is preserved from the command
        // 1.2: Order identifier
        var orderIdPreserved = payment.OrderId == command.OrderId;

        // 1.2: Customer identifier
        var customerIdPreserved = payment.CustomerId == command.CustomerId;

        // 1.2: Amount
        var amountPreserved = payment.Amount == command.Amount;

        // 1.2: Currency
        var currencyPreserved = payment.Currency == command.Currency;

        // 1.2: Payment method token
        var tokenPreserved = payment.PaymentMethodToken == command.PaymentMethodToken;

        // Also verify the PaymentInitiated event contains the same data
        var initiatedEvent = payment.PendingEvents[0] as PaymentInitiated;
        var eventDataPreserved = initiatedEvent != null
            && initiatedEvent.OrderId == command.OrderId
            && initiatedEvent.CustomerId == command.CustomerId
            && initiatedEvent.Amount == command.Amount
            && initiatedEvent.Currency == command.Currency
            && initiatedEvent.PaymentMethodToken == command.PaymentMethodToken;

        return orderIdPreserved
            && customerIdPreserved
            && amountPreserved
            && currencyPreserved
            && tokenPreserved
            && eventDataPreserved;
    }
}


/// <summary>
/// Arbitrary that generates valid PaymentRequested commands for property tests.
/// </summary>
public static class ValidPaymentRequestedArbitrary
{
    // Generator for printable ASCII strings (no control characters)
    private static Gen<string> PrintableStringGen(int minLength = 1, int maxLength = 20) =>
        Gen.Choose(minLength, maxLength)
            .SelectMany(length => Gen.Choose(32, 126).Select(c => (char)c).ArrayOf(length))
            .Select(chars => new string(chars))
            .Where(s => !string.IsNullOrWhiteSpace(s));

    public static Arbitrary<PaymentRequested> PaymentRequested()
    {
        var commandGen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(orderId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(customerId => Gen.Choose(100, 1000000)
                    .Select(cents => (decimal)cents / 100) // Amount between 1.00 and 10000.00
                    .SelectMany(amount => Gen.Elements("USD", "EUR", "GBP", "CAD")
                        .SelectMany(currency => Gen.Elements("tok_visa", "tok_mastercard", "tok_amex", "tok_success_test")
                            .Select(token => new Payments.Processing.PaymentRequested(
                                orderId,
                                customerId,
                                amount,
                                currency,
                                token))))));

        return commandGen.ToArbitrary();
    }
}
