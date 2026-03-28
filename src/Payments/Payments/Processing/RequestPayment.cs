using FluentValidation;
using Wolverine;
using Wolverine.Marten;
using IntegrationMessages = Messages.Contracts.Payments;

namespace Payments.Processing;

public sealed record RequestPayment(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string PaymentMethodToken)
{
    public class RequestPaymentValidator : AbstractValidator<RequestPayment>
    {
        public RequestPaymentValidator()
        {
            RuleFor(x => x.Amount).GreaterThan(0);
            RuleFor(x => x.OrderId).NotEmpty();
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.PaymentMethodToken).NotEmpty();
            RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
        }
    }
}

public static class RequestPaymentHandler
{
    public static async Task<(IStartStream, OutgoingMessages)> Handle(
        RequestPayment command,
        IPaymentGateway gateway,
        CancellationToken cancellationToken)
    {
        var paymentId = Guid.CreateVersion7();
        var initiatedAt = DateTimeOffset.UtcNow;

        var initiated = new PaymentInitiated(
            paymentId,
            command.OrderId,
            command.CustomerId,
            command.Amount,
            command.Currency,
            command.PaymentMethodToken,
            initiatedAt);

        var result = await gateway.CaptureAsync(
            command.Amount,
            command.Currency,
            command.PaymentMethodToken,
            cancellationToken);

        var processedAt = DateTimeOffset.UtcNow;

        var outgoing = new OutgoingMessages();

        if (!result.Success)
        {
            var failedEvent = new PaymentFailed(
                paymentId,
                result.FailureReason ?? "Unknown capture failure",
                result.IsRetriable,
                processedAt);

            var stream = MartenOps.StartStream<Payment>(paymentId, initiated, failedEvent);

            outgoing.Add(new IntegrationMessages.PaymentFailed(
                paymentId,
                command.OrderId,
                result.FailureReason ?? "Unknown capture failure",
                result.IsRetriable,
                processedAt));

            return (stream, outgoing);
        }

        var capturedEvent = new PaymentCaptured(
            paymentId,
            result.TransactionId!,
            processedAt);

        var successStream = MartenOps.StartStream<Payment>(paymentId, initiated, capturedEvent);

        outgoing.Add(new IntegrationMessages.PaymentCaptured(
            paymentId,
            command.OrderId,
            command.Amount,
            result.TransactionId!,
            processedAt));

        return (successStream, outgoing);
    }
}
