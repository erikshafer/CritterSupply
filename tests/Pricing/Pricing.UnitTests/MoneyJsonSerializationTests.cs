using System.Text.Json;

namespace Pricing.UnitTests;

/// <summary>
/// Tests for Money JSON serialization and deserialization via MoneyJsonConverter.
/// </summary>
public sealed class MoneyJsonSerializationTests
{
    [Fact]
    public void Serialize_WithValidMoney_ProducesCorrectJSON()
    {
        // Arrange
        var money = Money.Of(24.99m, "USD");

        // Act
        var json = JsonSerializer.Serialize(money);

        // Assert
        json.ShouldBe("""{"amount":24.99,"currency":"USD"}""");
    }

    [Fact]
    public void Serialize_WithZeroMoney_ProducesCorrectJSON()
    {
        // Arrange
        var money = Money.Zero;

        // Act
        var json = JsonSerializer.Serialize(money);

        // Assert
        json.ShouldBe("""{"amount":0,"currency":"USD"}""");
    }

    [Fact]
    public void Serialize_WithNonUSDCurrency_ProducesCorrectJSON()
    {
        // Arrange
        var money = Money.Of(123.45m, "EUR");

        // Act
        var json = JsonSerializer.Serialize(money);

        // Assert
        json.ShouldBe("""{"amount":123.45,"currency":"EUR"}""");
    }

    [Fact]
    public void Deserialize_WithValidJSON_CreatesMoney()
    {
        // Arrange
        var json = """{"amount":24.99,"currency":"USD"}""";

        // Act
        var money = JsonSerializer.Deserialize<Money>(json);

        // Assert
        money.ShouldNotBeNull();
        money.Amount.ShouldBe(24.99m);
        money.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Deserialize_WithZeroAmount_CreatesMoney()
    {
        // Arrange
        var json = """{"amount":0,"currency":"EUR"}""";

        // Act
        var money = JsonSerializer.Deserialize<Money>(json);

        // Assert
        money.ShouldNotBeNull();
        money.Amount.ShouldBe(0m);
        money.Currency.ShouldBe("EUR");
    }

    [Fact]
    public void Deserialize_NormalizesLowercaseCurrencyToUppercase()
    {
        // Arrange
        var json = """{"amount":100,"currency":"usd"}""";

        // Act
        var money = JsonSerializer.Deserialize<Money>(json);

        // Assert
        money.ShouldNotBeNull();
        money.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Deserialize_RoundsAmountTo2DecimalPlaces()
    {
        // Arrange
        var json = """{"amount":24.999,"currency":"USD"}""";

        // Act
        var money = JsonSerializer.Deserialize<Money>(json);

        // Assert
        money.ShouldNotBeNull();
        money.Amount.ShouldBe(25.00m); // Rounded
    }

    [Fact]
    public void Deserialize_WithNegativeAmount_ThrowsJsonException()
    {
        // Arrange
        var json = """{"amount":-10,"currency":"USD"}""";

        // Act & Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Money>(json));
    }

    [Fact]
    public void Deserialize_WithInvalidCurrencyLength_ThrowsJsonException()
    {
        // Arrange
        var json = """{"amount":10,"currency":"US"}""";

        // Act & Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Money>(json));
    }

    [Fact]
    public void Deserialize_WithMissingCurrency_ThrowsJsonException()
    {
        // Arrange
        var json = """{"amount":10}""";

        // Act & Assert
        var ex = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Money>(json));
        ex.Message.ShouldContain("Money JSON object must have 'currency' property");
    }

    [Fact]
    public void Deserialize_WithMissingAmount_UsesZero()
    {
        // Arrange
        var json = """{"currency":"USD"}""";

        // Act
        var money = JsonSerializer.Deserialize<Money>(json);

        // Assert
        money.ShouldNotBeNull();
        money.Amount.ShouldBe(0m);
        money.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Deserialize_WithUnknownProperties_IgnoresThem()
    {
        // Arrange
        var json = """{"amount":24.99,"currency":"USD","unknown":"ignored"}""";

        // Act
        var money = JsonSerializer.Deserialize<Money>(json);

        // Assert
        money.ShouldNotBeNull();
        money.Amount.ShouldBe(24.99m);
        money.Currency.ShouldBe("USD");
    }

    [Fact]
    public void RoundTrip_SerializeAndDeserialize_ProducesEqualMoney()
    {
        // Arrange
        var original = Money.Of(99.99m, "GBP");

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<Money>(json);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.ShouldBe(original);
    }

    [Fact]
    public void RoundTrip_WithZeroMoney_ProducesEqualMoney()
    {
        // Arrange
        var original = Money.Zero;

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<Money>(json);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.ShouldBe(original);
    }

    [Fact]
    public void Serialize_InArray_ProducesCorrectJSON()
    {
        // Arrange
        var prices = new[] {
            Money.Of(10m, "USD"),
            Money.Of(20m, "USD"),
            Money.Of(30m, "USD")
        };

        // Act
        var json = JsonSerializer.Serialize(prices);

        // Assert
        json.ShouldContain("""{"amount":10,"currency":"USD"}""");
        json.ShouldContain("""{"amount":20,"currency":"USD"}""");
        json.ShouldContain("""{"amount":30,"currency":"USD"}""");
    }

    [Fact]
    public void Deserialize_FromArray_CreatesMoneyInstances()
    {
        // Arrange
        var json = """[{"amount":10,"currency":"USD"},{"amount":20,"currency":"EUR"}]""";

        // Act
        var prices = JsonSerializer.Deserialize<Money[]>(json);

        // Assert
        prices.ShouldNotBeNull();
        prices.Length.ShouldBe(2);
        prices[0].Amount.ShouldBe(10m);
        prices[0].Currency.ShouldBe("USD");
        prices[1].Amount.ShouldBe(20m);
        prices[1].Currency.ShouldBe("EUR");
    }
}
