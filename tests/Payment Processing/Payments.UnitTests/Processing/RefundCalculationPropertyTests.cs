using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Payments.Processing;

namespace Payments.UnitTests.Processing;

/// <summary>
/// Property-based tests for refund amount calculations.
/// Uses FsCheck to generate test cases with arbitrary valid payment and refund amounts.
/// </summary>
public class RefundCalculationPropertyTests
{
    /// <summary>
    /// Property: RefundableAmount should always equal Amount minus TotalRefunded.
    /// </summary>
    [Property(Arbitrary = [typeof(PositiveDecimalGenerator)])]
    public Property RefundableAmount_Always_Equals_Amount_Minus_TotalRefunded(
        PositiveDecimal amount,
        NonNegativeDecimal totalRefunded)
    {
        // Ensure totalRefunded doesn't exceed amount for valid test case
        var validTotalRefunded = Math.Min(totalRefunded.Get, amount.Get);

        var payment = CreatePayment(amount.Get, validTotalRefunded);

        return (payment.RefundableAmount == amount.Get - validTotalRefunded)
            .Label($"RefundableAmount ({payment.RefundableAmount}) should equal Amount ({amount.Get}) - TotalRefunded ({validTotalRefunded})");
    }

    /// <summary>
    /// Property: After a full refund, status should be Refunded and RefundableAmount should be zero.
    /// </summary>
    [Property(Arbitrary = [typeof(PositiveDecimalGenerator)])]
    public Property FullRefund_Sets_Status_To_Refunded_And_RefundableAmount_To_Zero(PositiveDecimal amount)
    {
        // Arrange
        var payment = CreatePayment(amount.Get, 0m);

        // Act
        var (refundedPayment, _, _) = payment.Refund(
            amount.Get,
            $"ref_{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow);

        // Assert
        return (refundedPayment.Status == PaymentStatus.Refunded
                && refundedPayment.RefundableAmount == 0m)
            .Label($"Full refund should set Status to Refunded and RefundableAmount to 0");
    }

    /// <summary>
    /// Property: After a partial refund, status should remain Captured and RefundableAmount should be reduced.
    /// </summary>
    [Property(Arbitrary = [typeof(PositiveDecimalGenerator)])]
    public Property PartialRefund_Keeps_Status_Captured_And_Reduces_RefundableAmount(
        PositiveDecimal amount,
        PositiveDecimal refundAmount)
    {
        // Ensure refund is truly partial (less than total amount)
        var validRefundAmount = Math.Min(refundAmount.Get, amount.Get - 0.01m);
        if (validRefundAmount <= 0) return true.ToProperty(); // Skip invalid case

        // Arrange
        var payment = CreatePayment(amount.Get, 0m);

        // Act
        var (refundedPayment, _, _) = payment.Refund(
            validRefundAmount,
            $"ref_{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow);

        // Assert
        return (refundedPayment.Status == PaymentStatus.Captured
                && refundedPayment.RefundableAmount == amount.Get - validRefundAmount)
            .Label($"Partial refund should keep Status as Captured and reduce RefundableAmount");
    }

    /// <summary>
    /// Property: Multiple partial refunds should accumulate correctly.
    /// </summary>
    [Property(Arbitrary = [typeof(PositiveDecimalGenerator)])]
    public Property MultiplePartialRefunds_Accumulate_Correctly(
        PositiveDecimal amount,
        PositiveDecimal firstRefund,
        PositiveDecimal secondRefund)
    {
        // Ensure refunds don't exceed amount
        var validFirstRefund = Math.Min(firstRefund.Get, amount.Get * 0.4m);
        var validSecondRefund = Math.Min(secondRefund.Get, amount.Get - validFirstRefund - 0.01m);

        if (validFirstRefund <= 0 || validSecondRefund <= 0)
            return true.ToProperty(); // Skip invalid case

        // Arrange
        var payment = CreatePayment(amount.Get, 0m);

        // Act: Apply first refund
        var (afterFirst, _, _) = payment.Refund(
            validFirstRefund,
            $"ref_{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow);

        // Act: Apply second refund
        var (afterSecond, _, _) = afterFirst.Refund(
            validSecondRefund,
            $"ref_{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow);

        // Assert
        var expectedTotal = validFirstRefund + validSecondRefund;
        return (afterSecond.TotalRefunded == expectedTotal
                && afterSecond.RefundableAmount == amount.Get - expectedTotal)
            .Label($"Multiple refunds should accumulate: TotalRefunded={afterSecond.TotalRefunded}, Expected={expectedTotal}");
    }

    /// <summary>
    /// Property: TotalRefunded should never exceed Amount.
    /// This is an invariant that the domain model should enforce.
    /// </summary>
    [Property(Arbitrary = [typeof(PositiveDecimalGenerator)])]
    public Property TotalRefunded_Never_Exceeds_Amount(
        PositiveDecimal amount,
        NonNegativeDecimal totalRefunded)
    {
        var validTotalRefunded = Math.Min(totalRefunded.Get, amount.Get);
        var payment = CreatePayment(amount.Get, validTotalRefunded);

        return (payment.TotalRefunded <= payment.Amount)
            .Label($"TotalRefunded ({payment.TotalRefunded}) should never exceed Amount ({payment.Amount})");
    }

    /// <summary>
    /// Property: Refunding exactly the RefundableAmount should result in Status=Refunded.
    /// </summary>
    [Property(Arbitrary = [typeof(PositiveDecimalGenerator)])]
    public Property RefundingExactRefundableAmount_Sets_Status_To_Refunded(
        PositiveDecimal amount,
        NonNegativeDecimal existingRefunded)
    {
        var validExistingRefunded = Math.Min(existingRefunded.Get, amount.Get - 0.01m);
        if (validExistingRefunded < 0) return true.ToProperty(); // Skip invalid case

        // Arrange: Payment with some already refunded
        var payment = CreatePayment(amount.Get, validExistingRefunded);
        var remainingRefundable = payment.RefundableAmount;

        if (remainingRefundable <= 0) return true.ToProperty(); // Skip if fully refunded

        // Act: Refund exactly the remaining refundable amount
        var (refundedPayment, _, _) = payment.Refund(
            remainingRefundable,
            $"ref_{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow);

        // Assert
        return (refundedPayment.Status == PaymentStatus.Refunded
                && refundedPayment.RefundableAmount == 0m
                && refundedPayment.TotalRefunded == amount.Get)
            .Label($"Refunding exact refundable amount should set Status to Refunded");
    }

    /// <summary>
    /// Helper: Creates a Payment in Captured status with specified amount and total refunded.
    /// </summary>
    private static Payment CreatePayment(decimal amount, decimal totalRefunded)
    {
        return new Payment(
            Id: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            Amount: amount,
            Currency: "USD",
            PaymentMethodToken: "tok_test",
            Status: PaymentStatus.Captured,
            TransactionId: $"txn_{Guid.NewGuid():N}",
            AuthorizationId: null,
            AuthorizationExpiresAt: null,
            FailureReason: null,
            IsRetriable: false,
            InitiatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            ProcessedAt: DateTimeOffset.UtcNow.AddMinutes(-4),
            TotalRefunded: totalRefunded);
    }
}

/// <summary>
/// FsCheck generator for positive decimal values (> 0).
/// </summary>
public static class PositiveDecimalGenerator
{
    public static Arbitrary<PositiveDecimal> PositiveDecimal()
    {
        var gen = Gen.Choose(100, 10000000) // 1.00 to 100,000.00 in cents
            .Select(cents => new PositiveDecimal((decimal)cents / 100));
        return gen.ToArbitrary();
    }

    public static Arbitrary<NonNegativeDecimal> NonNegativeDecimal()
    {
        var gen = Gen.Choose(0, 10000000) // 0.00 to 100,000.00 in cents
            .Select(cents => new NonNegativeDecimal((decimal)cents / 100));
        return gen.ToArbitrary();
    }
}

/// <summary>
/// Wrapper for positive decimal values.
/// </summary>
public sealed record PositiveDecimal(decimal Get)
{
    public override string ToString() => Get.ToString("F2");
}

/// <summary>
/// Wrapper for non-negative decimal values (includes zero).
/// </summary>
public sealed record NonNegativeDecimal(decimal Get)
{
    public override string ToString() => Get.ToString("F2");
}
