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

public sealed class SubmitInspectionValidator : AbstractValidator<SubmitInspection>
{
    public SubmitInspectionValidator()
    {
        RuleFor(x => x.ReturnId).NotEmpty().WithMessage("ReturnId is required.");
        RuleFor(x => x.Results).NotEmpty().WithMessage("At least one inspection result is required.");

        RuleForEach(x => x.Results).ChildRules(result =>
        {
            result.RuleFor(r => r.Sku).NotEmpty().WithMessage("SKU is required.");
            result.RuleFor(r => r.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
            result.RuleFor(r => r.Condition).IsInEnum().WithMessage("Item condition is invalid.");
            result.RuleFor(r => r.Disposition).IsInEnum().WithMessage("Disposition decision is invalid.");
        });
    }
}

public sealed class DenyReturnValidator : AbstractValidator<DenyReturn>
{
    public DenyReturnValidator()
    {
        RuleFor(x => x.ReturnId).NotEmpty().WithMessage("ReturnId is required.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Denial reason is required.");
        RuleFor(x => x.Message).NotEmpty().WithMessage("Denial message is required.");
    }
}
