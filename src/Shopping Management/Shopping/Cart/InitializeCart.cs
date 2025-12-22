using FluentValidation;

namespace Shopping.Cart;

public sealed record InitializeCart(
    Guid? CustomerId,
    string? SessionId)
{
    public class InitializeCartValidator : AbstractValidator<InitializeCart>
    {
        public InitializeCartValidator()
        {
            RuleFor(x => x)
                .Must(x => x.CustomerId.HasValue || !string.IsNullOrWhiteSpace(x.SessionId))
                .WithMessage("Either CustomerId or SessionId must be provided");
        }
    }
}
