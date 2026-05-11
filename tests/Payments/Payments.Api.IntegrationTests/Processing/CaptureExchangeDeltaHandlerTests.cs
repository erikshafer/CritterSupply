using Marten;
using Payments.Processing;
using PaymentsMessages = Messages.Contracts.Payments;
using ReturnsMessages = Messages.Contracts.Returns;

namespace Payments.Api.IntegrationTests.Processing;

/// <summary>
/// M47.0 / Slice 2 — Integration tests for
/// <see cref="CaptureExchangeDeltaHandler"/>, the Payments BC entry point
/// for the cross-product exchange delta-capture choreography. Verifies
/// the handler:
/// <list type="bullet">
///   <item>captures the upcharge against the original payment method,</item>
///   <item>persists the new <see cref="Payment"/> stream at the deterministic
///         id from <see cref="ExchangePaymentIds.ComputeDeltaPaymentId"/>,</item>
///   <item>publishes <see cref="PaymentsMessages.ExchangeDeltaCaptured"/> /
///         <see cref="PaymentsMessages.ExchangeDeltaCaptureFailed"/>,</item>
///   <item>is idempotent under at-least-once redelivery.</item>
/// </list>
/// See ADR 0062.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class CaptureExchangeDeltaHandlerTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CaptureExchangeDeltaHandlerTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedCapturedOriginalPaymentAsync(
        Guid orderId,
        Guid customerId,
        decimal amount = 100m,
        string currency = "USD",
        string token = "tok_success_visa")
    {
        await _fixture.ExecuteAndWaitAsync(new RequestPayment(orderId, customerId, amount, currency, token));

        await using var session = _fixture.GetDocumentSession();
        var payment = await session.Query<Payment>()
            .Where(p => p.OrderId == orderId)
            .SingleAsync();
        return payment.Id;
    }

    [Fact]
    public async Task CaptureExchangeDeltaHandler_with_existing_captured_payment_emits_ExchangeDeltaCaptured()
    {
        // Arrange — seed an original captured Payment for the order.
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await SeedCapturedOriginalPaymentAsync(orderId, customerId, amount: 100m, currency: "USD", token: "tok_success_visa");

        var returnId = Guid.NewGuid();
        var amountDue = 25m;
        var message = new ReturnsMessages.ExchangeAdditionalPaymentRequired(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: customerId,
            AmountDue: amountDue,
            RequiredAt: DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert — new Payment stream at the deterministic delta id.
        var deltaId = ExchangePaymentIds.ComputeDeltaPaymentId(returnId);
        await using var session = _fixture.GetDocumentSession();
        var deltaPayment = await session.Events.AggregateStreamAsync<Payment>(deltaId);
        deltaPayment.ShouldNotBeNull();
        deltaPayment.Id.ShouldBe(deltaId);
        deltaPayment.OrderId.ShouldBe(orderId);
        deltaPayment.CustomerId.ShouldBe(customerId);
        deltaPayment.Amount.ShouldBe(amountDue);
        deltaPayment.Currency.ShouldBe("USD");
        deltaPayment.PaymentMethodToken.ShouldBe("tok_success_visa");
        deltaPayment.Status.ShouldBe(PaymentStatus.Captured);
        deltaPayment.TransactionId.ShouldNotBeNullOrEmpty();

        // Stream contains only PaymentInitiated + PaymentCaptured (no extra events).
        var events = await session.Events.FetchStreamAsync(deltaId);
        events.Count(e => e.Data is PaymentInitiated).ShouldBe(1);
        events.Count(e => e.Data is PaymentCaptured).ShouldBe(1);
        events.Count(e => e.Data is PaymentFailed).ShouldBe(0);

        // Outbound contract — single ExchangeDeltaCaptured with the right shape.
        var captured = tracked.Sent.SingleMessage<PaymentsMessages.ExchangeDeltaCaptured>();
        captured.ReturnId.ShouldBe(returnId);
        captured.OrderId.ShouldBe(orderId);
        captured.PaymentId.ShouldBe(deltaId);
        captured.AmountCaptured.ShouldBe(amountDue);
        captured.Currency.ShouldBe("USD");
        captured.TransactionId.ShouldBe(deltaPayment.TransactionId);

        tracked.Sent.MessagesOf<PaymentsMessages.ExchangeDeltaCaptureFailed>().ShouldBeEmpty();
    }

    [Fact]
    public async Task CaptureExchangeDeltaHandler_idempotent_on_redelivery()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await SeedCapturedOriginalPaymentAsync(orderId, customerId);

        // Reset gateway counter AFTER seeding (the seed call ran a capture).
        _fixture.PaymentGateway.Reset();

        var returnId = Guid.NewGuid();
        var message = new ReturnsMessages.ExchangeAdditionalPaymentRequired(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: customerId,
            AmountDue: 30m,
            RequiredAt: DateTimeOffset.UtcNow);

        // Act — deliver the same message twice.
        var firstTracked = await _fixture.ExecuteAndWaitAsync(message);
        var secondTracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert — gateway invoked exactly once.
        _fixture.PaymentGateway.CaptureCalls.ShouldBe(1);

        // Deterministic Payment stream has only one Initiated + one Captured.
        var deltaId = ExchangePaymentIds.ComputeDeltaPaymentId(returnId);
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(deltaId);
        events.Count(e => e.Data is PaymentInitiated).ShouldBe(1);
        events.Count(e => e.Data is PaymentCaptured).ShouldBe(1);

        // Both deliveries re-emit ExchangeDeltaCaptured (Slice 1 inventory pattern:
        // re-emit on duplicate, never silently drop).
        firstTracked.Sent.MessagesOf<PaymentsMessages.ExchangeDeltaCaptured>().Count().ShouldBe(1);
        secondTracked.Sent.MessagesOf<PaymentsMessages.ExchangeDeltaCaptured>().Count().ShouldBe(1);

        // The re-emit carries the same TransactionId — i.e. it's the original
        // capture's reference, not a new one.
        var first = firstTracked.Sent.SingleMessage<PaymentsMessages.ExchangeDeltaCaptured>();
        var second = secondTracked.Sent.SingleMessage<PaymentsMessages.ExchangeDeltaCaptured>();
        second.TransactionId.ShouldBe(first.TransactionId);
        second.PaymentId.ShouldBe(deltaId);
    }

    [Fact]
    public async Task CaptureExchangeDeltaHandler_with_no_original_payment_emits_failed_with_NonRetriable()
    {
        // Arrange — no original Payment for this OrderId.
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        var message = new ReturnsMessages.ExchangeAdditionalPaymentRequired(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: customerId,
            AmountDue: 25m,
            RequiredAt: DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert — non-retriable failure published.
        var failed = tracked.Sent.SingleMessage<PaymentsMessages.ExchangeDeltaCaptureFailed>();
        failed.ReturnId.ShouldBe(returnId);
        failed.OrderId.ShouldBe(orderId);
        failed.AmountDue.ShouldBe(25m);
        failed.IsRetriable.ShouldBeFalse();
        failed.Reason.ShouldNotBeNullOrEmpty();

        tracked.Sent.MessagesOf<PaymentsMessages.ExchangeDeltaCaptured>().ShouldBeEmpty();

        // Deterministic stream exists with PaymentFailed (so redeliveries
        // hit the idempotency branch).
        var deltaId = ExchangePaymentIds.ComputeDeltaPaymentId(returnId);
        await using var session = _fixture.GetDocumentSession();
        var deltaPayment = await session.Events.AggregateStreamAsync<Payment>(deltaId);
        deltaPayment.ShouldNotBeNull();
        deltaPayment.Status.ShouldBe(PaymentStatus.Failed);
        deltaPayment.IsRetriable.ShouldBeFalse();
    }

    [Fact]
    public async Task CaptureExchangeDeltaHandler_with_declining_gateway_emits_failed()
    {
        // Arrange — original payment used a tok_decline_* token.
        // The original RequestPayment will record a Failed payment, so to
        // give the delta handler a Captured payment to source the token from,
        // we instead seed a SUCCESSFUL original on tok_decline_insufficient
        // by going through the token-based gateway: tok_decline_* fails,
        // so the only way to get a Captured Payment whose token will then
        // be re-used by the delta capture (and decline) is to seed events
        // directly. Simplest path: append a synthetic captured Payment.
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var originalPaymentId = Guid.NewGuid();

        await using (var seedSession = _fixture.GetDocumentSession())
        {
            var now = DateTimeOffset.UtcNow.AddMinutes(-5);
            seedSession.Events.StartStream<Payment>(
                originalPaymentId,
                new PaymentInitiated(originalPaymentId, orderId, customerId, 100m, "USD", "tok_decline_insufficient_funds", now),
                new PaymentCaptured(originalPaymentId, "txn_seed", now));
            await seedSession.SaveChangesAsync();
        }

        var returnId = Guid.NewGuid();
        var message = new ReturnsMessages.ExchangeAdditionalPaymentRequired(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: customerId,
            AmountDue: 40m,
            RequiredAt: DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert — failure with the gateway's decline reason.
        var failed = tracked.Sent.SingleMessage<PaymentsMessages.ExchangeDeltaCaptureFailed>();
        failed.ReturnId.ShouldBe(returnId);
        failed.OrderId.ShouldBe(orderId);
        failed.AmountDue.ShouldBe(40m);
        failed.Currency.ShouldBe("USD");
        failed.Reason.ShouldBe("card_declined");
        failed.IsRetriable.ShouldBeFalse();

        tracked.Sent.MessagesOf<PaymentsMessages.ExchangeDeltaCaptured>().ShouldBeEmpty();

        // Deterministic stream is in Failed state.
        var deltaId = ExchangePaymentIds.ComputeDeltaPaymentId(returnId);
        await using var session = _fixture.GetDocumentSession();
        var deltaPayment = await session.Events.AggregateStreamAsync<Payment>(deltaId);
        deltaPayment.ShouldNotBeNull();
        deltaPayment.Status.ShouldBe(PaymentStatus.Failed);
        deltaPayment.FailureReason.ShouldBe("card_declined");
    }
}
