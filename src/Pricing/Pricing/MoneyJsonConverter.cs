using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pricing;

/// <summary>
/// JSON converter for Money value object.
/// Serializes as: { "amount": 24.99, "currency": "USD" }
/// Deserializes from same structure, validates via Money.Of() factory method.
/// </summary>
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject token, got {reader.TokenType}");

        decimal amount = 0m;
        string? currency = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected PropertyName token, got {reader.TokenType}");

            var propertyName = reader.GetString();
            reader.Read(); // Move to property value

            switch (propertyName?.ToLowerInvariant())
            {
                case "amount":
                    amount = reader.GetDecimal();
                    break;
                case "currency":
                    currency = reader.GetString();
                    break;
                default:
                    // Skip unknown properties
                    reader.Skip();
                    break;
            }
        }

        if (currency is null)
            throw new JsonException("Money JSON object must have 'currency' property");

        // Use factory method for validation
        try
        {
            return Money.Of(amount, currency);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException($"Invalid Money value: {ex.Message}", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("amount", value.Amount);
        writer.WriteString("currency", value.Currency);
        writer.WriteEndObject();
    }
}
