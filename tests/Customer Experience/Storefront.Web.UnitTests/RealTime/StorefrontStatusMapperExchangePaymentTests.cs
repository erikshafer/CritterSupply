using Storefront.Web.RealTime;

namespace Storefront.Web.Tests.RealTime;

/// <summary>
/// Pure-function tests for the M47.0 / Slice 3 additions to
/// <see cref="StorefrontStatusMapper"/>:
/// <see cref="StorefrontStatusMapper.BuildExchangePaymentMessage"/>.
///
/// <para>
/// These tests pin down the customer-facing copy and — more importantly
/// — pin down the <em>defensive</em> behaviour: unknown ISO currency
/// codes, empty / missing payment references, and unknown
/// <c>paymentKind</c> discriminators must all degrade to a useful line
/// instead of crashing the UI. The mapper is shared with any future
/// SignalR consumer (Backoffice timeline, account dashboards) so this
/// surface area must be exercised in isolation.
/// </para>
/// </summary>
public sealed class StorefrontStatusMapperExchangePaymentTests
{
    // ===== Capture (more-expensive replacement) =====

    [Fact]
    public void BuildExchangePaymentMessage_Capture_USD_FormatsAmountAndReference()
    {
        var msg = StorefrontStatusMapper.BuildExchangePaymentMessage(
            "Capture", 25.00m, "USD", "ch_test_capture_abc123");

        msg.ShouldBe("We charged the 25.00 USD difference for your replacement. Reference: ch_test_capture_abc123");
    }

    [Fact]
    public void BuildExchangePaymentMessage_Capture_EUR_RendersIsoCurrencyCode()
    {
        // Different ISO code must render verbatim — we deliberately do NOT
        // pull a CultureInfo / RegionInfo (would throw on unknown codes).
        var msg = StorefrontStatusMapper.BuildExchangePaymentMessage(
            "Capture", 17.50m, "EUR", "ch_eur_xyz");

        msg.ShouldContain("17.50 EUR");
        msg.ShouldContain("ch_eur_xyz");
    }

    [Fact]
    public void BuildExchangePaymentMessage_Capture_EmptyReference_OmitsReferenceClause()
    {
        var msg = StorefrontStatusMapper.BuildExchangePaymentMessage(
            "Capture", 5.00m, "USD", paymentReference: "");

        msg.ShouldBe("We charged the 5.00 USD difference for your replacement.");
        msg.ShouldNotContain("Reference:");
    }

    [Fact]
    public void BuildExchangePaymentMessage_Capture_NullReference_OmitsReferenceClause()
    {
        var msg = StorefrontStatusMapper.BuildExchangePaymentMessage(
            "Capture", 5.00m, "USD", paymentReference: null);

        msg.ShouldBe("We charged the 5.00 USD difference for your replacement.");
    }

    // ===== Refund (cheaper replacement) =====

    [Fact]
    public void BuildExchangePaymentMessage_Refund_USD_FormatsAmountAndTransaction()
    {
        var msg = StorefrontStatusMapper.BuildExchangePaymentMessage(
            "Refund", 15.00m, "USD", "re_test_refund_xyz789");

        msg.ShouldBe("We refunded the 15.00 USD difference to your original payment method. Transaction: re_test_refund_xyz789");
    }

    [Fact]
    public void BuildExchangePaymentMessage_Refund_EmptyReference_OmitsTransactionClause()
    {
        var msg = StorefrontStatusMapper.BuildExchangePaymentMessage(
            "Refund", 12.34m, "USD", paymentReference: "");

        msg.ShouldBe("We refunded the 12.34 USD difference to your original payment method.");
        msg.ShouldNotContain("Transaction:");
    }

    // ===== Defensive behaviour =====

    [Fact]
    public void BuildExchangePaymentMessage_UnknownIsoCurrency_RendersCodeVerbatim()
    {
        // Bug-bounty target — invalid ISO code must NOT throw.
        var msg = StorefrontStatusMapper.BuildExchangePaymentMessage(
            "Capture", 9.99m, "ZZZ", "ref_x");

        msg.ShouldContain("9.99 ZZZ");
    }

    [Fact]
    public void BuildExchangePaymentMessage_NullCurrency_DropsCurrencyClause()
    {
        var msg = StorefrontStatusMapper.BuildExchangePaymentMessage(
            "Capture", 1.00m, currency: null!, paymentReference: "ref");

        // Null/empty currency is tolerated — amount renders alone.
        msg.ShouldContain("1.00");
        msg.ShouldNotContain(" ZZZ");
    }

    [Fact]
    public void BuildExchangePaymentMessage_WhitespaceCurrency_DropsCurrencyClause()
    {
        var msg = StorefrontStatusMapper.BuildExchangePaymentMessage(
            "Capture", 1.00m, currency: "   ", paymentReference: "ref");

        msg.ShouldContain("1.00");
    }

    [Fact]
    public void BuildExchangePaymentMessage_LowercaseCurrency_NormalisedToUpper()
    {
        var msg = StorefrontStatusMapper.BuildExchangePaymentMessage(
            "Capture", 2.00m, "usd", "ref");

        msg.ShouldContain("USD");
    }

    [Fact]
    public void BuildExchangePaymentMessage_UnknownPaymentKind_FallsBackToGenericLine()
    {
        // Producer-side drift (e.g., a future "Adjustment" kind) must NOT
        // crash the UI — generic fallback line that still surfaces the
        // amount and reference.
        var msg = StorefrontStatusMapper.BuildExchangePaymentMessage(
            "Adjustment", 7.50m, "USD", "ref_unknown");

        msg.ShouldContain("7.50 USD");
        msg.ShouldContain("ref_unknown");
    }

    [Fact]
    public void BuildExchangePaymentMessage_UnknownPaymentKind_AndEmptyReference_StillUsefulLine()
    {
        var msg = StorefrontStatusMapper.BuildExchangePaymentMessage(
            "Whatever", 0.01m, "USD", "");

        msg.ShouldContain("0.01 USD");
    }
}
