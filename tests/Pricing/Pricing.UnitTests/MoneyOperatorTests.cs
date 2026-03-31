namespace Pricing.UnitTests;

/// <summary>
/// Tests for Money operator overloads (>, <, >=, <=) and explicit decimal cast.
/// </summary>
public sealed class MoneyOperatorTests
{
    [Fact]
    public void GreaterThan_WhenLeftIsGreater_ReturnsTrue()
    {
        // Arrange
        var left = Money.Of(25m, "USD");
        var right = Money.Of(20m, "USD");

        // Act & Assert
        (left > right).ShouldBeTrue();
    }

    [Fact]
    public void GreaterThan_WhenLeftIsEqual_ReturnsFalse()
    {
        // Arrange
        var left = Money.Of(25m, "USD");
        var right = Money.Of(25m, "USD");

        // Act & Assert
        (left > right).ShouldBeFalse();
    }

    [Fact]
    public void GreaterThan_WhenLeftIsLess_ReturnsFalse()
    {
        // Arrange
        var left = Money.Of(20m, "USD");
        var right = Money.Of(25m, "USD");

        // Act & Assert
        (left > right).ShouldBeFalse();
    }

    [Fact]
    public void LessThan_WhenLeftIsLess_ReturnsTrue()
    {
        // Arrange
        var left = Money.Of(20m, "USD");
        var right = Money.Of(25m, "USD");

        // Act & Assert
        (left < right).ShouldBeTrue();
    }

    [Fact]
    public void LessThan_WhenLeftIsEqual_ReturnsFalse()
    {
        // Arrange
        var left = Money.Of(25m, "USD");
        var right = Money.Of(25m, "USD");

        // Act & Assert
        (left < right).ShouldBeFalse();
    }

    [Fact]
    public void LessThan_WhenLeftIsGreater_ReturnsFalse()
    {
        // Arrange
        var left = Money.Of(25m, "USD");
        var right = Money.Of(20m, "USD");

        // Act & Assert
        (left < right).ShouldBeFalse();
    }

    [Fact]
    public void GreaterThanOrEqual_WhenLeftIsGreater_ReturnsTrue()
    {
        // Arrange
        var left = Money.Of(25m, "USD");
        var right = Money.Of(20m, "USD");

        // Act & Assert
        (left >= right).ShouldBeTrue();
    }

    [Fact]
    public void GreaterThanOrEqual_WhenLeftIsEqual_ReturnsTrue()
    {
        // Arrange
        var left = Money.Of(25m, "USD");
        var right = Money.Of(25m, "USD");

        // Act & Assert
        (left >= right).ShouldBeTrue();
    }

    [Fact]
    public void GreaterThanOrEqual_WhenLeftIsLess_ReturnsFalse()
    {
        // Arrange
        var left = Money.Of(20m, "USD");
        var right = Money.Of(25m, "USD");

        // Act & Assert
        (left >= right).ShouldBeFalse();
    }

    [Fact]
    public void LessThanOrEqual_WhenLeftIsLess_ReturnsTrue()
    {
        // Arrange
        var left = Money.Of(20m, "USD");
        var right = Money.Of(25m, "USD");

        // Act & Assert
        (left <= right).ShouldBeTrue();
    }

    [Fact]
    public void LessThanOrEqual_WhenLeftIsEqual_ReturnsTrue()
    {
        // Arrange
        var left = Money.Of(25m, "USD");
        var right = Money.Of(25m, "USD");

        // Act & Assert
        (left <= right).ShouldBeTrue();
    }

    [Fact]
    public void LessThanOrEqual_WhenLeftIsGreater_ReturnsFalse()
    {
        // Arrange
        var left = Money.Of(25m, "USD");
        var right = Money.Of(20m, "USD");

        // Act & Assert
        (left <= right).ShouldBeFalse();
    }

    [Fact]
    public void GreaterThan_WithDifferentCurrencies_ThrowsInvalidOperationException()
    {
        // Arrange
        var usd = Money.Of(25m, "USD");
        var eur = Money.Of(20m, "EUR");

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => usd > eur);
        ex.Message.ShouldContain("Cannot compare Money with different currencies");
        ex.Message.ShouldContain("USD");
        ex.Message.ShouldContain("EUR");
    }

    [Fact]
    public void LessThan_WithDifferentCurrencies_ThrowsInvalidOperationException()
    {
        // Arrange
        var usd = Money.Of(25m, "USD");
        var gbp = Money.Of(30m, "GBP");

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => usd < gbp);
        ex.Message.ShouldContain("Cannot compare Money with different currencies");
        ex.Message.ShouldContain("USD");
        ex.Message.ShouldContain("GBP");
    }

    [Fact]
    public void GreaterThanOrEqual_WithDifferentCurrencies_ThrowsInvalidOperationException()
    {
        // Arrange
        var usd = Money.Of(25m, "USD");
        var jpy = Money.Of(1000m, "JPY");

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => usd >= jpy);
        ex.Message.ShouldContain("Cannot compare Money with different currencies");
    }

    [Fact]
    public void LessThanOrEqual_WithDifferentCurrencies_ThrowsInvalidOperationException()
    {
        // Arrange
        var cad = Money.Of(10m, "CAD");
        var aud = Money.Of(15m, "AUD");

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => cad <= aud);
        ex.Message.ShouldContain("Cannot compare Money with different currencies");
    }

    [Fact]
    public void ExplicitCastToDecimal_ReturnsAmount()
    {
        // Arrange
        var money = Money.Of(24.99m, "USD");

        // Act
        var amount = (decimal)money;

        // Assert
        amount.ShouldBe(24.99m);
    }

    [Fact]
    public void ExplicitCastToDecimal_PreservesPrecision()
    {
        // Arrange
        var money = Money.Of(123.45m, "EUR");

        // Act
        var amount = (decimal)money;

        // Assert
        amount.ShouldBe(123.45m);
    }

    [Fact]
    public void ExplicitCastToDecimal_WorksWithZero()
    {
        // Arrange
        var money = Money.Zero;

        // Act
        var amount = (decimal)money;

        // Assert
        amount.ShouldBe(0m);
    }
}
