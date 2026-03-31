using FluentValidation.TestHelper;
using Pricing.Products;

namespace Pricing.UnitTests;

public sealed class SetInitialPriceValidatorTests
{
    private readonly SetInitialPriceValidator _validator = new();
    private readonly DateTimeOffset _testTime = new(2026, 3, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validate_WithValidCommand_PassesValidation()
    {
        var command = new SetInitialPrice(
            Sku: "DOG-FOOD-5LB",
            Amount: 29.99m,
            Currency: "USD",
            FloorAmount: null,
            CeilingAmount: null,
            SetBy: Guid.NewGuid(),
            PricedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptySku_FailsValidation()
    {
        var command = new SetInitialPrice(
            Sku: "",
            Amount: 29.99m,
            Currency: "USD",
            FloorAmount: null,
            CeilingAmount: null,
            SetBy: Guid.NewGuid(),
            PricedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Fact]
    public void Validate_WithZeroAmount_FailsValidation()
    {
        var command = new SetInitialPrice(
            Sku: "DOG-FOOD-5LB",
            Amount: 0m,
            Currency: "USD",
            FloorAmount: null,
            CeilingAmount: null,
            SetBy: Guid.NewGuid(),
            PricedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void Validate_WithInvalidCurrency_FailsValidation()
    {
        var command = new SetInitialPrice(
            Sku: "DOG-FOOD-5LB",
            Amount: 29.99m,
            Currency: "US",
            FloorAmount: null,
            CeilingAmount: null,
            SetBy: Guid.NewGuid(),
            PricedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void Validate_WithAmountBelowFloor_FailsValidation()
    {
        var command = new SetInitialPrice(
            Sku: "DOG-FOOD-5LB",
            Amount: 15.99m,
            Currency: "USD",
            FloorAmount: 19.99m,
            CeilingAmount: null,
            SetBy: Guid.NewGuid(),
            PricedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Validate_WithAmountAboveCeiling_FailsValidation()
    {
        var command = new SetInitialPrice(
            Sku: "DOG-FOOD-5LB",
            Amount: 44.99m,
            Currency: "USD",
            FloorAmount: null,
            CeilingAmount: 39.99m,
            SetBy: Guid.NewGuid(),
            PricedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Validate_WithFloorAboveCeiling_FailsValidation()
    {
        var command = new SetInitialPrice(
            Sku: "DOG-FOOD-5LB",
            Amount: 29.99m,
            Currency: "USD",
            FloorAmount: 39.99m,
            CeilingAmount: 19.99m,
            SetBy: Guid.NewGuid(),
            PricedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Validate_WithValidFloorAndCeiling_PassesValidation()
    {
        var command = new SetInitialPrice(
            Sku: "DOG-FOOD-5LB",
            Amount: 29.99m,
            Currency: "USD",
            FloorAmount: 19.99m,
            CeilingAmount: 39.99m,
            SetBy: Guid.NewGuid(),
            PricedAt: _testTime);

        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
