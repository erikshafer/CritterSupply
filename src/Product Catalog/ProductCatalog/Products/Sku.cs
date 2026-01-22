using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProductCatalog.Products;

/// <summary>
/// Strongly-typed SKU identifier for products.
/// Enforces format constraints while remaining string-compatible for serialization.
/// Rules: A-Z (uppercase only), 0-9, hyphens (-), max 24 characters.
/// </summary>
[JsonConverter(typeof(SkuJsonConverter))]
public sealed record Sku
{
    private const int MaxLength = 24;
    private static readonly Regex ValidPattern = new(@"^[A-Z0-9\-]+$", RegexOptions.Compiled);

    public string Value { get; init; } = null!;

    private Sku() { }

    public static Sku From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("SKU cannot be empty", nameof(value));

        if (value.Length > MaxLength)
            throw new ArgumentException($"SKU cannot exceed {MaxLength} characters", nameof(value));

        if (!ValidPattern.IsMatch(value))
            throw new ArgumentException(
                "SKU must contain only uppercase letters (A-Z), numbers (0-9), and hyphens (-)",
                nameof(value));

        return new Sku { Value = value };
    }

    public static implicit operator string(Sku sku) => sku.Value;

    public override string ToString() => Value;
}

public sealed class SkuJsonConverter : JsonConverter<Sku>
{
    public override Sku Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null
            ? throw new JsonException("SKU cannot be null")
            : Sku.From(value);
    }

    public override void Write(Utf8JsonWriter writer, Sku value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
