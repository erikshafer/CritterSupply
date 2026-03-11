using System.Text.Json;

namespace ProductCatalog.UnitTests.Products;

/// <summary>
/// Unit tests for the <see cref="Sku"/> value object.
/// Covers valid construction, format enforcement, equality, implicit string conversion,
/// and JSON serialization round-tripping.
/// </summary>
public class SkuTests
{
    // ---------------------------------------------------------------------------
    // Sku.From() — valid inputs
    // ---------------------------------------------------------------------------

    /// <summary>A valid uppercase alphanumeric SKU can be created.</summary>
    [Fact]
    public void From_ValidUppercaseAlphanumeric_Creates_Sku()
    {
        var sku = Sku.From("DOGFOOD001");

        sku.Value.ShouldBe("DOGFOOD001");
    }

    /// <summary>A valid SKU with hyphens can be created.</summary>
    [Fact]
    public void From_ValidSkuWithHyphens_Creates_Sku()
    {
        var sku = Sku.From("CAT-FOOD-5LB");

        sku.Value.ShouldBe("CAT-FOOD-5LB");
    }

    /// <summary>A SKU at exactly the maximum length (24 characters) is accepted.</summary>
    [Fact]
    public void From_MaxLength24Characters_Succeeds()
    {
        var maxSku = "ABCDEFGHIJ-0123456789-AB"; // 24 chars
        var sku = Sku.From(maxSku);

        sku.Value.ShouldBe(maxSku);
    }

    /// <summary>A single-character SKU is valid.</summary>
    [Fact]
    public void From_SingleCharacter_Succeeds()
    {
        var sku = Sku.From("A");

        sku.Value.ShouldBe("A");
    }

    // ---------------------------------------------------------------------------
    // Sku.From() — invalid inputs
    // ---------------------------------------------------------------------------

    /// <summary>An empty string is rejected.</summary>
    [Fact]
    public void From_EmptyString_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => Sku.From(""));
    }

    /// <summary>A whitespace-only string is rejected.</summary>
    [Fact]
    public void From_WhitespaceOnly_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => Sku.From("   "));
    }

    /// <summary>A SKU exceeding 24 characters is rejected.</summary>
    [Fact]
    public void From_TooLong_Throws_ArgumentException()
    {
        var tooLong = "ABCDEFGHIJ-0123456789-ABC"; // 25 chars
        Should.Throw<ArgumentException>(() => Sku.From(tooLong));
    }

    /// <summary>Lowercase letters are rejected.</summary>
    [Fact]
    public void From_LowercaseLetters_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => Sku.From("cat-food-001"));
    }

    /// <summary>Spaces are rejected.</summary>
    [Fact]
    public void From_ContainsSpaces_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => Sku.From("CAT FOOD 001"));
    }

    /// <summary>Special characters beyond hyphens (e.g. underscore) are rejected.</summary>
    [Fact]
    public void From_InvalidSpecialCharacter_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => Sku.From("CAT_FOOD_001"));
    }

    // ---------------------------------------------------------------------------
    // Equality — records provide value equality by default
    // ---------------------------------------------------------------------------

    /// <summary>Two Sku objects with the same value are equal.</summary>
    [Fact]
    public void Equality_Same_Value_Are_Equal()
    {
        var sku1 = Sku.From("DOG-TREAT-100");
        var sku2 = Sku.From("DOG-TREAT-100");

        sku1.ShouldBe(sku2);
    }

    /// <summary>Two Sku objects with different values are not equal.</summary>
    [Fact]
    public void Equality_Different_Values_Are_Not_Equal()
    {
        var sku1 = Sku.From("DOG-TREAT-100");
        var sku2 = Sku.From("CAT-FOOD-5LB");

        sku1.ShouldNotBe(sku2);
    }

    // ---------------------------------------------------------------------------
    // Implicit conversion and ToString
    // ---------------------------------------------------------------------------

    /// <summary>Implicit conversion to string yields the underlying value.</summary>
    [Fact]
    public void ImplicitConversion_To_String_ReturnsValue()
    {
        var sku = Sku.From("BIRD-SEED-10LB");
        string value = sku;

        value.ShouldBe("BIRD-SEED-10LB");
    }

    /// <summary>ToString returns the underlying value.</summary>
    [Fact]
    public void ToString_Returns_Value()
    {
        var sku = Sku.From("FISH-FOOD-2OZ");

        sku.ToString().ShouldBe("FISH-FOOD-2OZ");
    }

    // ---------------------------------------------------------------------------
    // JSON round-trip
    // ---------------------------------------------------------------------------

    /// <summary>Sku serializes to a plain JSON string (not a JSON object).</summary>
    [Fact]
    public void JsonSerialization_Serializes_As_Plain_String()
    {
        var sku = Sku.From("HAMSTER-WHEEL");
        var json = JsonSerializer.Serialize(sku);

        json.ShouldBe("\"HAMSTER-WHEEL\"");
    }

    /// <summary>Sku deserializes from a plain JSON string.</summary>
    [Fact]
    public void JsonSerialization_Deserializes_From_Plain_String()
    {
        var sku = JsonSerializer.Deserialize<Sku>("\"HAMSTER-WHEEL\"");

        sku.ShouldNotBeNull();
        sku!.Value.ShouldBe("HAMSTER-WHEEL");
    }

    /// <summary>Serializing then deserializing produces an equal Sku.</summary>
    [Fact]
    public void JsonSerialization_RoundTrip_Produces_Equal_Sku()
    {
        var original = Sku.From("REPTILE-LAMP-UV");
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<Sku>(json);

        roundTripped.ShouldBe(original);
    }
}
