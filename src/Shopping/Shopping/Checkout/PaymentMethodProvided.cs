namespace Shopping.Checkout;

public sealed record PaymentMethodProvided(
    string PaymentMethodToken,
    DateTimeOffset ProvidedAt);
