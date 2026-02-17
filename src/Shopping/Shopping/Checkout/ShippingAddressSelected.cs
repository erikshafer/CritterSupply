namespace Shopping.Checkout;

public sealed record ShippingAddressSelected(
    Guid CheckoutId,
    Guid AddressId,
    DateTimeOffset SelectedAt);
