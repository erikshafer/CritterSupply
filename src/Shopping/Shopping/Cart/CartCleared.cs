namespace Shopping.Cart;

public sealed record CartCleared(
    DateTimeOffset ClearedAt,
    string? Reason);
