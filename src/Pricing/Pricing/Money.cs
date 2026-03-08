using System.Text.Json.Serialization;

namespace Pricing;

/// <summary>
/// Money value object - canonical monetary representation for Pricing BC.
/// Enforces type safety (cannot mix currencies), provides operator overloads,
/// and rounds to 2 decimal places for monetary precision.
/// </summary>
[JsonConverter(typeof(MoneyJsonConverter))]
public sealed record Money
{
    public decimal Amount { get; init; }
    public string Currency { get; init; } = null!;

    /// <summary>
    /// Zero money constant (USD by default).
    /// </summary>
    public static readonly Money Zero = Of(0m, "USD");

    // Private constructor prevents direct instantiation - forces factory method usage
    private Money() { }

    /// <summary>
    /// Factory method to create Money value object with validation.
    /// </summary>
    /// <param name="amount">Monetary amount (must be >= 0)</param>
    /// <param name="currency">ISO 4217 3-letter currency code (e.g., "USD", "EUR", "GBP")</param>
    /// <returns>Money instance with validated and rounded amount</returns>
    /// <exception cref="ArgumentException">Thrown if amount is negative or currency is invalid</exception>
    public static Money Of(decimal amount, string currency = "USD")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency, nameof(currency));

        if (currency.Length != 3)
            throw new ArgumentException(
                $"Currency must be ISO 4217 3-letter code, got: '{currency}' (length: {currency.Length})",
                nameof(currency));

        if (amount < 0)
            throw new ArgumentException(
                $"Money amount cannot be negative: {amount}",
                nameof(amount));

        return new Money
        {
            Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            Currency = currency.ToUpperInvariant()
        };
    }

    /// <summary>
    /// Greater-than comparison. Throws if currencies differ.
    /// </summary>
    public static bool operator >(Money a, Money b)
    {
        AssertSameCurrency(a, b);
        return a.Amount > b.Amount;
    }

    /// <summary>
    /// Less-than comparison. Throws if currencies differ.
    /// </summary>
    public static bool operator <(Money a, Money b)
    {
        AssertSameCurrency(a, b);
        return a.Amount < b.Amount;
    }

    /// <summary>
    /// Greater-than-or-equal comparison. Throws if currencies differ.
    /// </summary>
    public static bool operator >=(Money a, Money b)
    {
        AssertSameCurrency(a, b);
        return a.Amount >= b.Amount;
    }

    /// <summary>
    /// Less-than-or-equal comparison. Throws if currencies differ.
    /// </summary>
    public static bool operator <=(Money a, Money b)
    {
        AssertSameCurrency(a, b);
        return a.Amount <= b.Amount;
    }

    /// <summary>
    /// Explicit cast to decimal. NOT implicit to prevent silent currency loss.
    /// Call sites must acknowledge currency context: (decimal)money or money.Amount
    /// </summary>
    public static explicit operator decimal(Money money) => money.Amount;

    /// <summary>
    /// Format as currency string (e.g., "$24.99 USD").
    /// </summary>
    public override string ToString() => $"{Amount:C2} {Currency}";

    /// <summary>
    /// Asserts that two Money instances have the same currency.
    /// Throws InvalidOperationException if currencies differ.
    /// </summary>
    private static void AssertSameCurrency(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException(
                $"Cannot compare Money with different currencies: {a.Currency} vs {b.Currency}");
    }
}
