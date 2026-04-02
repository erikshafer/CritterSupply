using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace Orders.Checkout;

/// <summary>
/// Request body for providing a checkout shipping address.
/// </summary>
public sealed record ProvideShippingAddressRequest(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country)
{
    public class ProvideShippingAddressRequestValidator : AbstractValidator<ProvideShippingAddressRequest>
    {
        public ProvideShippingAddressRequestValidator()
        {
            RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(200);
            RuleFor(x => x.AddressLine2).MaximumLength(200);
            RuleFor(x => x.City).NotEmpty().MaximumLength(100);
            RuleFor(x => x.StateOrProvince).NotEmpty().MaximumLength(100);
            RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
            RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
        }
    }
}

/// <summary>
/// Direct Implementation pattern — compound handler [WriteAggregate] silently fails
/// to persist events when mixing route + body parameters (M32.3 discovery).
/// </summary>
public static class ProvideShippingAddressHandler
{
    [WolverinePost("/api/checkouts/{checkoutId}/shipping-address")]
    public static async Task<IResult> Handle(
        Guid checkoutId,
        ProvideShippingAddressRequest request,
        IDocumentSession session,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<Checkout>(checkoutId, ct);
        var checkout = stream.Aggregate;

        if (checkout is null)
            return Results.NotFound(new { detail = "Checkout not found" });

        if (checkout.IsCompleted)
            return Results.BadRequest(new { detail = "Cannot modify a completed checkout" });

        var @event = new ShippingAddressProvided(
            request.AddressLine1,
            request.AddressLine2,
            request.City,
            request.StateOrProvince,
            request.PostalCode,
            request.Country,
            DateTimeOffset.UtcNow);

        stream.AppendOne(@event);
        await session.SaveChangesAsync(ct);

        return Results.Ok(@event);
    }
}
