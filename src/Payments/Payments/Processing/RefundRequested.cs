using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
using IntegrationMessages = Messages.Contracts.Payments;

namespace Payments.Processing;

public sealed record RefundRequested(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount)
{
    public class RefundRequestedValidator : AbstractValidator<RefundRequested>
    {
        public RefundRequestedValidator()
        {
            RuleFor(x => x.PaymentId).NotEmpty();
            RuleFor(x => x.OrderId).NotEmpty();
            RuleFor(x => x.Amount).GreaterThan(0);
        }
    }
}

public static class RefundRequestedHandler
{
    public static ProblemDetails Before(
        RefundRequested command,
        Payment? payment)
    {
        if (payment is null)
            return new ProblemDetails
            {
                Detail = "Payment not found",
                Status = 404
            };

        if (payment.Status != PaymentStatus.Captured)
            return new ProblemDetails
            {
                Detail = $"Payment is not in captured status. Current status: {payment.Status}",
                Status = 400
            };

        if (command.Amount > payment.RefundableAmount)
            return new ProblemDetails
            {
                Detail = $"Refund amount ({command.Amount}) exceeds refundable amount ({payment.RefundableAmount})",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task<(Events, OutgoingMessages)> Handle(
        RefundRequested command,
        [WriteAggregate] Payment payment,
        IPaymentGateway gateway,
        CancellationToken cancellationToken)
    {
        var result = await gateway.RefundAsync(
            payment.TransactionId!,
            command.Amount,
            cancellationToken);

        var refundedAt = DateTimeOffset.UtcNow;

        var events = new Events();
        var outgoing = new OutgoingMessages();

        if (!result.Success)
        {
            outgoing.Add(new IntegrationMessages.RefundFailed(
                payment.Id,
                payment.OrderId,
                result.FailureReason ?? "Unknown refund failure",
                refundedAt));

            return (events, outgoing);
        }

        var newTotalRefunded = payment.TotalRefunded + command.Amount;

        var domainEvent = new PaymentRefunded(
            payment.Id,
            command.Amount,
            newTotalRefunded,
            result.TransactionId!,
            refundedAt);

        events.Add(domainEvent);

        outgoing.Add(new IntegrationMessages.RefundCompleted(
            payment.Id,
            payment.OrderId,
            command.Amount,
            result.TransactionId!,
            refundedAt));

        return (events, outgoing);
    }
}
