namespace Backoffice.Composition;

/// <summary>
/// Composition view for customer correspondence history (CS workflow: message history lookup)
/// Aggregates message list from Correspondence BC with customer context
/// </summary>
public sealed record CorrespondenceHistoryView(
    Guid CustomerId,
    string CustomerEmail,
    IReadOnlyList<CorrespondenceMessageView> Messages);

/// <summary>
/// Message view model for correspondence history
/// </summary>
public sealed record CorrespondenceMessageView(
    Guid MessageId,
    DateTime SentAt,
    string MessageType,
    string Subject,
    string DeliveryStatus);
