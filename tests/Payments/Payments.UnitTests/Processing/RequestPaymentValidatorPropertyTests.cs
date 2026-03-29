using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Payments.Processing;

namespace Payments.UnitTests.Processing;

/// <summary>
/// Property-based tests for RequestPayment validation.
/// </summary>
public class RequestPaymentValidatorPropertyTests
{
    private readonly RequestPayment.RequestPaymentValidator _validator = new();

    /// <summary>
    /// **Feature: payment-processing, Property 5: Validation rejects invalid payment amounts**
    ///
    /// *For any* RequestPayment command with amount less than or equal to zero,
    /// validation SHALL fail and payment processing SHALL be rejected.
    ///
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(InvalidAmountArbitrary)])]
    public bool Validation_Rejects_Invalid_Payment_Amounts(RequestPayment command)
    {
        // Act: Validate the command with invalid amount
        var result = _validator.Validate(command);

        // Assert: Validation should fail with amount error
        var isInvalid = !result.IsValid;
        var hasAmountError = result.Errors.Any(e =>
            e.PropertyName == nameof(RequestPayment.Amount));

        return isInvalid && hasAmountError;
    }
}

/// <summary>
/// Arbitrary that generates RequestPayment commands with invalid amounts (zero or negative).
/// </summary>
public static class InvalidAmountArbitrary
{
    public static Arbitrary<RequestPayment> RequestPayment()
    {
        // Generate amounts that are zero or negative
        var invalidAmountGen = Gen.OneOf(
            Gen.Constant(0m),
            Gen.Choose(-100000, -1).Select(cents => (decimal)cents / 100)
        );

        var commandGen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(orderId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(customerId => invalidAmountGen
                    .SelectMany(amount => Gen.Elements("USD", "EUR", "GBP")
                        .SelectMany(currency => Gen.Elements("tok_visa", "tok_mastercard")
                            .Select(token => new Payments.Processing.RequestPayment(
                                orderId,
                                customerId,
                                amount,
                                currency,
                                token))))));

        return commandGen.ToArbitrary();
    }
}
