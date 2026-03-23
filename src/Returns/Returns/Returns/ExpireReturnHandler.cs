using Marten;
using Wolverine;

namespace Returns.Returns;

/// <summary>
/// Scheduled command that fires when an approved return is never shipped.
/// Only expires if the return is still in Approved state (no-op if already transitioned).
/// Publishes ReturnExpired integration event for Notifications BC and Orders saga.
/// </summary>
public static class ExpireReturnHandler
{
    public static async Task Handle(
        ExpireReturn command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var aggregate = await session.Events.AggregateStreamAsync<Return>(command.ReturnId, token: ct);

        // No-op if already past Approved state (customer shipped, or CS intervened)
        if (aggregate is null || aggregate.Status != ReturnStatus.Approved)
            return;

        var expired = new ReturnExpired(
            ReturnId: command.ReturnId,
            ExpiredAt: DateTimeOffset.UtcNow);

        session.Events.Append(command.ReturnId, expired);

        // Publish integration event for Notifications BC and Orders saga
        await bus.PublishAsync(new Messages.Contracts.Returns.ReturnExpired(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            ExpiredAt: expired.ExpiredAt));
    }
}
