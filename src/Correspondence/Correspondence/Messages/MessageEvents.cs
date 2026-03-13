namespace Correspondence.Messages;

// Domain Events (internal to Correspondence BC - stored in Marten event stream)

public sealed record MessageQueued(
    Guid MessageId,
    Guid CustomerId,
    string Channel,
    string TemplateId,
    string Subject,
    string Body,
    DateTimeOffset QueuedAt
);

public sealed record MessageDelivered(
    Guid MessageId,
    DateTimeOffset DeliveredAt,
    int AttemptNumber,
    string ProviderResponse
);

public sealed record DeliveryFailed(
    Guid MessageId,
    int AttemptNumber,
    DateTimeOffset FailedAt,
    string ErrorMessage,
    string ProviderResponse
);

public sealed record MessageSkipped(
    Guid MessageId,
    string Reason // "Customer opted out of email" or "Channel disabled"
);
