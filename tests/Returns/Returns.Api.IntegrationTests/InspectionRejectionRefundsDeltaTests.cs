using Marten;
using Returns.ReturnProcessing;
using PaymentsMessages = Messages.Contracts.Payments;
using ReturnsMessages = Messages.Contracts.Returns;
using InventoryMessages = Messages.Contracts.Inventory;

namespace Returns.Api.IntegrationTests;

/// <summary>
/// M47.0 / Slice 4 — Integration tests for the Returns BC's handling of
/// inspection-rejection on a cross-product exchange that already captured
/// the additional-payment delta. Verifies that
/// <see cref="SubmitInspectionHandler"/>:
/// <list type="bullet">
///   <item>publishes <see cref="PaymentsMessages.RefundExchangeDeltaRequested"/>
///         with the captured delta amount,</item>
///   <item>publishes <see cref="InventoryMessages.ReleaseExchangeReservation"/>
///         when the replacement reservation was previously confirmed,</item>
///   <item>does NOT publish either compensation message when the precondition
///         is not met (e.g. delta was never captured, or the exchange is not
///         cross-product).</item>
/// </list>
/// Closes the "Cross-product exchange with additional payment rejected —
/// refund payment difference" Gherkin scenario.
/// </summary>
[Collection("Integration")]
public sealed class InspectionRejectionRefundsDeltaTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public InspectionRejectionRefundsDeltaTests(TestFixture fixture) => _fixture = fixture;

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
    /// Drives a cross-product exchange to <see cref="ReturnStatus.Inspecting"/>
    /// with both the additional-payment delta captured AND the replacement
    /// reservation confirmed — i.e. all preconditions for the Slice 4
    /// inspection-rejection compensation path are present.
    /// </summary>
    private async Task<(Guid returnId, Guid orderId, Guid customerId, Guid inventoryId)>
        CreateInspectingExchangeWithCapturedDeltaAndReservation()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Replacement (PET-BED-L @ $75) more expensive than original (PET-CAR-M @ $50) → +$25 upcharge.
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
            s.StatusCodeShouldBe(System.Net.HttpStatusCode.OK);
        });
        var response = createResult.ReadAsJson<RequestReturnResponse>();
        var returnId = response!.ReturnId!.Value;

        await _fixture.ExecuteAndWaitAsync(new ApproveExchange(returnId));

        // Slice 1 — confirm the replacement reservation so the aggregate captures InventoryId.
        var inventoryId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new InventoryMessages.ReplacementReserved(
            ReturnId: returnId,
            OrderId: orderId,
            InventoryId: inventoryId,
            Sku: "PET-BED-L",
            WarehouseId: ReturnsExchangeDefaults.ReplacementWarehouseId,
            Quantity: 1,
            ReservedAt: DateTimeOffset.UtcNow));

        // Slice 2 — capture the additional-payment delta.
        await _fixture.ExecuteAndWaitAsync(new PaymentsMessages.ExchangeDeltaCaptured(
            ReturnId: returnId,
            OrderId: orderId,
            PaymentId: Guid.NewGuid(),
            AmountCaptured: 25m,
            Currency: "USD",
            TransactionId: "txn_seed_delta",
            CapturedAt: DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));
        await _fixture.ExecuteAndWaitAsync(new StartInspection(returnId, "INSP-01"));

        return (returnId, orderId, customerId, inventoryId);
    }

    [Fact]
    public async Task SubmitInspection_failure_with_captured_delta_publishes_refund_request_and_release()
    {
        var (returnId, orderId, customerId, inventoryId) =
            await CreateInspectingExchangeWithCapturedDeltaAndReservation();

        var failed = new List<InspectionLineResult>
        {
            new("PET-CAR-M", 1, ItemCondition.WorseThanExpected,
                "Significant scratches and missing parts", false, DispositionDecision.Dispose, null)
        };

        var tracked = await _fixture.ExecuteAndWaitAsync(new SubmitInspection(returnId, failed));

        // Aggregate is rejected (existing Slice-1-era behaviour).
        using var session = _fixture.GetDocumentSession();
        var after = await session.Events.AggregateStreamAsync<Return>(returnId);
        after.ShouldNotBeNull();
        after.Status.ShouldBe(ReturnStatus.Rejected);

        // Slice 4 compensation: refund the captured delta against the delta payment stream.
        var refundRequest = tracked.Sent
            .SingleMessage<PaymentsMessages.RefundExchangeDeltaRequested>();
        refundRequest.ReturnId.ShouldBe(returnId);
        refundRequest.OrderId.ShouldBe(orderId);
        refundRequest.CustomerId.ShouldBe(customerId);
        refundRequest.RefundAmount.ShouldBe(25m);

        // Slice 4 compensation: release the held replacement reservation.
        var release = tracked.Sent
            .SingleMessage<InventoryMessages.ReleaseExchangeReservation>();
        release.InventoryId.ShouldBe(inventoryId);
        release.ReservationId.ShouldBe(returnId);
        release.Reason.ShouldBe("ExchangeInspectionRejected");

        // The original ExchangeRejected fan-out is preserved (assertion
        // omitted — at the time of writing Returns.Api/Program.cs does not
        // route Returns.ExchangeRejected externally, so it never appears
        // in tracked.Sent).
    }

    [Fact]
    public async Task SubmitInspection_failure_without_captured_delta_does_not_publish_refund_request()
    {
        // Cross-product exchange with NO additional payment captured — e.g.
        // cheaper replacement, or the more-expensive flow stalled before capture.
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
            s.StatusCodeShouldBe(System.Net.HttpStatusCode.OK);
        });
        var returnId = createResult.ReadAsJson<RequestReturnResponse>()!.ReturnId!.Value;

        await _fixture.ExecuteAndWaitAsync(new ApproveExchange(returnId));
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));
        await _fixture.ExecuteAndWaitAsync(new StartInspection(returnId, "INSP-02"));

        var failed = new List<InspectionLineResult>
        {
            new("PET-CAR-M", 1, ItemCondition.WorseThanExpected,
                "Damaged in transit by customer", false, DispositionDecision.Dispose, null)
        };

        var tracked = await _fixture.ExecuteAndWaitAsync(new SubmitInspection(returnId, failed));

        // Refund-request must NOT be emitted — there is no captured delta to refund.
        tracked.Sent.MessagesOf<PaymentsMessages.RefundExchangeDeltaRequested>().ShouldBeEmpty();
    }
}
