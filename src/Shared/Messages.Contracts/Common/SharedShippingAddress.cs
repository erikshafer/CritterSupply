using System.Text.Json.Serialization;

namespace Messages.Contracts.Common;

/// <summary>
/// Canonical shared shipping address type used across bounded context integration messages.
/// Uses Fulfillment BC naming convention (AddressLine1, StateProvince) as the primary fields.
/// Legacy Orders BC field names (Street, State) are included as obsolete compatibility aliases
/// to allow deserialization of existing persisted messages during the Phase A migration window.
/// Phase B (next cycle) will remove the obsolete aliases after all in-flight messages have drained.
/// </summary>
public sealed record SharedShippingAddress
{
    [JsonPropertyName("addressLine1")]
    public required string AddressLine1 { get; init; }

    [JsonPropertyName("addressLine2")]
    public string? AddressLine2 { get; init; }

    [JsonPropertyName("city")]
    public required string City { get; init; }

    [JsonPropertyName("stateProvince")]
    public required string StateProvince { get; init; }

    [JsonPropertyName("postalCode")]
    public required string PostalCode { get; init; }

    [JsonPropertyName("country")]
    public required string Country { get; init; }

    // Legacy compatibility aliases for Orders BC naming convention (Phase A migration)
    [JsonPropertyName("street")]
    [Obsolete("Use AddressLine1 instead. Will be removed in Phase B cleanup.")]
    public string? Street
    {
        get => AddressLine1;
        init => AddressLine1 = value!;
    }

    [JsonPropertyName("street2")]
    [Obsolete("Use AddressLine2 instead. Will be removed in Phase B cleanup.")]
    public string? Street2
    {
        get => AddressLine2;
        init => AddressLine2 = value;
    }

    [JsonPropertyName("state")]
    [Obsolete("Use StateProvince instead. Will be removed in Phase B cleanup.")]
    public string? State
    {
        get => StateProvince;
        init => StateProvince = value!;
    }
}
