using Correspondence.Providers;
using Messages.Contracts.Correspondence;
using Wolverine;
using Wolverine.Marten;

namespace Correspondence.Messages;

/// <summary>
/// Handler for SendMessage command. Implements retry logic with exponential backoff.
/// Retry schedule: 3 attempts (5 min, 30 min, 2 hr)
/// </summary>
public static class SendMessageHandler
{
    public static HandlerContinuation Before(SendMessage command, Message? message)
    {
        if (message is null) return HandlerContinuation.Stop;
        if (message.Status == MessageStatus.Delivered) return HandlerContinuation.Stop;
        if (message.Status == MessageStatus.Skipped) return HandlerContinuation.Stop;
        return HandlerContinuation.Continue;
    }

    public static async Task<(Events, OutgoingMessages)> Handle(
        SendMessage command,
        [WriteAggregate] Message message,
        IEmailProvider emailProvider,
        CancellationToken ct)
    {
        var emailMessage = new EmailMessage(
            ToEmail: "customer@example.com", // TODO: will be populated from CustomerIdentity query in integration handlers
            ToName: "Customer",
            Subject: message.Subject,
            HtmlBody: message.Body,
            CorrespondenceMessageId: message.Id.ToString());

        try
        {
            var result = await emailProvider.SendEmailAsync(emailMessage, ct);

            if (result.Success)
            {
                var delivered = new MessageDelivered(
                    message.Id,
                    DateTimeOffset.UtcNow,
                    message.AttemptCount + 1,
                    result.ProviderId ?? "unknown");

                var events = new Events();
                events.Add(delivered);

                var outgoing = new OutgoingMessages();
                outgoing.Add(new CorrespondenceDelivered(
                    message.Id,
                    message.CustomerId,
                    message.Channel,
                    delivered.DeliveredAt,
                    delivered.AttemptNumber));

                return (events, outgoing);
            }

            return BuildFailureResult(message, message.AttemptCount + 1,
                result.FailureReason ?? "Unknown error", result.IsRetriable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BuildFailureResult(message, message.AttemptCount + 1,
                ex.Message, isRetriable: true);
        }
    }

    private static (Events, OutgoingMessages) BuildFailureResult(
        Message message, int attemptNumber, string reason, bool isRetriable)
    {
        var failed = new DeliveryFailed(
            message.Id,
            attemptNumber,
            DateTimeOffset.UtcNow,
            reason,
            string.Empty);

        var events = new Events();
        events.Add(failed);

        var outgoing = new OutgoingMessages();

        if (attemptNumber < 3 && isRetriable)
        {
            var delay = attemptNumber switch
            {
                1 => TimeSpan.FromMinutes(5),
                2 => TimeSpan.FromMinutes(30),
                _ => TimeSpan.FromHours(2)
            };
            outgoing.Add(new SendMessage(message.Id).DelayedFor(delay));
        }
        else
        {
            outgoing.Add(new CorrespondenceFailed(
                message.Id,
                message.CustomerId,
                message.Channel,
                reason,
                failed.FailedAt));
        }

        return (events, outgoing);
    }
}
