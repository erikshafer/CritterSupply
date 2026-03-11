using System.Text.Json;

namespace ProductCatalog.UnitTests.Products;

/// <summary>
/// Unit tests for the <see cref="CategoryName"/> value object.
/// Covers valid construction, length enforcement, whitespace trimming,
/// equality, implicit string conversion, and JSON serialization round-tripping.
/// </summary>
public class CategoryNameTests
{
    // ---------------------------------------------------------------------------
    // CategoryName.From() — valid inputs
    // ---------------------------------------------------------------------------

    /// <summary>A typical category name is accepted.</summary>
    [Fact]
    public void From_SimpleString_Creates_CategoryName()
    {
        var category = CategoryName.From("Dog Food");

        category.Value.ShouldBe("Dog Food");
    }

    /// <summary>A name at exactly 50 characters is accepted.</summary>
    [Fact]
    public void From_ExactlyMaxLength_50_Characters_Succeeds()
    {
        var maxName = new string('A', 50);
        var category = CategoryName.From(maxName);

        category.Value.ShouldBe(maxName);
    }

    /// <summary>Leading and trailing whitespace is trimmed.</summary>
    [Fact]
    public void From_TrimsLeadingAndTrailingWhitespace()
    {
        var category = CategoryName.From("  Cat Supplies  ");

        category.Value.ShouldBe("Cat Supplies");
    }

    /// <summary>A category name with numbers and letters is accepted.</summary>
    [Fact]
    public void From_WithNumbersAndLetters_Succeeds()
    {
        var category = CategoryName.From("Aquarium & Fish Supplies");

        category.Value.ShouldBe("Aquarium & Fish Supplies");
    }

    // ---------------------------------------------------------------------------
    // CategoryName.From() — invalid inputs
    // ---------------------------------------------------------------------------

    /// <summary>An empty string is rejected.</summary>
    [Fact]
    public void From_EmptyString_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => CategoryName.From(""));
    }

    /// <summary>A whitespace-only string is rejected.</summary>
    [Fact]
    public void From_WhitespaceOnly_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => CategoryName.From("   "));
    }

    /// <summary>A name exceeding 50 characters is rejected.</summary>
    [Fact]
    public void From_Exceeds_50_Characters_Throws_ArgumentException()
    {
        var tooLong = new string('A', 51);
        Should.Throw<ArgumentException>(() => CategoryName.From(tooLong));
    }

    // ---------------------------------------------------------------------------
    // Equality
    // ---------------------------------------------------------------------------

    /// <summary>Two CategoryName objects with the same value are equal.</summary>
    [Fact]
    public void Equality_Same_Value_Are_Equal()
    {
        var cat1 = CategoryName.From("Bird Supplies");
        var cat2 = CategoryName.From("Bird Supplies");

        cat1.ShouldBe(cat2);
    }

    /// <summary>Two CategoryName objects with different values are not equal.</summary>
    [Fact]
    public void Equality_Different_Values_Are_Not_Equal()
    {
        var cat1 = CategoryName.From("Bird Supplies");
        var cat2 = CategoryName.From("Reptile Supplies");

        cat1.ShouldNotBe(cat2);
    }

    // ---------------------------------------------------------------------------
    // Implicit conversion and ToString
    // ---------------------------------------------------------------------------

    /// <summary>Implicit conversion to string yields the underlying value.</summary>
    [Fact]
    public void ImplicitConversion_To_String_ReturnsValue()
    {
        var category = CategoryName.From("Small Animal Supplies");
        string value = category;

        value.ShouldBe("Small Animal Supplies");
    }

    /// <summary>ToString returns the underlying value.</summary>
    [Fact]
    public void ToString_Returns_Value()
    {
        var category = CategoryName.From("Fish & Aquatics");

        category.ToString().ShouldBe("Fish & Aquatics");
    }

    // ---------------------------------------------------------------------------
    // JSON round-trip
    // ---------------------------------------------------------------------------

    /// <summary>CategoryName serializes to a plain JSON string.</summary>
    [Fact]
    public void JsonSerialization_Serializes_As_Plain_String()
    {
        var category = CategoryName.From("Dog Treats");
        var json = JsonSerializer.Serialize(category);

        json.ShouldBe("\"Dog Treats\"");
    }

    /// <summary>CategoryName deserializes from a plain JSON string.</summary>
    [Fact]
    public void JsonSerialization_Deserializes_From_Plain_String()
    {
        var category = JsonSerializer.Deserialize<CategoryName>("\"Dog Treats\"");

        category.ShouldNotBeNull();
        category!.Value.ShouldBe("Dog Treats");
    }

    /// <summary>Serializing then deserializing produces an equal CategoryName.</summary>
    [Fact]
    public void JsonSerialization_RoundTrip_Produces_Equal_CategoryName()
    {
        var original = CategoryName.From("Cat Grooming");
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<CategoryName>(json);

        roundTripped.ShouldBe(original);
    }
}
