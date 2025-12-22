using FluentValidation;

namespace Shopping.Cart;

public sealed record ClearCart(
    Guid CartId,
    string? Reason)
{
    public class ClearCartValidator : AbstractValidator<ClearCart>
    {
        public ClearCartValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
            RuleFor(x => x.Reason).MaximumLength(200);
        }
    }
}
