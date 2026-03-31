namespace Pricing.UnitTests;

/// <summary>
/// Tests for Money equality (records have value equality by default) and ToString formatting.
/// </summary>
public sealed class MoneyEqualityTests
{
    [Fact]
    public void Equality_WithSameAmountAndCurrency_AreEqual()
    {
        // Arrange
        var money1 = Money.Of(24.99m, "USD");
        var money2 = Money.Of(24.99m, "USD");

        // Act & Assert
        money1.ShouldBe(money2);
        (money1 == money2).ShouldBeTrue();
        (money1 != money2).ShouldBeFalse();
    }

    [Fact]
    public void Equality_WithDifferentAmountsSameCurrency_AreNotEqual()
    {
        // Arrange
        var money1 = Money.Of(24.99m, "USD");
        var money2 = Money.Of(25.00m, "USD");

        // Act & Assert
        money1.ShouldNotBe(money2);
        (money1 == money2).ShouldBeFalse();
        (money1 != money2).ShouldBeTrue();
    }

    [Fact]
    public void Equality_WithSameAmountDifferentCurrency_AreNotEqual()
    {
        // Arrange
        var money1 = Money.Of(24.99m, "USD");
        var money2 = Money.Of(24.99m, "EUR");

        // Act & Assert
        money1.ShouldNotBe(money2);
        (money1 == money2).ShouldBeFalse();
        (money1 != money2).ShouldBeTrue();
    }

    [Fact]
    public void Equality_WithDifferentAmountAndCurrency_AreNotEqual()
    {
        // Arrange
        var money1 = Money.Of(24.99m, "USD");
        var money2 = Money.Of(30.00m, "GBP");

        // Act & Assert
        money1.ShouldNotBe(money2);
        (money1 == money2).ShouldBeFalse();
        (money1 != money2).ShouldBeTrue();
    }

    [Fact]
    public void GetHashCode_ForEqualMoney_AreSame()
    {
        // Arrange
        var money1 = Money.Of(24.99m, "USD");
        var money2 = Money.Of(24.99m, "USD");

        // Act & Assert
        money1.GetHashCode().ShouldBe(money2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_ForDifferentMoney_AreDifferent()
    {
        // Arrange
        var money1 = Money.Of(24.99m, "USD");
        var money2 = Money.Of(25.00m, "USD");

        // Act & Assert
        money1.GetHashCode().ShouldNotBe(money2.GetHashCode());
    }

    [Fact]
    public void ToString_WithUSMoney_FormatsAsCurrency()
    {
        // Arrange
        var money = Money.Of(24.99m, "USD");

        // Act
        var result = money.ToString();

        // Assert
        result.ShouldContain("24.99");
        result.ShouldContain("USD");
    }

    [Fact]
    public void ToString_WithZeroMoney_FormatsCorrectly()
    {
        // Arrange
        var money = Money.Zero;

        // Act
        var result = money.ToString();

        // Assert
        result.ShouldContain("0");
        result.ShouldContain("USD");
    }

    [Fact]
    public void ToString_WithNonUSDCurrency_IncludesCurrencyCode()
    {
        // Arrange
        var money = Money.Of(123.45m, "EUR");

        // Act
        var result = money.ToString();

        // Assert
        result.ShouldContain("123.45");
        result.ShouldContain("EUR");
    }

    [Fact]
    public void With_ModifyingAmount_CreatesNewInstance()
    {
        // Arrange
        var original = Money.Of(24.99m, "USD");

        // Act
        var modified = original with { Amount = 30.00m };

        // Assert
        original.Amount.ShouldBe(24.99m);
        modified.Amount.ShouldBe(30.00m);
        modified.Currency.ShouldBe("USD");
    }

    [Fact]
    public void With_ModifyingCurrency_CreatesNewInstance()
    {
        // Arrange
        var original = Money.Of(24.99m, "USD");

        // Act
        var modified = original with { Currency = "EUR" };

        // Assert
        original.Currency.ShouldBe("USD");
        modified.Currency.ShouldBe("EUR");
        modified.Amount.ShouldBe(24.99m);
    }

    [Fact]
    public void Collection_CanContainMoney()
    {
        // Arrange
        var prices = new List<Money>
        {
            Money.Of(10m, "USD"),
            Money.Of(20m, "USD"),
            Money.Of(30m, "EUR")
        };

        // Act & Assert
        prices.Count.ShouldBe(3);
        prices[0].Amount.ShouldBe(10m);
        prices[1].Amount.ShouldBe(20m);
        prices[2].Currency.ShouldBe("EUR");
    }

    [Fact]
    public void Dictionary_CanUseMoneyAsKey()
    {
        // Arrange
        var priceMapping = new Dictionary<Money, string>
        {
            { Money.Of(10m, "USD"), "Cheap" },
            { Money.Of(50m, "USD"), "Moderate" },
            { Money.Of(100m, "USD"), "Expensive" }
        };

        // Act & Assert
        priceMapping[Money.Of(10m, "USD")].ShouldBe("Cheap");
        priceMapping[Money.Of(50m, "USD")].ShouldBe("Moderate");
    }
}
