using FluentValidation;

namespace Fulfillment.Shipments;

/// <summary>
/// Command to request fulfillment for an order.
/// </summary>
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
