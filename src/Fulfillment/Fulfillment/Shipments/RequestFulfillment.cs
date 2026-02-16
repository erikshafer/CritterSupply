using FluentValidation;
using Marten;
using Wolverine.Http;

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
    public static CreationResponse Handle(
        RequestFulfillment command,
        IDocumentSession session)
    {
        var shipmentId = Guid.CreateVersion7();

        var @event = new FulfillmentRequested(
            command.OrderId,
            command.CustomerId,
            command.ShippingAddress,
            command.LineItems,
            command.ShippingMethod,
            DateTimeOffset.UtcNow);

        session.Events.StartStream<Shipment>(shipmentId, @event);

        return new CreationResponse($"/api/fulfillment/shipments/{shipmentId}");
    }
}
