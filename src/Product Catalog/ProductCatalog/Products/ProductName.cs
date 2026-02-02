using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProductCatalog.Products;

/// <summary>
/// Strongly-typed product name.
/// Enforces format constraints while remaining string-compatible for serialization.
/// Rules: Mixed case allowed, letters, numbers, spaces, and special chars (. , ! & ( ) -), max 100 characters.
/// </summary>
[JsonConverter(typeof(ProductNameJsonConverter))]
public sealed record ProductName
{
    private const int MaxLength = 100;
    private static readonly Regex ValidPattern = new(@"^[A-Za-z0-9\s.,!&()\-]+$", RegexOptions.Compiled);

    public string Value { get; init; } = null!;

    private ProductName() { }

    public static ProductName From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Product name cannot be empty", nameof(value));

        var trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
            throw new ArgumentException($"Product name cannot exceed {MaxLength} characters", nameof(value));

        if (!ValidPattern.IsMatch(trimmed))
            throw new ArgumentException(
                "Product name contains invalid characters. Allowed: letters, numbers, spaces, and . , ! & ( ) -",
                nameof(value));

        return new ProductName { Value = trimmed };
    }

    public static implicit operator string(ProductName name) => name.Value;

    public override string ToString() => Value;
}

public sealed class ProductNameJsonConverter : JsonConverter<ProductName>
{
    public override ProductName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null
            ? throw new JsonException("Product name cannot be null")
            : ProductName.From(value);
    }

    public override void Write(Utf8JsonWriter writer, ProductName value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
