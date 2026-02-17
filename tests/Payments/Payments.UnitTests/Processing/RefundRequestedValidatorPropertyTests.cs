using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Payments.Processing;

namespace Payments.UnitTests.Processing;

/// <summary>
/// Property-based tests for RefundRequested validation.
/// </summary>
public class RefundRequestedValidatorPropertyTests
{
    private readonly RefundRequested.RefundRequestedValidator _validator = new();

    /// <summary>
    /// **Feature: payment-processing, Property 6: Refund validation rejects invalid requests**
    ///
    /// *For any* RefundRequested command where the PaymentId is empty or the amount is less than
    /// or equal to zero, the refund SHALL be rejected.
    ///
    /// **Validates: Requirements 5.1, 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(InvalidRefundRequestArbitrary)])]
    public bool Validation_Rejects_Invalid_Refund_Requests(RefundRequested command)
    {
        // Act: Validate the command
        var result = _validator.Validate(command);

        // Assert: Validation should fail
        return !result.IsValid;
    }

    /// <summary>
    /// Property test: Empty PaymentId should be rejected.
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(EmptyPaymentIdRefundArbitrary)])]
    public bool Validation_Rejects_Empty_PaymentId(RefundRequested command)
    {
        // Act: Validate the command with empty PaymentId
        var result = _validator.Validate(command);

        // Assert: Validation should fail with PaymentId error
        var isInvalid = !result.IsValid;
        var hasPaymentIdError = result.Errors.Any(e =>
            e.PropertyName == nameof(RefundRequested.PaymentId));

        return isInvalid && hasPaymentIdError;
    }

    /// <summary>
    /// Property test: Invalid amounts (zero or negative) should be rejected.
    /// **Validates: Requirements 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(InvalidAmountRefundArbitrary)])]
    public bool Validation_Rejects_Invalid_Refund_Amounts(RefundRequested command)
    {
        // Act: Validate the command with invalid amount
        var result = _validator.Validate(command);

        // Assert: Validation should fail with amount error
        var isInvalid = !result.IsValid;
        var hasAmountError = result.Errors.Any(e =>
            e.PropertyName == nameof(RefundRequested.Amount));

        return isInvalid && hasAmountError;
    }
}

/// <summary>
/// Arbitrary that generates RefundRequested commands with invalid data (empty PaymentId or invalid amount).
/// </summary>
public static class InvalidRefundRequestArbitrary
{
    public static Arbitrary<RefundRequested> RefundRequested()
    {
        // Generate either empty PaymentId or invalid amount
        var emptyPaymentIdGen = Gen.Constant(Guid.Empty)
            .SelectMany(paymentId => ArbMap.Default.GeneratorFor<Guid>()
                .SelectMany(orderId => Gen.Choose(1, 100000).Select(cents => (decimal)cents / 100)
                    .Select(amount => new Payments.Processing.RefundRequested(paymentId, orderId, amount))));

        var invalidAmountGen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(paymentId => ArbMap.Default.GeneratorFor<Guid>()
                .SelectMany(orderId => Gen.OneOf(
                        Gen.Constant(0m),
                        Gen.Choose(-100000, -1).Select(cents => (decimal)cents / 100))
                    .Select(amount => new Payments.Processing.RefundRequested(paymentId, orderId, amount))));

        return Gen.OneOf(emptyPaymentIdGen, invalidAmountGen).ToArbitrary();
    }
}

/// <summary>
/// Arbitrary that generates RefundRequested commands with empty PaymentId.
/// </summary>
public static class EmptyPaymentIdRefundArbitrary
{
    public static Arbitrary<RefundRequested> RefundRequested()
    {
        var gen = ArbMap.Default.GeneratorFor<Guid>()
            .SelectMany(orderId => Gen.Choose(1, 100000).Select(cents => (decimal)cents / 100)
                .Select(amount => new Payments.Processing.RefundRequested(Guid.Empty, orderId, amount)));

        return gen.ToArbitrary();
    }
}

/// <summary>
/// Arbitrary that generates RefundRequested commands with invalid amounts (zero or negative).
/// </summary>
public static class InvalidAmountRefundArbitrary
{
    public static Arbitrary<RefundRequested> RefundRequested()
    {
        var invalidAmountGen = Gen.OneOf(
            Gen.Constant(0m),
            Gen.Choose(-100000, -1).Select(cents => (decimal)cents / 100)
        );

        var gen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(paymentId => ArbMap.Default.GeneratorFor<Guid>()
                .SelectMany(orderId => invalidAmountGen
                    .Select(amount => new Payments.Processing.RefundRequested(paymentId, orderId, amount))));

        return gen.ToArbitrary();
    }
}
