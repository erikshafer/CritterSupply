namespace Returns.Api.IntegrationTests;

/// <summary>
/// Honesty-pass placeholders (M46.0/A) for cross-product-exchange
/// scenarios that the PO has signed off on but that are
/// <b>not yet implementable end-to-end</b> because the cross-BC
/// choreography (Inventory replacement reservation, Payments delta
/// capture/refund, Orders saga consumption of the relevant integration
/// events) has not been built. The Returns BC owns its own state for
/// each scenario, but a green test asserting only the Returns side
/// would be misleading — see
/// <c>docs/planning/milestones/m45-1-cross-product-exchange-gap-memo.md</c>
/// for the full gap inventory and proposed landing.
///
/// <para>
/// Each test below is permanently skipped (with a Skip reason that
/// names the missing capability) so the test report keeps the gap
/// visible to anyone scanning the suite. When a future milestone
/// implements the corresponding cross-BC slice, lift the Skip and
/// flesh out the body — and remove the matching <c>@pending</c> tag
/// from <c>docs/features/returns/cross-product-exchange.feature</c>.
/// </para>
///
/// <para>
/// We chose Skipped Facts over deleting/omitting the tests entirely
/// because deletion creates an information vacuum: the next person
/// reading the feature file sees specced behaviour but no
/// corresponding test, and assumes coverage exists somewhere. The
/// Skipped fact, with its Skip reason linked to the gap memo, makes
/// the missing coverage discoverable from <c>dotnet test</c> output.
/// </para>
/// </summary>
public sealed class CrossProductExchangePendingTests
{
    private const string GapMemo =
        "docs/planning/milestones/m45-1-cross-product-exchange-gap-memo.md";

    [Fact(Skip =
        "Pending — ExchangePartialRefundIssued integration message is " +
        "defined and routed for publication but never constructed. No " +
        "Payments handler issues the delta refund. See " + GapMemo +
        " 'What is missing' row #5.")]
    public void Cheaper_Replacement_Issues_Partial_Refund()
    {
        // Implementation deferred until: Payments BC subscribes to
        // ExchangeApproved (or equivalent) for cheaper replacements and
        // emits ExchangePartialRefundIssued, with the Returns aggregate
        // applying it to mark the exchange completed.
    }

    [Fact(Skip =
        "Pending — no Payments subscriber for " +
        "ExchangeAdditionalPaymentRequired and no emitter of " +
        "ExchangeAdditionalPaymentCaptured. The 'And the additional " +
        "payment is captured' Gherkin step has no implementing code path. See " +
        GapMemo + " 'What is missing' rows #3–4.")]
    public void More_Expensive_Replacement_Captures_Additional_Payment()
    {
        // Implementation deferred until: Payments BC handles
        // ExchangeAdditionalPaymentRequired by issuing a
        // CapturePayment for the delta and emits
        // ExchangeAdditionalPaymentCaptured back; Returns aggregate
        // applies it and proceeds to ship the replacement.
    }

    [Fact(Skip =
        "Pending — no refund-of-additional-payment compensation path " +
        "when inspection rejects the original item. The " +
        "'And the $25.00 additional payment is refunded to the customer' " +
        "Gherkin step has no implementing handler. See " + GapMemo +
        " 'What is missing' row #7.")]
    public void Inspection_Rejection_Refunds_Additional_Payment_Delta()
    {
        // Implementation deferred until: SubmitInspection's reject path
        // emits a RefundAdditionalPayment command to Payments when
        // IsCrossProductExchange && AdditionalPaymentCaptured.
    }

    [Fact(Skip =
        "Pending — no ExchangeCancelled command/event for the " +
        "additional-payment capture-failure compensation path. The " +
        "'Then the exchange is cancelled' Gherkin step has no " +
        "implementing code. See " + GapMemo +
        " 'What is missing' row #6.")]
    public void Payment_Capture_Failure_Cancels_Exchange()
    {
        // Implementation deferred until: Payments BC emits
        // ExchangeAdditionalPaymentCaptureFailed, Returns applies it to
        // a CancelExchange command, the aggregate transitions to
        // Cancelled and notifies the customer per the Gherkin copy.
    }
}
