using FluentValidation.TestHelper;
using Orders.Placement;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for CheckoutCompletedValidator edge case validations.
/// </summary>
public class CheckoutCompletedValidatorTests
{
    private readonly CheckoutCompletedValidator _validator = new();

    private static ShippingAddress ValidShippingAddress => new(
        "123 Main St",
        null,
        "Seattle",
        "WA",
        "98101",
        "USA");

    private static CheckoutLineItem ValidLineItem => new(
        "SKU-001",
        2,
        19.99m);

    /// <summary>
    /// Test empty line items list (Requirement 3.1)
    /// WHEN CheckoutCompleted contains zero line items 
    /// THEN the system SHALL reject the order creation and return a validation error
    /// </summary>
    [Fact]
    public void Validate_EmptyLineItems_ReturnsValidationError()
    {
        // Arrange
        var checkout = new CheckoutCompleted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [],
            ValidShippingAddress,
            "Standard",
            "tok_visa",
            null,
            DateTimeOffset.UtcNow);

        // Act
        var result = _validator.TestValidate(checkout);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.LineItems)
            .WithErrorMessage("Order must contain at least one line item");
    }

    /// <summary>
    /// Test missing customer identifier (Requirement 3.4)
    /// WHEN CheckoutCompleted is missing a customer identifier 
    /// THEN the system SHALL reject the order creation and return a validation error
    /// </summary>
    [Fact]
    public void Validate_EmptyCustomerId_ReturnsValidationError()
    {
        // Arrange
        var checkout = new CheckoutCompleted(
            Guid.NewGuid(),
            Guid.Empty,
            [ValidLineItem],
            ValidShippingAddress,
            "Standard",
            "tok_visa",
            null,
            DateTimeOffset.UtcNow);

        // Act
        var result = _validator.TestValidate(checkout);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.CustomerId)
            .WithErrorMessage("Customer identifier is required");
    }

    /// <summary>
    /// Test missing shipping address (Requirement 3.5)
    /// WHEN CheckoutCompleted is missing a shipping address 
    /// THEN the system SHALL reject the order creation and return a validation error
    /// </summary>
    [Fact]
    public void Validate_NullShippingAddress_ReturnsValidationError()
    {
        // Arrange
        var checkout = new CheckoutCompleted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [ValidLineItem],
            null!,
            "Standard",
            "tok_visa",
            null,
            DateTimeOffset.UtcNow);

        // Act
        var result = _validator.TestValidate(checkout);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ShippingAddress)
            .WithErrorMessage("Shipping address is required");
    }

    /// <summary>
    /// Test missing payment method token (Requirement 3.6)
    /// WHEN CheckoutCompleted is missing a payment method token 
    /// THEN the system SHALL reject the order creation and return a validation error
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyPaymentMethodToken_ReturnsValidationError(string? paymentToken)
    {
        // Arrange
        var checkout = new CheckoutCompleted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [ValidLineItem],
            ValidShippingAddress,
            "Standard",
            paymentToken!,
            null,
            DateTimeOffset.UtcNow);

        // Act
        var result = _validator.TestValidate(checkout);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PaymentMethodToken)
            .WithErrorMessage("Payment method token is required");
    }
}
