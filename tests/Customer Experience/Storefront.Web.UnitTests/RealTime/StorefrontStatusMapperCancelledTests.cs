using Storefront.Web.RealTime;

namespace Storefront.Web.Tests.RealTime;

/// <summary>
/// M47.0 / Slice 4 — pure-function tests for the new <c>"Cancelled"</c>
/// case added to <see cref="StorefrontStatusMapper.BuildReturnMessage"/>.
/// Closes the customer-facing half of the
/// "Additional payment capture fails — exchange cancelled" Gherkin
/// scenario.
/// </summary>
public sealed class StorefrontStatusMapperCancelledTests
{
    [Fact]
    public void BuildReturnMessage_Cancelled_WithDetails_ReturnsDetailsVerbatim()
    {
        // The producer (Returns ExchangeCancelledHandler) carries the verbatim
        // PO-approved Gherkin copy on the integration message; the mapper's
        // job is to surface it without re-wording.
        const string verbatimGherkinCopy =
            "Payment for price difference could not be processed. Exchange cancelled.";

        var msg = StorefrontStatusMapper.BuildReturnMessage("Cancelled", verbatimGherkinCopy);

        msg.ShouldBe(verbatimGherkinCopy);
    }

    [Fact]
    public void BuildReturnMessage_Cancelled_WithNullDetails_FallsBackToSafeGenericLine()
    {
        // Defensive: producer drift / dropped field must not break the UI.
        var msg = StorefrontStatusMapper.BuildReturnMessage("Cancelled", details: null);

        msg.ShouldBe("Your exchange has been cancelled. Please contact support for more information.");
    }

    [Fact]
    public void BuildReturnMessage_Cancelled_WithEmptyDetails_FallsBackToSafeGenericLine()
    {
        var msg = StorefrontStatusMapper.BuildReturnMessage("Cancelled", details: "");

        msg.ShouldBe("Your exchange has been cancelled. Please contact support for more information.");
    }
}
