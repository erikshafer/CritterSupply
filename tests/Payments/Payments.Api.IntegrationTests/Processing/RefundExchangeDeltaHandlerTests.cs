using Marten;
using Payments.Processing;
using PaymentsMessages = Messages.Contracts.Payments;
using ReturnsMessages = Messages.Contracts.Returns;

namespace Payments.Api.IntegrationTests.Processing;

/// <summary>
/// M47.0 / Slice 4 — Integration tests for
/// <see cref="RefundExchangeDeltaHandler"/>, the Payments BC entry point
/// for the inspection-rejection compensation path that refunds a captured
/// additional-payment delta. Verifies the handler:
/// <list type="bullet">
///   <item>refunds the delta against the deterministic delta-Payment stream
///         (<see cref="ExchangePaymentIds.ComputeDeltaPaymentId"/>),</item>
///   <item>appends a single <see cref="PaymentRefunded"/> event tagged with
///         <c>ReturnId</c>,</item>
///   <item>publishes the reusable
///         <see cref="PaymentsMessages.ExchangePartialRefundIssued"/> reply
///         (Storefront / Orders / Backoffice already render it),</item>
///   <item>is idempotent under at-least-once redelivery.</item>
/// </list>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RefundExchangeDeltaHandlerTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public RefundExchangeDeltaHandlerTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Drives a captured delta-Payment stream into existence by replaying
    /// the Slice 2 capture choreography end-to-end. The seeded original
    /// captured Payment is required because <see cref="CaptureExchangeDeltaHandler"/>
    /// sources <c>Currency</c> and <c>PaymentMethodToken</c> from it.
    /// </summary>
    private async Task<(Guid returnId, Guid orderId, Guid customerId, Guid deltaPaymentId)>
        SeedCapturedDeltaPaymentAsync(decimal originalAmount = 100m, decimal deltaAmount = 25m)
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new RequestPayment(orderId, customerId, originalAmount, "USD", "tok_success_visa"));

        var returnId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReturnsMessages.ExchangeAdditionalPaymentRequired(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: customerId,
            AmountDue: deltaAmount,
            RequiredAt: DateTimeOffset.UtcNow));

        var deltaPaymentId = ExchangePaymentIds.ComputeDeltaPaymentId(returnId);
        return (returnId, orderId, customerId, deltaPaymentId);
    }

    [Fact]
    public async Task RefundExchangeDeltaHandler_with_captured_delta_emits_ExchangePartialRefundIssued_against_delta_stream()
    {
        // Arrange
        var (returnId, orderId, customerId, deltaPaymentId) = await SeedCapturedDeltaPaymentAsync(deltaAmount: 25m);

        var message = new PaymentsMessages.RefundExchangeDeltaRequested(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: customerId,
            RefundAmount: 25m,
            RequestedAt: DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert — single PaymentRefunded on the DELTA stream tagged with ReturnId
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(deltaPaymentId);
        var refunds = events.Select(e => e.Data).OfType<PaymentRefunded>().ToList();
        refunds.Count.ShouldBe(1);
        refunds[0].ReturnId.ShouldBe(returnId);
        refunds[0].RefundAmount.ShouldBe(25m);
        refunds[0].PaymentId.ShouldBe(deltaPaymentId);

        // Outbound contract — reuses ExchangePartialRefundIssued; OriginalPaymentId
        // is the delta stream id so downstream observers can distinguish it from
        // the cheaper-replacement path if they care.
        var issued = tracked.Sent.SingleMessage<PaymentsMessages.ExchangePartialRefundIssued>();
        issued.ReturnId.ShouldBe(returnId);
        issued.OrderId.ShouldBe(orderId);
        issued.OriginalPaymentId.ShouldBe(deltaPaymentId);
        issued.RefundAmount.ShouldBe(25m);
        issued.Currency.ShouldBe("USD");
        issued.TransactionId.ShouldBe(refunds[0].RefundTransactionId);
    }

    [Fact]
    public async Task RefundExchangeDeltaHandler_redelivery_does_not_double_refund_and_re_emits_reply()
    {
        // Arrange
        var (returnId, orderId, customerId, deltaPaymentId) = await SeedCapturedDeltaPaymentAsync(deltaAmount: 25m);

        var message = new PaymentsMessages.RefundExchangeDeltaRequested(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: customerId,
            RefundAmount: 25m,
            RequestedAt: DateTimeOffset.UtcNow);

        // Act — deliver twice
        await _fixture.ExecuteAndWaitAsync(message);
        var tracked2 = await _fixture.ExecuteAndWaitAsync(message);

        // Assert — exactly one PaymentRefunded on the delta stream
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(deltaPaymentId);
        var refunds = events.Select(e => e.Data).OfType<PaymentRefunded>().ToList();
        refunds.Count.ShouldBe(1);

        // Reply re-emitted on the redelivery (matches Slice 2 / IssueExchangePartialRefundHandler
        // contract — never silently drop, always echo so a Returns redelivery loss does not
        // strand the exchange).
        var issued = tracked2.Sent.SingleMessage<PaymentsMessages.ExchangePartialRefundIssued>();
        issued.ReturnId.ShouldBe(returnId);
        issued.OriginalPaymentId.ShouldBe(deltaPaymentId);
        issued.TransactionId.ShouldBe(refunds[0].RefundTransactionId);
    }

    [Fact]
    public async Task RefundExchangeDeltaHandler_without_delta_stream_is_silent_no_op()
    {
        // Arrange — never seed a delta capture; simulate a request that lost its
        // precondition (defensive symmetry with IssueExchangePartialRefundHandler).
        var message = new PaymentsMessages.RefundExchangeDeltaRequested(
            ReturnId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            RefundAmount: 25m,
            RequestedAt: DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert — no reply emitted, no events appended.
        tracked.Sent.MessagesOf<PaymentsMessages.ExchangePartialRefundIssued>().ShouldBeEmpty();
    }
}
