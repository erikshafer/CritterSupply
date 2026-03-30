namespace Listings.Listing;

/// <summary>
/// The lifecycle status of a Listing.
/// See the glossary for the canonical definition of each state.
/// </summary>
public enum ListingStatus
{
    Draft,
    ReadyForReview,
    Submitted,
    Live,
    Paused,
    Ended
}
