using Orders.Placement;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for <see cref="CancelOrder.CancelOrderValidator"/>.
/// Verifies that the FluentValidation rules correctly accept valid commands
/// and reject invalid ones with appropriate error messages.
/// </summary>
public class CancelOrderValidatorTests
{
    private static readonly CancelOrder.CancelOrderValidator Validator = new();

    // ---------------------------------------------------------------------------
    // Valid command
    // ---------------------------------------------------------------------------

    /// <summary>A command with a valid non-empty OrderId and non-empty Reason must pass validation.</summary>
    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        var command = new CancelOrder(Guid.NewGuid(), "Customer changed their mind");

        var result = Validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    // ---------------------------------------------------------------------------
    // OrderId validation
    // ---------------------------------------------------------------------------

    /// <summary>An empty Guid for OrderId must fail with the required error message.</summary>
    [Fact]
    public void Validate_EmptyOrderId_FailsWithRequiredMessage()
    {
        var command = new CancelOrder(Guid.Empty, "Some reason");

        var result = Validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.PropertyName == nameof(CancelOrder.OrderId) &&
            e.ErrorMessage == "Order identifier is required");
    }

    // ---------------------------------------------------------------------------
    // Reason validation
    // ---------------------------------------------------------------------------

    /// <summary>An empty Reason string must fail with the required error message.</summary>
    [Fact]
    public void Validate_EmptyReason_FailsWithRequiredMessage()
    {
        var command = new CancelOrder(Guid.NewGuid(), string.Empty);

        var result = Validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.PropertyName == nameof(CancelOrder.Reason) &&
            e.ErrorMessage == "Cancellation reason is required");
    }

    /// <summary>A whitespace-only Reason must fail validation (treated as empty by FluentValidation NotEmpty).</summary>
    [Fact]
    public void Validate_WhitespaceReason_FailsValidation()
    {
        var command = new CancelOrder(Guid.NewGuid(), "   ");

        var result = Validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CancelOrder.Reason));
    }

    // ---------------------------------------------------------------------------
    // Multiple violations
    // ---------------------------------------------------------------------------

    /// <summary>A command with both empty OrderId and empty Reason must produce two validation errors.</summary>
    [Fact]
    public void Validate_EmptyOrderIdAndReason_ProducesTwoErrors()
    {
        var command = new CancelOrder(Guid.Empty, string.Empty);

        var result = Validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.Count.ShouldBe(2);
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CancelOrder.OrderId));
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CancelOrder.Reason));
    }

    // ---------------------------------------------------------------------------
    // Various valid reasons
    // ---------------------------------------------------------------------------

    /// <summary>Various legitimate cancellation reasons must all pass validation.</summary>
    [Theory]
    [InlineData("Customer changed their mind")]
    [InlineData("Duplicate order")]
    [InlineData("Fraud detected")]
    [InlineData("Item no longer available")]
    public void Validate_VariousValidReasons_AllPass(string reason)
    {
        var command = new CancelOrder(Guid.NewGuid(), reason);

        var result = Validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }
}
