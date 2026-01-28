using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProductCatalog.Products;

/// <summary>
/// Category name value object.
/// Phase 1: Simple string wrapper for basic categorization.
/// Future: Will evolve into full Category subdomain with marketplace mapping.
/// </summary>
[JsonConverter(typeof(CategoryNameJsonConverter))]
public sealed record CategoryName
{
    private const int MaxLength = 50;

    public string Value { get; init; } = null!;

    private CategoryName() { }

    public static CategoryName From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Category name cannot be empty", nameof(value));

        var trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
            throw new ArgumentException($"Category name cannot exceed {MaxLength} characters", nameof(value));

        return new CategoryName { Value = trimmed };
    }

    public static implicit operator string(CategoryName category) => category.Value;

    public override string ToString() => Value;
}

public sealed class CategoryNameJsonConverter : JsonConverter<CategoryName>
{
    public override CategoryName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null
            ? throw new JsonException("Category name cannot be null")
            : CategoryName.From(value);
    }

    public override void Write(Utf8JsonWriter writer, CategoryName value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
