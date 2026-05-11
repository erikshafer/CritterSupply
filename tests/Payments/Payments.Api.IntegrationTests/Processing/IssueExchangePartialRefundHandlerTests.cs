using Marten;
using Payments.Processing;
using PaymentsMessages = Messages.Contracts.Payments;

namespace Payments.Api.IntegrationTests.Processing;

/// <summary>
/// M47.0 / Slice 2 — Integration tests for
/// <see cref="IssueExchangePartialRefundHandler"/>, the Payments BC
/// entry point for the cheaper-replacement partial-refund choreography.
/// Verifies the handler appends a <see cref="PaymentRefunded"/> tagged
/// with <c>ReturnId</c> to the original Payment stream, replies with
/// <see cref="PaymentsMessages.ExchangePartialRefundIssued"/>, and is
/// idempotent under at-least-once redelivery. See ADR 0062.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class IssueExchangePartialRefundHandlerTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public IssueExchangePartialRefundHandlerTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedCapturedOriginalPaymentAsync(Guid orderId, Guid customerId, decimal amount = 100m)
    {
        await _fixture.ExecuteAndWaitAsync(new RequestPayment(orderId, customerId, amount, "USD", "tok_success_visa"));

        await using var session = _fixture.GetDocumentSession();
        var payment = await session.Query<Payment>()
            .Where(p => p.OrderId == orderId)
            .SingleAsync();
        return payment.Id;
    }

    [Fact]
    public async Task IssueExchangePartialRefundHandler_with_captured_original_emits_ExchangePartialRefundIssued()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var originalPaymentId = await SeedCapturedOriginalPaymentAsync(orderId, customerId, amount: 100m);

        var returnId = Guid.NewGuid();
        var refundAmount = 25m;
        var message = new PaymentsMessages.ExchangePartialRefundRequested(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: customerId,
            RefundAmount: refundAmount,
            RequestedAt: DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert — original Payment stream now has a single PaymentRefunded
        // tagged with the ReturnId.
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(originalPaymentId);
        var refunds = events.Select(e => e.Data).OfType<PaymentRefunded>().ToList();
        refunds.Count.ShouldBe(1);
        refunds[0].ReturnId.ShouldBe(returnId);
        refunds[0].RefundAmount.ShouldBe(refundAmount);
        refunds[0].TotalRefunded.ShouldBe(refundAmount);
        refunds[0].RefundTransactionId.ShouldNotBeNullOrEmpty();

        var aggregate = await session.Events.AggregateStreamAsync<Payment>(originalPaymentId);
        aggregate.ShouldNotBeNull();
        aggregate.TotalRefunded.ShouldBe(refundAmount);

        // Outbound contract.
        var issued = tracked.Sent.SingleMessage<PaymentsMessages.ExchangePartialRefundIssued>();
        issued.ReturnId.ShouldBe(returnId);
        issued.OrderId.ShouldBe(orderId);
        issued.OriginalPaymentId.ShouldBe(originalPaymentId);
        issued.RefundAmount.ShouldBe(refundAmount);
        issued.Currency.ShouldBe("USD");
        issued.TransactionId.ShouldBe(refunds[0].RefundTransactionId);
    }

    [Fact]
    public async Task IssueExchangePartialRefundHandler_idempotent_on_redelivery()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var originalPaymentId = await SeedCapturedOriginalPaymentAsync(orderId, customerId, amount: 100m);

        // Reset gateway counter — seeding ran a Capture, not a Refund, but
        // be explicit so the assertion below is unambiguous.
        _fixture.PaymentGateway.Reset();

        var returnId = Guid.NewGuid();
        var message = new PaymentsMessages.ExchangePartialRefundRequested(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: customerId,
            RefundAmount: 30m,
            RequestedAt: DateTimeOffset.UtcNow);

        // Act — deliver the same refund request twice.
        var firstTracked = await _fixture.ExecuteAndWaitAsync(message);
        var secondTracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert — gateway invoked exactly once, single PaymentRefunded
        // appended to the original stream.
        _fixture.PaymentGateway.RefundCalls.ShouldBe(1);

        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(originalPaymentId);
        var refunds = events.Select(e => e.Data).OfType<PaymentRefunded>().ToList();
        refunds.Count.ShouldBe(1);

        var aggregate = await session.Events.AggregateStreamAsync<Payment>(originalPaymentId);
        aggregate.ShouldNotBeNull();
        aggregate.TotalRefunded.ShouldBe(30m); // Only one application of the refund.

        // Both deliveries re-emit ExchangePartialRefundIssued (Slice 1
        // inventory pattern: re-emit on duplicate, never silently drop).
        firstTracked.Sent.MessagesOf<PaymentsMessages.ExchangePartialRefundIssued>().Count().ShouldBe(1);
        secondTracked.Sent.MessagesOf<PaymentsMessages.ExchangePartialRefundIssued>().Count().ShouldBe(1);

        var first = firstTracked.Sent.SingleMessage<PaymentsMessages.ExchangePartialRefundIssued>();
        var second = secondTracked.Sent.SingleMessage<PaymentsMessages.ExchangePartialRefundIssued>();
        second.TransactionId.ShouldBe(first.TransactionId);
        second.OriginalPaymentId.ShouldBe(originalPaymentId);
    }

    [Fact]
    public async Task IssueExchangePartialRefundHandler_with_no_original_payment_returns_silently()
    {
        // Arrange — no original Payment for this OrderId. Slice 2 contract:
        // no reply, no exception, no events appended (Slice 4 turns this
        // into a customer-visible failure).
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        _fixture.PaymentGateway.Reset();

        var message = new PaymentsMessages.ExchangePartialRefundRequested(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: customerId,
            RefundAmount: 15m,
            RequestedAt: DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert — no reply, no gateway call, no events.
        tracked.Sent.MessagesOf<PaymentsMessages.ExchangePartialRefundIssued>().ShouldBeEmpty();
        _fixture.PaymentGateway.RefundCalls.ShouldBe(0);

        await using var session = _fixture.GetDocumentSession();
        var anyPayments = await session.Query<Payment>().AnyAsync();
        anyPayments.ShouldBeFalse();
    }
}
