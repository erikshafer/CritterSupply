using FluentValidation;
using Wolverine;
using Wolverine.Marten;
using IntegrationMessages = Messages.Contracts.Payments;

namespace Payments.Processing;

public sealed record AuthorizePayment(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string PaymentMethodToken)
{
    public class AuthorizePaymentValidator : AbstractValidator<AuthorizePayment>
    {
        public AuthorizePaymentValidator()
        {
            RuleFor(x => x.OrderId).NotEmpty();
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.Amount).GreaterThan(0);
            RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
            RuleFor(x => x.PaymentMethodToken).NotEmpty().MaximumLength(256);
        }
    }
}

public static class AuthorizePaymentHandler
{
    public static async Task<(IStartStream, OutgoingMessages)> Handle(
        AuthorizePayment command,
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

        var result = await gateway.AuthorizeAsync(
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
                result.FailureReason ?? "Unknown authorization failure",
                result.IsRetriable,
                processedAt);

            var stream = MartenOps.StartStream<Payment>(paymentId, initiated, failedEvent);

            outgoing.Add(new IntegrationMessages.PaymentFailed(
                paymentId,
                command.OrderId,
                result.FailureReason ?? "Unknown authorization failure",
                result.IsRetriable,
                processedAt));

            return (stream, outgoing);
        }

        var expiresAt = processedAt.AddDays(7);

        var authorizedEvent = new PaymentAuthorized(
            paymentId,
            result.TransactionId!,
            processedAt);

        var successStream = MartenOps.StartStream<Payment>(paymentId, initiated, authorizedEvent);

        outgoing.Add(new IntegrationMessages.PaymentAuthorized(
            paymentId,
            command.OrderId,
            command.Amount,
            result.TransactionId!,
            processedAt,
            expiresAt));

        return (successStream, outgoing);
    }
}
