namespace Shopping.Cart;

/// <summary>
/// Represents the lifecycle states of a shopping cart.
/// </summary>
public enum CartStatus
{
    /// <summary>Cart is active and can be modified.</summary>
    Active,

    /// <summary>Cart has been abandoned by the user (terminal state).</summary>
    Abandoned,

    /// <summary>Cart has been cleared by the user (terminal state).</summary>
    Cleared,

    /// <summary>Checkout has been initiated (terminal state, transitions to Checkout).</summary>
    CheckedOut
}
