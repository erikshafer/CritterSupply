using FluentValidation.TestHelper;
using Pricing.Products;
using Xunit;

namespace Pricing.UnitTests;

public sealed class ChangePriceValidatorTests
{
    private readonly ChangePriceValidator _validator = new();
    private readonly DateTimeOffset _testTime = new(2026, 3, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validate_WithValidCommand_PassesValidation()
    {
        var command = new ChangePrice(
            Sku: "DOG-FOOD-5LB",
            NewAmount: 24.99m,
            Currency: "USD",
            Reason: null,
            ChangedBy: Guid.NewGuid(),
            ChangedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptySku_FailsValidation()
    {
        var command = new ChangePrice(
            Sku: "",
            NewAmount: 24.99m,
            Currency: "USD",
            Reason: null,
            ChangedBy: Guid.NewGuid(),
            ChangedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Fact]
    public void Validate_WithZeroAmount_FailsValidation()
    {
        var command = new ChangePrice(
            Sku: "DOG-FOOD-5LB",
            NewAmount: 0m,
            Currency: "USD",
            Reason: null,
            ChangedBy: Guid.NewGuid(),
            ChangedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.NewAmount);
    }

    [Fact]
    public void Validate_WithNegativeAmount_FailsValidation()
    {
        var command = new ChangePrice(
            Sku: "DOG-FOOD-5LB",
            NewAmount: -10m,
            Currency: "USD",
            Reason: null,
            ChangedBy: Guid.NewGuid(),
            ChangedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.NewAmount);
    }

    [Fact]
    public void Validate_WithInvalidCurrency_FailsValidation()
    {
        var command = new ChangePrice(
            Sku: "DOG-FOOD-5LB",
            NewAmount: 24.99m,
            Currency: "US",
            Reason: null,
            ChangedBy: Guid.NewGuid(),
            ChangedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void Validate_WithEmptyCurrency_FailsValidation()
    {
        var command = new ChangePrice(
            Sku: "DOG-FOOD-5LB",
            NewAmount: 24.99m,
            Currency: "",
            Reason: null,
            ChangedBy: Guid.NewGuid(),
            ChangedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Currency);
    }
}
