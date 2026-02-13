using FluentValidation;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;
using Wolverine.Marten;

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

public static class InitializeCartHandler
{
    [WolverinePost("/api/carts")]
    public static (CreationResponse<Guid>, IStartStream) Handle(InitializeCart command)
    {
        var cartId = Guid.CreateVersion7();
        var @event = new CartInitialized(
            command.CustomerId,
            command.SessionId,
            DateTimeOffset.UtcNow);

        var stream = MartenOps.StartStream<Cart>(cartId, @event);

        // CRITICAL: CreationResponse MUST come first in the tuple!
        // First item = HTTP response, Second item = side effect (stream creation)
        var response = new CreationResponse<Guid>($"/api/carts/{cartId}", cartId);

        return (response, stream);
    }
}
