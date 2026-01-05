using Payments.Processing;
using Shouldly;

namespace Payments.UnitTests.Processing;

/// <summary>
/// Unit tests for PaymentRequestedValidator edge cases.
/// </summary>
public class PaymentRequestedValidatorTests
{
    private readonly PaymentRequested.PaymentRequestedValidator _validator = new();

    /// <summary>
    /// Requirement 4.2: Missing order identifier should fail validation.
    /// </summary>
    [Fact]
    public void Validation_Fails_When_OrderId_Is_Empty()
    {
        // Arrange
        var command = new PaymentRequested(
            OrderId: Guid.Empty,
            CustomerId: Guid.NewGuid(),
            Amount: 100.00m,
            Currency: "USD",
            PaymentMethodToken: "tok_visa");

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(PaymentRequested.OrderId));
    }

    /// <summary>
    /// Requirement 4.3: Missing payment method token should fail validation.
    /// </summary>
    [Fact]
    public void Validation_Fails_When_PaymentMethodToken_Is_Empty()
    {
        // Arrange
        var command = new PaymentRequested(
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            Amount: 100.00m,
            Currency: "USD",
            PaymentMethodToken: "");

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(PaymentRequested.PaymentMethodToken));
    }

    /// <summary>
    /// Requirement 4.4: Missing currency should fail validation.
    /// </summary>
    [Fact]
    public void Validation_Fails_When_Currency_Is_Empty()
    {
        // Arrange
        var command = new PaymentRequested(
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            Amount: 100.00m,
            Currency: "",
            PaymentMethodToken: "tok_visa");

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(PaymentRequested.Currency));
    }

    /// <summary>
    /// Missing customer identifier should fail validation.
    /// </summary>
    [Fact]
    public void Validation_Fails_When_CustomerId_Is_Empty()
    {
        // Arrange
        var command = new PaymentRequested(
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.Empty,
            Amount: 100.00m,
            Currency: "USD",
            PaymentMethodToken: "tok_visa");

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(PaymentRequested.CustomerId));
    }

    /// <summary>
    /// Valid command should pass validation.
    /// </summary>
    [Fact]
    public void Validation_Passes_For_Valid_Command()
    {
        // Arrange
        var command = new PaymentRequested(
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            Amount: 100.00m,
            Currency: "USD",
            PaymentMethodToken: "tok_visa");

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }
}
