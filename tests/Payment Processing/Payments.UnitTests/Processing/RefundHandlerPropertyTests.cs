using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Marten;
using NSubstitute;
using Payments.Processing;
using Shouldly;

namespace Payments.UnitTests.Processing;

/// <summary>
/// Property-based tests for RefundRequestedHandler.
/// </summary>
public class RefundHandlerPropertyTests
{
    /// <summary>
    /// **Feature: payment-processing, Property 7: Successful refund publishes RefundCompleted event**
    /// 
    /// *For any* valid refund request where the gateway returns success,
    /// a RefundCompleted event SHALL be published.
    /// 
    /// **Validates: Requirements 5.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidRefundScenarioArbitrary)])]
    public async Task<bool> Successful_Refund_Publishes_RefundCompleted_Event(
        ValidRefundScenario scenario)
    {
        // Arrange: Set up mocks
        var gateway = Substitute.For<IPaymentGateway>();
        var session = Substitute.For<IDocumentSession>();

        // Configure gateway to return success
        var refundTransactionId = $"ref_{Guid.NewGuid():N}";
        gateway.RefundAsync(
                Arg.Any<string>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>())
            .Returns(new GatewayResult(true, refundTransactionId, null, false));

        // Configure session to return the captured payment
        session.Events.AggregateStreamAsync<Payment>(
                scenario.Command.PaymentId,
                token: Arg.Any<CancellationToken>())
            .Returns(scenario.CapturedPayment);

        // Act: Handle the refund request
        var result = await RefundRequestedHandler.Handle(
            scenario.Command,
            gateway,
            session,
            CancellationToken.None);

        // Assert: Result should be RefundCompleted with correct data
        if (result is not RefundCompleted completed)
            return false;

        var hasCorrectPaymentId = completed.PaymentId == scenario.Command.PaymentId;
        var hasCorrectOrderId = completed.OrderId == scenario.Command.OrderId;
        var hasCorrectAmount = completed.Amount == scenario.Command.Amount;
        var hasTransactionId = !string.IsNullOrEmpty(completed.TransactionId);
        var hasTimestamp = completed.RefundedAt != default;

        return hasCorrectPaymentId
            && hasCorrectOrderId
            && hasCorrectAmount
            && hasTransactionId
            && hasTimestamp;
    }

    /// <summary>
    /// Property test: Refund for non-existent payment returns RefundFailed.
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(NonExistentPaymentRefundArbitrary)])]
    public async Task<bool> Refund_For_NonExistent_Payment_Returns_RefundFailed(
        RefundRequested command)
    {
        // Arrange: Set up mocks
        var gateway = Substitute.For<IPaymentGateway>();
        var session = Substitute.For<IDocumentSession>();

        // Configure session to return null (payment not found)
        session.Events.AggregateStreamAsync<Payment>(
                command.PaymentId,
                token: Arg.Any<CancellationToken>())
            .Returns((Payment?)null);

        // Act: Handle the refund request
        var result = await RefundRequestedHandler.Handle(
            command,
            gateway,
            session,
            CancellationToken.None);

        // Assert: Result should be RefundFailed
        if (result is not RefundFailed failed)
            return false;

        var hasCorrectPaymentId = failed.PaymentId == command.PaymentId;
        var hasCorrectOrderId = failed.OrderId == command.OrderId;
        var hasFailureReason = !string.IsNullOrEmpty(failed.FailureReason);

        return hasCorrectPaymentId && hasCorrectOrderId && hasFailureReason;
    }

    /// <summary>
    /// Property test: Refund amount exceeding captured amount returns RefundFailed.
    /// **Validates: Requirements 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ExcessiveRefundAmountArbitrary)])]
    public async Task<bool> Refund_Exceeding_Captured_Amount_Returns_RefundFailed(
        ExcessiveRefundScenario scenario)
    {
        // Arrange: Set up mocks
        var gateway = Substitute.For<IPaymentGateway>();
        var session = Substitute.For<IDocumentSession>();

        // Configure session to return the captured payment
        session.Events.AggregateStreamAsync<Payment>(
                scenario.Command.PaymentId,
                token: Arg.Any<CancellationToken>())
            .Returns(scenario.CapturedPayment);

        // Act: Handle the refund request
        var result = await RefundRequestedHandler.Handle(
            scenario.Command,
            gateway,
            session,
            CancellationToken.None);

        // Assert: Result should be RefundFailed with amount exceeded message
        if (result is not RefundFailed failed)
            return false;

        var hasCorrectPaymentId = failed.PaymentId == scenario.Command.PaymentId;
        var hasCorrectOrderId = failed.OrderId == scenario.Command.OrderId;
        var mentionsExceeded = failed.FailureReason.Contains("exceeds", StringComparison.OrdinalIgnoreCase);

        return hasCorrectPaymentId && hasCorrectOrderId && mentionsExceeded;
    }
}

/// <summary>
/// Represents a valid refund scenario with a captured payment and valid refund command.
/// </summary>
public record ValidRefundScenario(Payment CapturedPayment, RefundRequested Command);

/// <summary>
/// Represents a scenario where refund amount exceeds captured amount.
/// </summary>
public record ExcessiveRefundScenario(Payment CapturedPayment, RefundRequested Command);

/// <summary>
/// Arbitrary that generates valid refund scenarios.
/// </summary>
public static class ValidRefundScenarioArbitrary
{
    public static Arbitrary<ValidRefundScenario> ValidRefundScenario()
    {
        var gen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(paymentId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(orderId => ArbMap.Default.GeneratorFor<Guid>()
                    .Where(g => g != Guid.Empty)
                    .SelectMany(customerId => Gen.Choose(1000, 100000)
                        .Select(cents => (decimal)cents / 100)
                        .SelectMany(capturedAmount => Gen.Choose(100, (int)(capturedAmount * 100))
                            .Select(refundCents => (decimal)refundCents / 100)
                            .SelectMany(refundAmount => Gen.Elements("USD", "EUR", "GBP")
                                .SelectMany(currency => Gen.Elements("tok_success_visa", "tok_success_mc")
                                    .Select(token =>
                                    {
                                        var payment = new Payment(
                                            paymentId,
                                            orderId,
                                            customerId,
                                            capturedAmount,
                                            currency,
                                            token,
                                            PaymentStatus.Captured,
                                            $"txn_{Guid.NewGuid():N}",
                                            null,
                                            false,
                                            DateTimeOffset.UtcNow.AddMinutes(-5),
                                            DateTimeOffset.UtcNow.AddMinutes(-1),
                                            0m); // TotalRefunded

                                        var command = new RefundRequested(paymentId, orderId, refundAmount);

                                        return new ValidRefundScenario(payment, command);
                                    })))))));

        return gen.ToArbitrary();
    }
}

/// <summary>
/// Arbitrary that generates refund requests for non-existent payments.
/// </summary>
public static class NonExistentPaymentRefundArbitrary
{
    public static Arbitrary<RefundRequested> RefundRequested()
    {
        var gen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(paymentId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(orderId => Gen.Choose(100, 10000)
                    .Select(cents => (decimal)cents / 100)
                    .Select(amount => new Payments.Processing.RefundRequested(paymentId, orderId, amount))));

        return gen.ToArbitrary();
    }
}

/// <summary>
/// Arbitrary that generates scenarios where refund amount exceeds captured amount.
/// </summary>
public static class ExcessiveRefundAmountArbitrary
{
    public static Arbitrary<ExcessiveRefundScenario> ExcessiveRefundScenario()
    {
        var gen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(paymentId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(orderId => ArbMap.Default.GeneratorFor<Guid>()
                    .Where(g => g != Guid.Empty)
                    .SelectMany(customerId => Gen.Choose(1000, 50000)
                        .Select(cents => (decimal)cents / 100)
                        .SelectMany(capturedAmount => Gen.Choose((int)(capturedAmount * 100) + 1, (int)(capturedAmount * 100) + 10000)
                            .Select(refundCents => (decimal)refundCents / 100)
                            .SelectMany(refundAmount => Gen.Elements("USD", "EUR", "GBP")
                                .SelectMany(currency => Gen.Elements("tok_success_visa", "tok_success_mc")
                                    .Select(token =>
                                    {
                                        var payment = new Payment(
                                            paymentId,
                                            orderId,
                                            customerId,
                                            capturedAmount,
                                            currency,
                                            token,
                                            PaymentStatus.Captured,
                                            $"txn_{Guid.NewGuid():N}",
                                            null,
                                            false,
                                            DateTimeOffset.UtcNow.AddMinutes(-5),
                                            DateTimeOffset.UtcNow.AddMinutes(-1),
                                            0m); // TotalRefunded

                                        var command = new RefundRequested(paymentId, orderId, refundAmount);

                                        return new ExcessiveRefundScenario(payment, command);
                                    })))))));

        return gen.ToArbitrary();
    }
}
