using Backoffice.Clients;

namespace Backoffice.E2ETests.Stubs;

/// <summary>
/// Stub implementation of ICorrespondenceClient for E2E tests.
/// Returns in-memory test data configured per scenario.
/// </summary>
public sealed class StubCorrespondenceClient : ICorrespondenceClient
{
    private readonly Dictionary<Guid, CorrespondenceDetailDto> _messages = new();
    private readonly Dictionary<Guid, List<Guid>> _customerMessages = new();

    public void AddMessage(
        Guid messageId,
        Guid customerId,
        DateTimeOffset sentAt,
        string messageType,
        string subject,
        string body,
        string deliveryStatus = "Sent")
    {
        _messages[messageId] = new CorrespondenceDetailDto(
            messageId,
            customerId,
            sentAt.UtcDateTime,
            messageType,
            subject,
            body,
            deliveryStatus,
            ErrorMessage: null);

        if (!_customerMessages.ContainsKey(customerId))
            _customerMessages[customerId] = new List<Guid>();

        _customerMessages[customerId].Add(messageId);
    }

    public Task<IReadOnlyList<CorrespondenceMessageDto>> GetMessagesForCustomerAsync(
        Guid customerId,
        int? limit = null,
        CancellationToken ct = default)
    {
        var messageIds = _customerMessages.GetValueOrDefault(customerId) ?? new List<Guid>();
        var messages = messageIds
            .Select(id => _messages.GetValueOrDefault(id))
            .Where(m => m != null)
            .OrderByDescending(m => m!.SentAt)
            .Take(limit ?? int.MaxValue)
            .Select(m => new CorrespondenceMessageDto(
                m!.Id,
                m.CustomerId,
                m.SentAt,
                m.MessageType,
                m.Subject,
                m.DeliveryStatus))
            .ToList();

        return Task.FromResult<IReadOnlyList<CorrespondenceMessageDto>>(messages);
    }

    public Task<CorrespondenceDetailDto?> GetMessageDetailAsync(Guid messageId, CancellationToken ct = default)
    {
        return Task.FromResult(_messages.GetValueOrDefault(messageId));
    }

    public void Clear()
    {
        _messages.Clear();
        _customerMessages.Clear();
    }
}
