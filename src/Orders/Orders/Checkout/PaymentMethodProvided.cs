namespace Orders.Checkout;

public sealed record PaymentMethodProvided(
    string PaymentMethodToken,
    DateTimeOffset ProvidedAt);
