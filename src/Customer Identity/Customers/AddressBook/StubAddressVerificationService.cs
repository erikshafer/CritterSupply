namespace CustomerIdentity.AddressBook;

/// <summary>
/// Stub implementation of address verification service for development and testing.
/// Always returns verified status without calling external services.
/// </summary>
public sealed class StubAddressVerificationService : IAddressVerificationService
{
    public Task<AddressVerificationResult> VerifyAsync(
        string addressLine1,
        string? addressLine2,
        string city,
        string stateOrProvince,
        string postalCode,
        string country,
        CancellationToken ct)
    {
        // Return the address as-is with verified status
        var corrected = new CorrectedAddress(
            addressLine1,
            addressLine2,
            city,
            stateOrProvince,
            postalCode,
            country);

        var result = new AddressVerificationResult(
            Status: VerificationStatus.Verified,
            ErrorMessage: null,
            SuggestedAddress: corrected,
            ConfidenceScore: 1.0);

        return Task.FromResult(result);
    }
}
