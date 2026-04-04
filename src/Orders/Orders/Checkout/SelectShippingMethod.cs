using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace Orders.Checkout;

/// <summary>
/// Request body for selecting a checkout shipping method.
/// </summary>
public sealed record SelectShippingMethodRequest(
    string ShippingMethod,
    decimal ShippingCost)
{
    public class SelectShippingMethodRequestValidator : AbstractValidator<SelectShippingMethodRequest>
    {
        public SelectShippingMethodRequestValidator()
        {
            RuleFor(x => x.ShippingMethod).NotEmpty().MaximumLength(100);
            RuleFor(x => x.ShippingCost).GreaterThanOrEqualTo(0);
        }
    }
}

/// <summary>
/// Mixed route + body parameter handler — uses FetchForWriting pattern.
/// AutoApplyTransactions() in Orders.Api handles SaveChangesAsync automatically.
/// </summary>
public static class SelectShippingMethodHandler
{
    [WolverinePost("/api/checkouts/{checkoutId}/shipping-method")]
    public static async Task<IResult> Handle(
        Guid checkoutId,
        SelectShippingMethodRequest request,
        IDocumentSession session,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<Checkout>(checkoutId, ct);
        var checkout = stream.Aggregate;

        if (checkout is null)
            return Results.NotFound(new { detail = "Checkout not found" });

        if (checkout.IsCompleted)
            return Results.BadRequest(new { detail = "Cannot modify a completed checkout" });

        var @event = new ShippingMethodSelected(
            request.ShippingMethod,
            request.ShippingCost,
            DateTimeOffset.UtcNow);

        stream.AppendOne(@event);

        return Results.Ok(@event);
    }
}
