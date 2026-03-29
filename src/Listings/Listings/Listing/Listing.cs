namespace Listings.Listing;

/// <summary>
/// Event-sourced aggregate representing a product's selling presence on a marketplace channel.
/// Write-only model — contains no behavior, only Create() factory and Apply() methods.
/// All business logic and state machine guards live in command handlers (Decider pattern).
/// </summary>
public sealed record Listing(
    Guid Id,
    string Sku,
    string ChannelCode,
    string ProductName,
    string? Content,
    ListingStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? EndedAt,
    EndedCause? EndCause,
    string? PauseReason)
{
    public static Listing Create(ListingDraftCreated @event) =>
        new(@event.ListingId,
            @event.Sku,
            @event.ChannelCode,
            @event.ProductName,
            @event.InitialContent,
            ListingStatus.Draft,
            @event.OccurredAt,
            null,
            null,
            null,
            null);

    public Listing Apply(ListingSubmittedForReview _) =>
        this with { Status = ListingStatus.ReadyForReview };

    public Listing Apply(ListingApproved _) =>
        this with { Status = ListingStatus.Submitted };

    public Listing Apply(ListingActivated @event) =>
        this with
        {
            Status = ListingStatus.Live,
            ActivatedAt = @event.OccurredAt
        };

    public Listing Apply(ListingPaused @event) =>
        this with
        {
            Status = ListingStatus.Paused,
            PauseReason = @event.Reason
        };

    public Listing Apply(ListingResumed _) =>
        this with
        {
            Status = ListingStatus.Live,
            PauseReason = null
        };

    public Listing Apply(ListingEnded @event) =>
        this with
        {
            Status = ListingStatus.Ended,
            EndedAt = @event.OccurredAt,
            EndCause = @event.Cause
        };

    public Listing Apply(ListingForcedDown @event) =>
        this with
        {
            Status = ListingStatus.Ended,
            EndedAt = @event.OccurredAt,
            EndCause = EndedCause.ProductDiscontinued
        };

    public Listing Apply(ListingContentUpdated @event) =>
        this with
        {
            ProductName = @event.ProductName ?? ProductName,
            Content = @event.Description ?? Content
        };

    /// <summary>
    /// Whether this listing is in a terminal state.
    /// </summary>
    public bool IsTerminal => Status == ListingStatus.Ended;
}
