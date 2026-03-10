namespace VendorPortal.ChangeRequests;

/// <summary>
/// A single vendor response to a "Needs More Info" question from the Catalog BC.
/// Stored as a structured list on <see cref="ChangeRequest.InfoResponses"/> rather than
/// being concatenated into the <see cref="ChangeRequest.AdditionalNotes"/> string,
/// allowing the UI to render each Q&amp;A round as a distinct threaded entry.
/// </summary>
/// <param name="Response">The vendor's answer to the Catalog BC question.</param>
/// <param name="RespondedAt">When the vendor submitted this response.</param>
public sealed record VendorInfoResponse(
    string Response,
    DateTimeOffset RespondedAt);
