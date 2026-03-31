using JasperFx.Events;

namespace Shopping.UnitTests.Cart;

/// <summary>
/// Unit tests for <see cref="Shopping.Cart.Cart.Create"/>.
/// Verifies that initial cart state is correctly mapped from a <see cref="CartInitialized"/> event.
/// </summary>
public class CartCreateTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static IEvent<CartInitialized> BuildEvent(
        Guid? streamId = null,
        Guid? customerId = null,
        string? sessionId = null,
        DateTimeOffset? initializedAt = null)
    {
        var data = new CartInitialized(
            CustomerId: customerId,
            SessionId: sessionId,
            InitializedAt: initializedAt ?? DateTimeOffset.UtcNow);

        var evt = Substitute.For<IEvent<CartInitialized>>();
        evt.StreamId.Returns(streamId ?? Guid.NewGuid());
        evt.Data.Returns(data);
        return evt;
    }

    // ---------------------------------------------------------------------------
    // Cart.Create() — field mapping
    // ---------------------------------------------------------------------------

    /// <summary>Cart Id must match the event's StreamId.</summary>
    [Fact]
    public void Create_Sets_Id_From_StreamId()
    {
        var streamId = Guid.NewGuid();
        var cart = Shopping.Cart.Cart.Create(BuildEvent(streamId: streamId));

        cart.Id.ShouldBe(streamId);
    }

    /// <summary>CustomerId is mapped when present in the event.</summary>
    [Fact]
    public void Create_Sets_CustomerId_When_Present()
    {
        var customerId = Guid.NewGuid();
        var cart = Shopping.Cart.Cart.Create(BuildEvent(customerId: customerId));

        cart.CustomerId.ShouldBe(customerId);
    }

    /// <summary>CustomerId is null when the event carries a guest session instead.</summary>
    [Fact]
    public void Create_CustomerId_Is_Null_For_Guest_Session()
    {
        var cart = Shopping.Cart.Cart.Create(BuildEvent(sessionId: "sess-abc-123"));

        cart.CustomerId.ShouldBeNull();
    }

    /// <summary>SessionId is mapped when present in the event.</summary>
    [Fact]
    public void Create_Sets_SessionId_When_Present()
    {
        var cart = Shopping.Cart.Cart.Create(BuildEvent(sessionId: "session-xyz-789"));

        cart.SessionId.ShouldBe("session-xyz-789");
    }

    /// <summary>SessionId is null when the event carries a logged-in customer instead.</summary>
    [Fact]
    public void Create_SessionId_Is_Null_For_Authenticated_Customer()
    {
        var cart = Shopping.Cart.Cart.Create(BuildEvent(customerId: Guid.NewGuid()));

        cart.SessionId.ShouldBeNull();
    }

    /// <summary>InitializedAt is mapped from the event timestamp.</summary>
    [Fact]
    public void Create_Sets_InitializedAt_From_Event()
    {
        var timestamp = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var cart = Shopping.Cart.Cart.Create(BuildEvent(initializedAt: timestamp));

        cart.InitializedAt.ShouldBe(timestamp);
    }

    /// <summary>A new cart always starts in the Active status.</summary>
    [Fact]
    public void Create_Sets_Status_To_Active()
    {
        var cart = Shopping.Cart.Cart.Create(BuildEvent());

        cart.Status.ShouldBe(CartStatus.Active);
    }

    /// <summary>A new cart always starts with an empty items collection.</summary>
    [Fact]
    public void Create_Initializes_Items_As_Empty()
    {
        var cart = Shopping.Cart.Cart.Create(BuildEvent());

        cart.Items.ShouldBeEmpty();
    }

    /// <summary>A newly created cart is not in a terminal state.</summary>
    [Fact]
    public void Create_IsTerminal_Is_False()
    {
        var cart = Shopping.Cart.Cart.Create(BuildEvent());

        cart.IsTerminal.ShouldBeFalse();
    }
}
