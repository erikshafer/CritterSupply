using FluentValidation;

namespace Inventory.Api.Commands;

/// <summary>
/// Validator for AdjustInventoryRequest.
/// Ensures non-zero adjustment quantities and non-empty audit trail fields.
/// </summary>
public sealed class AdjustInventoryRequestValidator : AbstractValidator<AdjustInventoryRequest>
{
    public AdjustInventoryRequestValidator()
    {
        RuleFor(x => x.AdjustmentQuantity)
            .NotEqual(0)
            .WithMessage("Adjustment quantity must be non-zero");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Reason is required")
            .MaximumLength(500)
            .WithMessage("Reason cannot exceed 500 characters");

        RuleFor(x => x.AdjustedBy)
            .NotEmpty()
            .WithMessage("AdjustedBy is required")
            .MaximumLength(100)
            .WithMessage("AdjustedBy cannot exceed 100 characters");
    }
}
