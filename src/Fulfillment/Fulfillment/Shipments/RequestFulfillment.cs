using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;
using Wolverine.Marten;

namespace Fulfillment.Shipments;

public sealed record RequestFulfillment(
    Guid OrderId,
    Guid CustomerId,
    ShippingAddress ShippingAddress,
    IReadOnlyList<FulfillmentLineItem> LineItems,
    string ShippingMethod)
{
    public class RequestFulfillmentValidator : AbstractValidator<RequestFulfillment>
    {
        public RequestFulfillmentValidator()
        {
            RuleFor(x => x.OrderId).NotEmpty();
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.ShippingAddress).NotNull();
            RuleFor(x => x.LineItems).NotEmpty();
            RuleFor(x => x.ShippingMethod).NotEmpty().MaximumLength(50);
        }
    }
}

public static class RequestFulfillmentHandler
{
    [WolverinePost("/api/fulfillment/shipments")]
    [Authorize]
    public static (CreationResponse, IStartStream) Handle(RequestFulfillment command)
    {
        var shipmentId = Guid.CreateVersion7();

        var @event = new FulfillmentRequested(
            command.OrderId,
            command.CustomerId,
            command.ShippingAddress,
            command.LineItems,
            command.ShippingMethod,
            DateTimeOffset.UtcNow);

        var stream = MartenOps.StartStream<Shipment>(shipmentId, @event);

        return (new CreationResponse($"/api/fulfillment/shipments/{shipmentId}"), stream);
    }
}
