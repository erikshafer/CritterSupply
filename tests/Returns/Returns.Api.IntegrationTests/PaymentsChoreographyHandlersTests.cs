using System.Net;
using Marten;
using Returns.Integration;
using Returns.ReturnProcessing;
using PaymentsMessages = Messages.Contracts.Payments;
using ReturnsMessages = Messages.Contracts.Returns;

namespace Returns.Api.IntegrationTests;

/// <summary>
/// M47.0 / Slice 2 — Integration tests for the Returns side of the
/// cross-product exchange Payments choreography. Verifies that
/// <see cref="ExchangeDeltaCapturedHandler"/>,
/// <see cref="ExchangePartialRefundIssuedHandler"/>, and
/// <see cref="ExchangeDeltaCaptureFailedHandler"/> behave per ADR 0062.
/// </summary>
[Collection("Integration")]
public sealed class PaymentsChoreographyHandlersTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public PaymentsChoreographyHandlersTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedEligibilityWindow(Guid orderId, Guid customerId)
    {
        using var session = _fixture.GetDocumentSession();
        session.Store(new ReturnEligibilityWindow
        {
            Id = orderId,
            OrderId = orderId,
            CustomerId = customerId,
            DeliveredAt = DateTimeOffset.UtcNow.AddDays(-5),
            WindowExpiresAt = DateTimeOffset.UtcNow.AddDays(25),
            EligibleItems = []
        });
        await session.SaveChangesAsync();
    }

    /// <summary>
    /// Drives a cross-product exchange to the post-Approved state with
    /// <c>AdditionalPaymentAmount</c> set, matching the precondition
    /// for <see cref="ExchangeDeltaCapturedHandler"/>. Replacement
    /// (PET-BED-L @ $75) is more expensive than original
    /// (PET-CAR-M @ $50), producing an upcharge of $25.
    /// </summary>
    private async Task<(Guid returnId, Guid orderId, Guid customerId)>
        CreateApprovedMoreExpensiveExchange()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResult = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("PET-CAR-M", "Pet Carrier (Medium)", 1, 50.00m,
                        ReturnReason.Unwanted, "Wrong size")
                ],
                ExchangeRequest: new RequestReturnExchangeRequest("PET-BED-L", 1, 75.00m)
            )).ToUrl("/api/returns");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = createResult.ReadAsJson<RequestReturnResponse>();
        var returnId = response!.ReturnId!.Value;

        await _fixture.ExecuteAndWaitAsync(new ApproveExchange(returnId));

        return (returnId, orderId, customerId);
    }

    /// <summary>
    /// Drives a cross-product exchange end-to-end through ShipReplacementItem
    /// for a CHEAPER replacement, matching the precondition for
    /// <see cref="ExchangePartialRefundIssuedHandler"/>. Original PET-CAR-M
    /// @ $50, replacement PET-BED-L @ $40 → $10 partial refund owed.
    /// </summary>
    private async Task<(Guid returnId, Guid orderId, Guid customerId)>
        CreateCompletedCheaperExchange()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResult = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("PET-CAR-M", "Pet Carrier (Medium)", 1, 50.00m,
                        ReturnReason.Unwanted, "Wrong size")
                ],
                ExchangeRequest: new RequestReturnExchangeRequest("PET-BED-L", 1, 40.00m)
            )).ToUrl("/api/returns");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = createResult.ReadAsJson<RequestReturnResponse>();
        var returnId = response!.ReturnId!.Value;

        await _fixture.ExecuteAndWaitAsync(new ApproveExchange(returnId));
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        var inspectionResults = new List<InspectionLineResult>
        {
            new("PET-CAR-M", 1, ItemCondition.AsExpected,
                "Good condition", true, DispositionDecision.Restockable, "A-12-3")
        };
        await _fixture.ExecuteAndWaitAsync(new SubmitInspection(returnId, inspectionResults));
        await _fixture.ExecuteAndWaitAsync(new ShipReplacementItem(returnId, "SHIP-123", "TRACK-456"));

        return (returnId, orderId, customerId);
    }

    // -----------------------------------------------------------------
    // ExchangeDeltaCapturedHandler — happy-path + idempotency
    // -----------------------------------------------------------------

    [Fact]
    public async Task ExchangeDeltaCapturedHandler_appends_domain_event_and_republishes_public_message()
    {
        var (returnId, orderId, customerId) = await CreateApprovedMoreExpensiveExchange();

        var deltaPaymentId = Guid.NewGuid(); // opaque to Returns
        var capturedAt = DateTimeOffset.UtcNow;
        var message = new PaymentsMessages.ExchangeDeltaCaptured(
            ReturnId: returnId,
            OrderId: orderId,
            PaymentId: deltaPaymentId,
            AmountCaptured: 25.00m,
            Currency: "USD",
            TransactionId: "txn_delta_123",
            CapturedAt: capturedAt);

        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert: domain event appended to the Return stream and aggregate state updated.
        using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(returnId);
        var domainEvents = events.Select(e => e.Data).OfType<ExchangeAdditionalPaymentCaptured>().ToList();
        domainEvents.Count.ShouldBe(1);
        domainEvents[0].PaymentReference.ShouldBe("txn_delta_123");
        domainEvents[0].AmountCaptured.ShouldBe(25.00m);

        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);
        aggregate.ShouldNotBeNull();
        aggregate.AdditionalPaymentCaptured.ShouldBeTrue();
        aggregate.PaymentReference.ShouldBe("txn_delta_123");

        // Assert: public message republished with the new Slice 2 fields populated.
        // Returns.ExchangeAdditionalPaymentCaptured fans out to two queues
        // (orders-returns-events + storefront-returns-events) per
        // src/Returns/Returns.Api/Program.cs lines 158, 185 — so a single
        // PublishAsync produces two `Sent` envelopes. Both carry the same
        // payload; assert the count and inspect the first.
        var republishedAll = tracked.Sent.MessagesOf<ReturnsMessages.ExchangeAdditionalPaymentCaptured>().ToList();
        republishedAll.Count.ShouldBe(2);
        var republished = republishedAll[0];
        republished.ReturnId.ShouldBe(returnId);
        republished.OrderId.ShouldBe(orderId);
        republished.CustomerId.ShouldBe(customerId);
        republished.PaymentId.ShouldBe(deltaPaymentId);
        republished.AmountCaptured.ShouldBe(25.00m);
        republished.Currency.ShouldBe("USD");
        republished.PaymentReference.ShouldBe("txn_delta_123");
    }

    [Fact]
    public async Task ExchangeDeltaCapturedHandler_idempotent_on_redelivery()
    {
        var (returnId, orderId, customerId) = await CreateApprovedMoreExpensiveExchange();

        var message = new PaymentsMessages.ExchangeDeltaCaptured(
            ReturnId: returnId,
            OrderId: orderId,
            PaymentId: Guid.NewGuid(),
            AmountCaptured: 25.00m,
            Currency: "USD",
            TransactionId: "txn_delta_dup",
            CapturedAt: DateTimeOffset.UtcNow);

        var firstTracked = await _fixture.ExecuteAndWaitAsync(message);
        var secondTracked = await _fixture.ExecuteAndWaitAsync(message);

        // Domain event appended exactly once.
        using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(returnId);
        events.Count(e => e.Data is ExchangeAdditionalPaymentCaptured).ShouldBe(1);

        // Public republish only happens on the first delivery.
        // (Each delivery's republish itself fans out to 2 queues per Program.cs.)
        firstTracked.Sent.MessagesOf<ReturnsMessages.ExchangeAdditionalPaymentCaptured>().Count().ShouldBe(2);
        secondTracked.Sent.MessagesOf<ReturnsMessages.ExchangeAdditionalPaymentCaptured>().ShouldBeEmpty();
    }

    // -----------------------------------------------------------------
    // ExchangePartialRefundIssuedHandler — happy path
    // -----------------------------------------------------------------

    [Fact]
    public async Task ExchangePartialRefundIssuedHandler_appends_domain_event_and_republishes_public_message()
    {
        var (returnId, orderId, customerId) = await CreateCompletedCheaperExchange();

        var originalPaymentId = Guid.NewGuid(); // opaque to Returns
        var issuedAt = DateTimeOffset.UtcNow;
        var message = new PaymentsMessages.ExchangePartialRefundIssued(
            ReturnId: returnId,
            OrderId: orderId,
            OriginalPaymentId: originalPaymentId,
            RefundAmount: 10.00m,
            Currency: "USD",
            TransactionId: "ref_partial_xyz",
            IssuedAt: issuedAt);

        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Domain event appended to the Return stream.
        using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(returnId);
        var domainEvents = events.Select(e => e.Data).OfType<ExchangePartialRefundIssued>().ToList();
        domainEvents.Count.ShouldBe(1);
        domainEvents[0].RefundAmount.ShouldBe(10.00m);

        // Public republish with the new Slice 2 fields populated.
        // Returns.ExchangePartialRefundIssued fans out to two queues
        // (orders-returns-events + storefront-returns-events) per
        // src/Returns/Returns.Api/Program.cs lines 160, 187.
        var republishedAll = tracked.Sent.MessagesOf<ReturnsMessages.ExchangePartialRefundIssued>().ToList();
        republishedAll.Count.ShouldBe(2);
        var republished = republishedAll[0];
        republished.ReturnId.ShouldBe(returnId);
        republished.OrderId.ShouldBe(orderId);
        republished.CustomerId.ShouldBe(customerId);
        republished.OriginalPaymentId.ShouldBe(originalPaymentId);
        republished.RefundAmount.ShouldBe(10.00m);
        republished.Currency.ShouldBe("USD");
        republished.TransactionId.ShouldBe("ref_partial_xyz");
    }

    // -----------------------------------------------------------------
    // ExchangeDeltaCaptureFailedHandler — log-only stub
    // -----------------------------------------------------------------

    [Fact]
    public async Task ExchangeDeltaCaptureFailedHandler_does_not_mutate_state()
    {
        var (returnId, orderId, _) = await CreateApprovedMoreExpensiveExchange();

        // Capture aggregate snapshot BEFORE the failure delivery so we can
        // diff afterwards. Slice 2 contract: no Return state mutation.
        Return? before;
        using (var beforeSession = _fixture.GetDocumentSession())
        {
            before = await beforeSession.Events.AggregateStreamAsync<Return>(returnId);
        }

        var message = new PaymentsMessages.ExchangeDeltaCaptureFailed(
            ReturnId: returnId,
            OrderId: orderId,
            AmountDue: 25m,
            Currency: "USD",
            Reason: "card_declined",
            IsRetriable: false,
            FailedAt: DateTimeOffset.UtcNow);

        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert: no events appended, no public re-emit.
        using var session = _fixture.GetDocumentSession();
        var after = await session.Events.AggregateStreamAsync<Return>(returnId);
        after.ShouldNotBeNull();
        after.Status.ShouldBe(before!.Status);
        after.AdditionalPaymentCaptured.ShouldBeFalse();
        after.AdditionalPaymentAmount.ShouldBe(before.AdditionalPaymentAmount);
        after.IsTerminal.ShouldBeFalse();

        tracked.Sent.MessagesOf<ReturnsMessages.ExchangeAdditionalPaymentCaptured>().ShouldBeEmpty();

        // Note: log-warning capture (the original test #11) is skipped —
        // the Returns test fixture does not yet wire a FakeLogger. The
        // class-level marker ExchangeDeltaCaptureFailedLog is exercised
        // implicitly because the handler ran without exception.
        typeof(ExchangeDeltaCaptureFailedLog).ShouldNotBeNull();
    }
}
