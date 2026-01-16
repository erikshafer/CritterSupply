namespace CustomerIdentity.AddressBook;

/// <summary>
/// Represents the verification status of a customer address.
/// </summary>
public enum VerificationStatus
{
    /// <summary>
    /// Address has not been verified against postal service databases.
    /// </summary>
    Unverified,

    /// <summary>
    /// Address has been verified and confirmed as deliverable.
    /// </summary>
    Verified,

    /// <summary>
    /// Address is valid but required corrections (e.g., standardized formatting).
    /// </summary>
    Corrected,

    /// <summary>
    /// Address could not be verified or is not deliverable.
    /// </summary>
    Invalid,

    /// <summary>
    /// Address is partially valid but missing details (e.g., apartment number).
    /// </summary>
    PartiallyValid
}
