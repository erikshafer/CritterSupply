using FluentValidation;

namespace Returns.Returns;

public sealed class RequestReturnValidator : AbstractValidator<RequestReturn>
{
    public RequestReturnValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("OrderId is required.");
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("CustomerId is required.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one item must be included in the return request.");

        RuleForEach(x => x.Items).SetValidator(new RequestReturnItemValidator());
    }
}

public sealed class RequestReturnItemValidator : AbstractValidator<RequestReturnItem>
{
    public RequestReturnItemValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().WithMessage("SKU is required.");
        RuleFor(x => x.ProductName).NotEmpty().WithMessage("Product name is required.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        RuleFor(x => x.UnitPrice).GreaterThan(0).WithMessage("Unit price must be greater than zero.");
        RuleFor(x => x.Reason).IsInEnum().WithMessage("Return reason is invalid.");
    }
}
