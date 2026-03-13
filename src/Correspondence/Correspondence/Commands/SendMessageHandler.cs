using Correspondence.Messages;
using Correspondence.Providers;
using Marten;
using Messages.Contracts.Correspondence;
using Wolverine;

namespace Correspondence.Commands;

/// <summary>
/// Handler for SendMessage command. Implements retry logic with exponential backoff.
/// Retry schedule: 3 attempts (5 min, 30 min, 2 hr)
/// </summary>
public sealed class SendMessageHandler
{
    public async Task<OutgoingMessages> Handle(
        SendMessage command,
        IDocumentSession session,
        IEmailProvider emailProvider)
    {
        var message = await session.Events.AggregateStreamAsync<Message>(command.MessageId);

        if (message is null)
        {
            // Message doesn't exist - this shouldn't happen
            return [];
        }

        if (message.Status == MessageStatus.Delivered)
        {
            // Idempotency: already sent
            return [];
        }

        if (message.Status == MessageStatus.Skipped)
        {
            // Message was skipped (customer opted out)
            return [];
        }

        try
        {
            // Send via email provider
            var emailMessage = new EmailMessage(
                ToEmail: "customer@example.com", // TODO: will be populated from CustomerIdentity query in integration handlers
                ToName: "Customer",
                Subject: message.Subject,
                HtmlBody: message.Body,
                CorrespondenceMessageId: message.Id.ToString()
            );

            var result = await emailProvider.SendEmailAsync(emailMessage, CancellationToken.None);

            if (result.Success)
            {
                // Success - record delivery
                var delivered = new MessageDelivered(
                    message.Id,
                    DateTimeOffset.UtcNow,
                    message.AttemptCount + 1,
                    result.ProviderId ?? "unknown"
                );

                session.Events.Append(message.Id, delivered);

                var outgoing = new OutgoingMessages();
                outgoing.Add(new CorrespondenceDelivered(
                    message.Id,
                    message.CustomerId,
                    message.Channel,
                    delivered.DeliveredAt,
                    delivered.AttemptNumber
                ));
                return outgoing;
            }
            else
            {
                // Provider returned failure
                var failed = new DeliveryFailed(
                    message.Id,
                    message.AttemptCount + 1,
                    DateTimeOffset.UtcNow,
                    result.FailureReason ?? "Unknown error",
                    "Provider error"
                );

                session.Events.Append(message.Id, failed);

                // Retry logic: exponential backoff
                if (failed.AttemptNumber < 3 && result.IsRetriable)
                {
                    var delay = failed.AttemptNumber switch
                    {
                        1 => TimeSpan.FromMinutes(5),
                        2 => TimeSpan.FromMinutes(30),
                        _ => TimeSpan.FromHours(2)
                    };

                    var outgoing = new OutgoingMessages();
                    outgoing.Add(new SendMessage(message.Id).DelayedFor(delay));
                    return outgoing;
                }

                // Permanent failure after 3 attempts or non-retriable error
                var failureOutgoing = new OutgoingMessages();
                failureOutgoing.Add(new CorrespondenceFailed(
                    message.Id,
                    message.CustomerId,
                    message.Channel,
                    failed.ErrorMessage,
                    failed.FailedAt
                ));
                return failureOutgoing;
            }
        }
        catch (Exception ex)
        {
            // Exception during send (network error, etc.)
            var failed = new DeliveryFailed(
                message.Id,
                message.AttemptCount + 1,
                DateTimeOffset.UtcNow,
                ex.Message,
                ex.ToString()
            );

            session.Events.Append(message.Id, failed);

            // Retry logic
            if (failed.AttemptNumber < 3)
            {
                var delay = failed.AttemptNumber switch
                {
                    1 => TimeSpan.FromMinutes(5),
                    2 => TimeSpan.FromMinutes(30),
                    _ => TimeSpan.FromHours(2)
                };

                var outgoing = new OutgoingMessages();
                outgoing.Add(new SendMessage(message.Id).DelayedFor(delay));
                return outgoing;
            }

            // Permanent failure after 3 attempts
            var failureOutgoing = new OutgoingMessages();
            failureOutgoing.Add(new CorrespondenceFailed(
                message.Id,
                message.CustomerId,
                message.Channel,
                failed.ErrorMessage,
                failed.FailedAt
            ));
            return failureOutgoing;
        }
    }
}
