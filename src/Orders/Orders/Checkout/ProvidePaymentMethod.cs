using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace Orders.Checkout;

/// <summary>
/// Request body for providing a checkout payment method.
/// </summary>
public sealed record ProvidePaymentMethodRequest(
    string PaymentMethodToken)
{
    public class ProvidePaymentMethodRequestValidator : AbstractValidator<ProvidePaymentMethodRequest>
    {
        public ProvidePaymentMethodRequestValidator()
        {
            RuleFor(x => x.PaymentMethodToken).NotEmpty().MaximumLength(500);
        }
    }
}

/// <summary>
/// Direct Implementation pattern — see ProvideShippingAddress.cs for rationale.
/// </summary>
public static class ProvidePaymentMethodHandler
{
    [WolverinePost("/api/checkouts/{checkoutId}/payment-method")]
    public static async Task<IResult> Handle(
        Guid checkoutId,
        ProvidePaymentMethodRequest request,
        IDocumentSession session,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<Checkout>(checkoutId, ct);
        var checkout = stream.Aggregate;

        if (checkout is null)
            return Results.NotFound(new { detail = "Checkout not found" });

        if (checkout.IsCompleted)
            return Results.BadRequest(new { detail = "Cannot modify a completed checkout" });

        var @event = new PaymentMethodProvided(
            request.PaymentMethodToken,
            DateTimeOffset.UtcNow);

        stream.AppendOne(@event);
        await session.SaveChangesAsync(ct);

        return Results.Ok(@event);
    }
}
