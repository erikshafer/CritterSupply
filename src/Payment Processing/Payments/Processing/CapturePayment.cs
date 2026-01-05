using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
using IntegrationMessages = Messages.Contracts.Payments;

namespace Payments.Processing;

/// <summary>
/// Command from Orders context requesting capture of a previously authorized payment.
/// </summary>
public sealed record CapturePayment(
    Guid PaymentId,
    Guid OrderId,
    decimal? AmountToCapture = null) // null = capture full authorized amount
{
    public class CapturePaymentValidator : AbstractValidator<CapturePayment>
    {
        public CapturePaymentValidator()
        {
            RuleFor(x => x.PaymentId).NotEmpty();
            RuleFor(x => x.OrderId).NotEmpty();
            RuleFor(x => x.AmountToCapture)
                .GreaterThan(0)
                .When(x => x.AmountToCapture.HasValue);
        }
    }
}

/// <summary>
/// Wolverine handler for CapturePayment commands.
/// Processes capture requests for previously authorized payments.
/// </summary>
public static class CapturePaymentHandler
{
    /// <summary>
    /// Pre-validation before handling capture command.
    /// Checks payment exists, is in correct status, and authorization hasn't expired.
    /// </summary>
    public static ProblemDetails Before(
        CapturePayment command,
        Payment? payment)
    {
        if (payment is null)
            return new ProblemDetails
            {
                Detail = "Payment not found",
                Status = 404
            };

        if (payment.Status != PaymentStatus.Authorized)
            return new ProblemDetails
            {
                Detail = $"Payment is not in authorized status. Current status: {payment.Status}",
                Status = 400
            };

        if (payment.AuthorizationExpiresAt.HasValue &&
            payment.AuthorizationExpiresAt.Value < DateTimeOffset.UtcNow)
            return new ProblemDetails
            {
                Detail = "Authorization has expired",
                Status = 400
            };

        var amountToCapture = command.AmountToCapture ?? payment.Amount;
        if (amountToCapture > payment.Amount)
            return new ProblemDetails
            {
                Detail = $"Capture amount ({amountToCapture}) exceeds authorized amount ({payment.Amount})",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Handles CapturePayment command using Wolverine Aggregate Handler Workflow.
    /// Returns domain events and integration messages for Orders context.
    /// Handles both success (PaymentCaptured) and failure (PaymentFailed) cases.
    /// </summary>
    public static async Task<(Events, OutgoingMessages)> Handle(
        CapturePayment command,
        [WriteAggregate] Payment payment,
        IPaymentGateway gateway,
        CancellationToken cancellationToken)
    {
        var amountToCapture = command.AmountToCapture ?? payment.Amount;

        var result = await gateway.CaptureAuthorizedAsync(
            payment.AuthorizationId!,
            amountToCapture,
            cancellationToken);

        var capturedAt = DateTimeOffset.UtcNow;

        var events = new Events();
        var outgoing = new OutgoingMessages();

        if (!result.Success)
        {
            var failedEvent = new PaymentFailed(
                payment.Id,
                result.FailureReason ?? "Unknown capture failure",
                result.IsRetriable,
                capturedAt);

            events.Add(failedEvent);

            outgoing.Add(new IntegrationMessages.PaymentFailed(
                payment.Id,
                payment.OrderId,
                result.FailureReason ?? "Unknown capture failure",
                result.IsRetriable,
                capturedAt));

            return (events, outgoing);
        }

        var domainEvent = new PaymentCaptured(
            payment.Id,
            result.TransactionId!,
            capturedAt);

        events.Add(domainEvent);

        outgoing.Add(new IntegrationMessages.PaymentCaptured(
            payment.Id,
            payment.OrderId,
            payment.Amount,
            result.TransactionId!,
            capturedAt));

        return (events, outgoing);
    }
}
