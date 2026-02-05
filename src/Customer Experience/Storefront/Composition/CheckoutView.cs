namespace Storefront.Composition;

/// <summary>
/// Composed view for checkout wizard (aggregates Orders BC + Customer Identity BC)
/// </summary>
public sealed record CheckoutView(
    Guid CheckoutId,
    Guid CustomerId,
    CheckoutStep CurrentStep,
    IReadOnlyList<CartLineItemView> Items,
    IReadOnlyList<AddressSummary> SavedAddresses,  // From Customer Identity BC
    decimal Subtotal,
    decimal ShippingCost,
    decimal Total,
    bool CanProceedToNextStep);

/// <summary>
/// Checkout wizard step
/// </summary>
public enum CheckoutStep
{
    ShippingAddress = 1,
    ShippingMethod = 2,
    Payment = 3,
    Review = 4
}

/// <summary>
/// Address summary for dropdown selection (from Customer Identity BC)
/// </summary>
public sealed record AddressSummary(
    Guid AddressId,
    string Nickname,
    string DisplayLine);  // "123 Main St, Seattle, WA 98101"
