using JasperFx.Events;
using Marten;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart;

public static class InitializeCartHandler
{
    [WolverinePost("/api/carts")]
    public static (IStartStream, CreationResponse) Handle(InitializeCart command)
    {
        var cartId = Guid.CreateVersion7();
        var @event = new CartInitialized(
            command.CustomerId,
            command.SessionId,
            DateTimeOffset.UtcNow);

        var stream = MartenOps.StartStream<Cart>(cartId, @event);

        return (stream, new CreationResponse($"/api/carts/{cartId}"));
    }
}
