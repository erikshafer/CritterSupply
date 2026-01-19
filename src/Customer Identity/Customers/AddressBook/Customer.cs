namespace CustomerIdentity.AddressBook;

/// <summary>
/// Customer aggregate root for EF Core relational model.
/// Demonstrates navigation properties and one-to-many relationships.
/// </summary>
public sealed class Customer
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    // Navigation property (EF Core one-to-many)
    public ICollection<CustomerAddress> Addresses { get; private set; } = new List<CustomerAddress>();

    // Required by EF Core
    private Customer() { }

    public static Customer Create(Guid id, string email, string firstName, string lastName)
    {
        return new Customer
        {
            Id = id,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public CustomerAddress AddAddress(
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
        var address = CustomerAddress.Create(
            Id,
            type,
            nickname,
            addressLine1,
            addressLine2,
            city,
            stateOrProvince,
            postalCode,
            country,
            isVerified);

        Addresses.Add(address);
        return address;
    }
}
