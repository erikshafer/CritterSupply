namespace CustomerIdentity.AddressBook;

/// <summary>
/// Represents a customer's saved address for shipping or billing purposes.
/// Refactored for EF Core with navigation properties and private setters.
/// </summary>
public sealed class CustomerAddress
{
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }  // Foreign key
    public AddressType Type { get; private set; }
    public string Nickname { get; private set; } = string.Empty;
    public string AddressLine1 { get; private set; } = string.Empty;
    public string? AddressLine2 { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string StateOrProvince { get; private set; } = string.Empty;
    public string PostalCode { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public bool IsDefault { get; private set; }
    public bool IsVerified { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    // Navigation property (back to Customer)
    public Customer Customer { get; private set; } = null!;

    // Required by EF Core
    private CustomerAddress() { }

    internal static CustomerAddress Create(
        Guid customerId,
        AddressType type,
        string nickname,
        string addressLine1,
        string? addressLine2,
        string city,
        string stateOrProvince,
        string postalCode,
        string country,
        bool isVerified)
    {
        return new CustomerAddress
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            Type = type,
            Nickname = nickname,
            AddressLine1 = addressLine1,
            AddressLine2 = addressLine2,
            City = city,
            StateOrProvince = stateOrProvince,
            PostalCode = postalCode,
            Country = country,
            IsDefault = false,
            IsVerified = isVerified,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUsedAt = null
        };
    }

    /// <summary>
    /// Indicates if this address can be used for shipping purposes.
    /// </summary>
    public bool IsShipping => Type is AddressType.Shipping or AddressType.Both;

    /// <summary>
    /// Indicates if this address can be used for billing purposes.
    /// </summary>
    public bool IsBilling => Type is AddressType.Billing or AddressType.Both;

    /// <summary>
    /// Returns a display-friendly single-line representation of the address.
    /// Example: "123 Main St, Seattle, WA 98101, USA"
    /// </summary>
    public string DisplayLine =>
        AddressLine2 is null
            ? $"{AddressLine1}, {City}, {StateOrProvince} {PostalCode}, {Country}"
            : $"{AddressLine1}, {AddressLine2}, {City}, {StateOrProvince} {PostalCode}, {Country}";

    public void MarkAsVerified()
    {
        IsVerified = true;
    }

    public void SetAsDefault(bool isDefault = true)
    {
        IsDefault = isDefault;
    }

    public void UpdateLastUsedAt(DateTimeOffset timestamp)
    {
        LastUsedAt = timestamp;
    }

    public CustomerAddress Update(
        AddressType? type = null,
        string? nickname = null,
        string? addressLine1 = null,
        string? addressLine2 = null,
        string? city = null,
        string? stateOrProvince = null,
        string? postalCode = null,
        string? country = null,
        bool? isVerified = null)
    {
        if (type.HasValue) Type = type.Value;
        if (nickname is not null) Nickname = nickname;
        if (addressLine1 is not null) AddressLine1 = addressLine1;
        if (addressLine2 is not null) AddressLine2 = addressLine2;
        if (city is not null) City = city;
        if (stateOrProvince is not null) StateOrProvince = stateOrProvince;
        if (postalCode is not null) PostalCode = postalCode;
        if (country is not null) Country = country;
        if (isVerified.HasValue) IsVerified = isVerified.Value;

        return this;
    }
}
