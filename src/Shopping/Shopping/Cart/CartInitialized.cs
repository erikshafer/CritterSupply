namespace Shopping.Cart;

public sealed record CartInitialized(
    Guid? CustomerId,
    string? SessionId,
    DateTimeOffset InitializedAt);
