namespace Shopping.Checkout;

public sealed record ShippingMethodSelected(
    string ShippingMethod,
    decimal ShippingCost,
    DateTimeOffset SelectedAt);
