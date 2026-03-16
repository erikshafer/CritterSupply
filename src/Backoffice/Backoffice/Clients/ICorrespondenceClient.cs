namespace Backoffice.Clients;

/// <summary>
/// HTTP client for querying Correspondence BC (admin use)
/// </summary>
public interface ICorrespondenceClient
{
    /// <summary>
    /// Get correspondence messages for a customer (CS workflow: message history lookup)
    /// </summary>
    Task<IReadOnlyList<CorrespondenceMessageDto>> GetMessagesForCustomerAsync(
        Guid customerId,
        int? limit = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get detailed message information
    /// </summary>
    Task<CorrespondenceDetailDto?> GetMessageDetailAsync(Guid messageId, CancellationToken ct = default);
}

/// <summary>
/// Correspondence message DTO from Correspondence BC
/// </summary>
public sealed record CorrespondenceMessageDto(
    Guid Id,
    Guid CustomerId,
    DateTime SentAt,
    string MessageType,
    string Subject,
    string DeliveryStatus);

/// <summary>
/// Correspondence detail DTO
/// </summary>
public sealed record CorrespondenceDetailDto(
    Guid Id,
    Guid CustomerId,
    DateTime SentAt,
    string MessageType,
    string Subject,
    string Body,
    string DeliveryStatus,
    string? ErrorMessage);
