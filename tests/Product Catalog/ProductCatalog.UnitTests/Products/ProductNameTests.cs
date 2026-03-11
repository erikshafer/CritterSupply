using System.Text.Json;

namespace ProductCatalog.UnitTests.Products;

/// <summary>
/// Unit tests for the <see cref="ProductName"/> value object.
/// Covers valid construction, format enforcement, trimming, equality,
/// implicit string conversion, and JSON serialization round-tripping.
/// </summary>
public class ProductNameTests
{
    // ---------------------------------------------------------------------------
    // ProductName.From() — valid inputs
    // ---------------------------------------------------------------------------

    /// <summary>A typical product name with letters is accepted.</summary>
    [Fact]
    public void From_SimpleLetters_Creates_ProductName()
    {
        var name = ProductName.From("Premium Dog Food");

        name.Value.ShouldBe("Premium Dog Food");
    }

    /// <summary>A name with allowed special characters is accepted.</summary>
    [Fact]
    public void From_WithAllowedSpecialCharacters_Creates_ProductName()
    {
        var name = ProductName.From("Salmon & Tuna Mix - Deluxe (5lb)");

        name.Value.ShouldBe("Salmon & Tuna Mix - Deluxe (5lb)");
    }

    /// <summary>A name with a period and comma is accepted.</summary>
    [Fact]
    public void From_WithPeriodAndComma_Creates_ProductName()
    {
        var name = ProductName.From("Premium Formula, Beef & Barley");

        name.Value.ShouldBe("Premium Formula, Beef & Barley");
    }

    /// <summary>Leading and trailing whitespace is trimmed.</summary>
    [Fact]
    public void From_TrimsLeadingAndTrailingWhitespace()
    {
        var name = ProductName.From("  Bird Seed Mix  ");

        name.Value.ShouldBe("Bird Seed Mix");
    }

    /// <summary>A name at exactly 100 characters is accepted.</summary>
    [Fact]
    public void From_ExactlyMaxLength_100_Characters_Succeeds()
    {
        var maxName = new string('A', 100);
        var name = ProductName.From(maxName);

        name.Value.ShouldBe(maxName);
    }

    // ---------------------------------------------------------------------------
    // ProductName.From() — invalid inputs
    // ---------------------------------------------------------------------------

    /// <summary>An empty string is rejected.</summary>
    [Fact]
    public void From_EmptyString_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => ProductName.From(""));
    }

    /// <summary>A whitespace-only string is rejected.</summary>
    [Fact]
    public void From_WhitespaceOnly_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => ProductName.From("   "));
    }

    /// <summary>A name exceeding 100 characters is rejected.</summary>
    [Fact]
    public void From_Exceeds_100_Characters_Throws_ArgumentException()
    {
        var tooLong = new string('A', 101);
        Should.Throw<ArgumentException>(() => ProductName.From(tooLong));
    }

    /// <summary>A name with an invalid character (e.g. '#') is rejected.</summary>
    [Fact]
    public void From_InvalidCharacter_Hash_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => ProductName.From("Premium #1 Dog Food"));
    }

    /// <summary>A name with an at-sign is rejected.</summary>
    [Fact]
    public void From_InvalidCharacter_AtSign_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => ProductName.From("Contact@us Dog Food"));
    }

    // ---------------------------------------------------------------------------
    // Equality
    // ---------------------------------------------------------------------------

    /// <summary>Two ProductName objects with the same value are equal.</summary>
    [Fact]
    public void Equality_Same_Value_Are_Equal()
    {
        var name1 = ProductName.From("Premium Cat Food");
        var name2 = ProductName.From("Premium Cat Food");

        name1.ShouldBe(name2);
    }

    /// <summary>Two ProductName objects with different values are not equal.</summary>
    [Fact]
    public void Equality_Different_Values_Are_Not_Equal()
    {
        var name1 = ProductName.From("Premium Cat Food");
        var name2 = ProductName.From("Basic Dog Kibble");

        name1.ShouldNotBe(name2);
    }

    // ---------------------------------------------------------------------------
    // Implicit conversion and ToString
    // ---------------------------------------------------------------------------

    /// <summary>Implicit conversion to string yields the underlying value.</summary>
    [Fact]
    public void ImplicitConversion_To_String_ReturnsValue()
    {
        var name = ProductName.From("Deluxe Fish Flakes");
        string value = name;

        value.ShouldBe("Deluxe Fish Flakes");
    }

    /// <summary>ToString returns the underlying value.</summary>
    [Fact]
    public void ToString_Returns_Value()
    {
        var name = ProductName.From("Reptile Habitat Kit");

        name.ToString().ShouldBe("Reptile Habitat Kit");
    }

    // ---------------------------------------------------------------------------
    // JSON round-trip
    // ---------------------------------------------------------------------------

    /// <summary>ProductName serializes to a plain JSON string.</summary>
    [Fact]
    public void JsonSerialization_Serializes_As_Plain_String()
    {
        var name = ProductName.From("Organic Hamster Pellets");
        var json = JsonSerializer.Serialize(name);

        json.ShouldBe("\"Organic Hamster Pellets\"");
    }

    /// <summary>ProductName deserializes from a plain JSON string.</summary>
    [Fact]
    public void JsonSerialization_Deserializes_From_Plain_String()
    {
        var name = JsonSerializer.Deserialize<ProductName>("\"Organic Hamster Pellets\"");

        name.ShouldNotBeNull();
        name!.Value.ShouldBe("Organic Hamster Pellets");
    }

    /// <summary>Serializing then deserializing produces an equal ProductName.</summary>
    [Fact]
    public void JsonSerialization_RoundTrip_Produces_Equal_ProductName()
    {
        var original = ProductName.From("Aquarium Gravel 5lb");
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<ProductName>(json);

        roundTripped.ShouldBe(original);
    }
}
