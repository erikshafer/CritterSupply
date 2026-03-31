namespace Pricing.UnitTests;

/// <summary>
/// Tests for Money.Of() factory method validation.
/// </summary>
public sealed class MoneyFactoryTests
{
    [Fact]
    public void Of_WithValidAmountAndCurrency_CreatesMoneyInstance()
    {
        // Arrange & Act
        var money = Money.Of(24.99m, "USD");

        // Assert
        money.Amount.ShouldBe(24.99m);
        money.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Of_WithDefaultCurrency_UsesUSD()
    {
        // Arrange & Act
        var money = Money.Of(10.00m);

        // Assert
        money.Amount.ShouldBe(10.00m);
        money.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Of_WithZeroAmount_CreatesValidMoney()
    {
        // Arrange & Act
        var money = Money.Of(0m, "EUR");

        // Assert
        money.Amount.ShouldBe(0m);
        money.Currency.ShouldBe("EUR");
    }

    [Fact]
    public void Of_RoundsAmountTo2DecimalPlaces()
    {
        // Arrange & Act
        var money = Money.Of(24.999m, "USD");

        // Assert
        money.Amount.ShouldBe(25.00m); // Rounded away from zero
    }

    [Fact]
    public void Of_RoundsAmountAwayFromZero()
    {
        // Arrange & Act
        var money1 = Money.Of(24.995m, "USD");
        var money2 = Money.Of(24.994m, "USD");

        // Assert
        money1.Amount.ShouldBe(25.00m); // 24.995 rounds up
        money2.Amount.ShouldBe(24.99m); // 24.994 rounds down
    }

    [Fact]
    public void Of_NormalizesCurrencyToUppercase()
    {
        // Arrange & Act
        var money = Money.Of(10m, "usd");

        // Assert
        money.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Of_WithNegativeAmount_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        var ex = Should.Throw<ArgumentException>(() => Money.Of(-10m, "USD"));
        ex.ParamName.ShouldBe("amount");
        ex.Message.ShouldContain("Money amount cannot be negative");
        ex.Message.ShouldContain("-10");
    }

    [Fact]
    public void Of_WithNullCurrency_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        var ex = Should.Throw<ArgumentException>(() => Money.Of(10m, null!));
        ex.ParamName.ShouldBe("currency");
    }

    [Fact]
    public void Of_WithEmptyCurrency_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        var ex = Should.Throw<ArgumentException>(() => Money.Of(10m, ""));
        ex.ParamName.ShouldBe("currency");
    }

    [Fact]
    public void Of_WithWhitespaceCurrency_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        var ex = Should.Throw<ArgumentException>(() => Money.Of(10m, "   "));
        ex.ParamName.ShouldBe("currency");
    }

    [Theory]
    [InlineData("US")]        // Too short
    [InlineData("USDD")]      // Too long
    [InlineData("U")]         // Too short
    [InlineData("USDDD")]     // Too long
    public void Of_WithInvalidCurrencyLength_ThrowsArgumentException(string currency)
    {
        // Arrange & Act & Assert
        var ex = Should.Throw<ArgumentException>(() => Money.Of(10m, currency));
        ex.ParamName.ShouldBe("currency");
        ex.Message.ShouldContain("Currency must be ISO 4217 3-letter code");
        ex.Message.ShouldContain($"got: '{currency.ToUpperInvariant()}'");
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    [InlineData("JPY")]
    [InlineData("CAD")]
    [InlineData("AUD")]
    public void Of_WithValidISO4217Currencies_CreatesMoneyInstance(string currency)
    {
        // Arrange & Act
        var money = Money.Of(100m, currency);

        // Assert
        money.Currency.ShouldBe(currency.ToUpperInvariant());
    }

    [Fact]
    public void Zero_ReturnsZeroUSDMoney()
    {
        // Arrange & Act
        var zero = Money.Zero;

        // Assert
        zero.Amount.ShouldBe(0m);
        zero.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Zero_IsSameInstanceAcrossMultipleCalls()
    {
        // Arrange & Act
        var zero1 = Money.Zero;
        var zero2 = Money.Zero;

        // Assert
        ReferenceEquals(zero1, zero2).ShouldBeTrue();
    }
}
