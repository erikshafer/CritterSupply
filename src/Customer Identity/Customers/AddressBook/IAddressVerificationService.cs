namespace CustomerIdentity.AddressBook;

/// <summary>
/// Service interface for verifying customer addresses against postal service databases.
/// Implementations can be real (SmartyStreets, Google Address Validation) or stubs for testing.
/// </summary>
public interface IAddressVerificationService
{
    /// <summary>
    /// Verifies an address and returns a result with verification status and suggested corrections.
    /// </summary>
    /// <param name="addressLine1">Primary street address</param>
    /// <param name="addressLine2">Secondary address (apartment, suite, etc.) - optional</param>
    /// <param name="city">City name</param>
    /// <param name="stateOrProvince">State or province abbreviation</param>
    /// <param name="postalCode">Postal code / ZIP code</param>
    /// <param name="country">ISO 2-letter country code (e.g., US, CA, GB)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Verification result with status and suggested corrections</returns>
    Task<AddressVerificationResult> VerifyAsync(
        string addressLine1,
        string? addressLine2,
        string city,
        string stateOrProvince,
        string postalCode,
        string country,
        CancellationToken ct);
}
