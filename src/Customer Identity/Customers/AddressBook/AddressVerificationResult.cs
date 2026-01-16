namespace CustomerIdentity.AddressBook;

/// <summary>
/// Represents the result of an address verification operation.
/// </summary>
public sealed record AddressVerificationResult(
    VerificationStatus Status,
    string? ErrorMessage,
    CorrectedAddress? SuggestedAddress,
    double? ConfidenceScore);

/// <summary>
/// Represents a corrected or standardized address returned by the verification service.
/// </summary>
public sealed record CorrectedAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country);
