using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Checkout;

public sealed record ProvidePaymentMethod(
    Guid CheckoutId,
    string PaymentMethodToken)
{
    public class ProvidePaymentMethodValidator : AbstractValidator<ProvidePaymentMethod>
    {
        public ProvidePaymentMethodValidator()
        {
            RuleFor(x => x.CheckoutId).NotEmpty();
            RuleFor(x => x.PaymentMethodToken).NotEmpty().MaximumLength(500);
        }
    }
}

public static class ProvidePaymentMethodHandler
{
    public static ProblemDetails Before(
        ProvidePaymentMethod command,
        Checkout? checkout)
    {
        if (checkout is null)
            return new ProblemDetails { Detail = "Checkout not found", Status = 404 };

        if (checkout.IsCompleted)
            return new ProblemDetails
            {
                Detail = "Cannot modify a completed checkout",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/checkouts/{checkoutId}/payment-method")]
    public static PaymentMethodProvided Handle(
        ProvidePaymentMethod command,
        [WriteAggregate] Checkout checkout)
    {
        return new PaymentMethodProvided(
            command.PaymentMethodToken,
            DateTimeOffset.UtcNow);
    }
}
