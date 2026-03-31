using System.Net;
using Returns.ReturnProcessing;

namespace Returns.Api.IntegrationTests;

/// <summary>
/// Integration tests for Exchange workflow endpoints.
/// Tests the full exchange lifecycle: request → approve → receive → inspect → ship replacement → complete.
/// Validates exchange-specific business rules: same-SKU constraint, price difference handling, inspection rejection.
/// </summary>
[Collection("Integration")]
public sealed class ExchangeWorkflowEndpointTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ExchangeWorkflowEndpointTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    #region Helpers

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

    private async Task<RequestReturnResponse> CreateExchangeViaApi(
        Guid orderId, Guid customerId,
        string originalSku = "PET-CARRIER-M",
        string originalName = "Pet Carrier (Medium)",
        decimal originalPrice = 50.00m,
        string replacementSku = "PET-CARRIER-L",
        decimal replacementPrice = 50.00m)
    {
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem(originalSku, originalName, 1, originalPrice, ReturnReason.Unwanted, "Wrong size")
                ],
                ExchangeRequest: new RequestReturnExchangeRequest(replacementSku, 1, replacementPrice)
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = result.ReadAsJson<RequestReturnResponse>();
        response.ShouldNotBeNull();
        return response;
    }

    #endregion

    [Fact]
    public async Task POST_exchange_request_creates_exchange_return_under_review()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var response = await CreateExchangeViaApi(orderId, customerId);

        response.Status.ShouldBe("UnderReview"); // Exchanges always require manual approval
        response.ReturnId.ShouldNotBeNull();

        // Verify aggregate state
        using var session = _fixture.GetDocumentSession();
        var returnAggregate = await session.Events.AggregateStreamAsync<Return>(response.ReturnId!.Value);
        returnAggregate.ShouldNotBeNull();
        returnAggregate.Type.ShouldBe(ReturnType.Exchange);
        returnAggregate.Status.ShouldBe(ReturnStatus.Requested);
        returnAggregate.ExchangeRequest.ShouldNotBeNull();
        returnAggregate.ExchangeRequest.ReplacementSku.ShouldBe("PET-CARRIER-L");
    }

    [Fact]
    public async Task POST_approve_exchange_same_price_sets_zero_price_difference()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateExchangeViaApi(orderId, customerId,
            originalPrice: 50.00m, replacementPrice: 50.00m);

        var returnId = createResponse.ReturnId!.Value;

        // Approve exchange (direct command invocation to avoid race conditions)
        var command = new ApproveExchange(returnId);
        await _fixture.ExecuteAndWaitAsync(command);

        // Verify state
        using var session = _fixture.GetDocumentSession();
        var returnAggregate = await session.Events.AggregateStreamAsync<Return>(returnId);
        returnAggregate.ShouldNotBeNull();
        returnAggregate.Status.ShouldBe(ReturnStatus.Approved);
        returnAggregate.PriceDifference.ShouldBe(0m);
    }

    [Fact]
    public async Task POST_approve_exchange_cheaper_replacement_calculates_refund()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateExchangeViaApi(orderId, customerId,
            originalPrice: 50.00m, replacementPrice: 40.00m); // $10 price difference

        var returnId = createResponse.ReturnId!.Value;

        // Approve exchange (direct command invocation)
        var command = new ApproveExchange(returnId);
        await _fixture.ExecuteAndWaitAsync(command);

        // Verify state
        using var session = _fixture.GetDocumentSession();
        var returnAggregate = await session.Events.AggregateStreamAsync<Return>(returnId);
        returnAggregate.ShouldNotBeNull();
        returnAggregate.Status.ShouldBe(ReturnStatus.Approved);
        returnAggregate.PriceDifference.ShouldBe(10.00m); // Customer gets $10 refund
    }

    [Fact]
    public async Task POST_approve_exchange_more_expensive_replacement_approves_with_additional_payment()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateExchangeViaApi(orderId, customerId,
            originalPrice: 50.00m, replacementPrice: 65.00m); // $15 more expensive

        var returnId = createResponse.ReturnId!.Value;

        // Approve exchange (direct command invocation) — now succeeds for cross-product
        var command = new ApproveExchange(returnId);
        await _fixture.ExecuteAndWaitAsync(command);

        // Verify state: approved with negative price difference and additional payment required
        using var session = _fixture.GetDocumentSession();
        var returnAggregate = await session.Events.AggregateStreamAsync<Return>(returnId);
        returnAggregate.ShouldNotBeNull();
        returnAggregate.Status.ShouldBe(ReturnStatus.Approved);
        returnAggregate.PriceDifference.ShouldBe(-15.00m); // Negative = customer owes more
        returnAggregate.IsCrossProductExchange.ShouldBeTrue();
        returnAggregate.AdditionalPaymentAmount.ShouldBe(15.00m);
    }

    [Fact]
    public async Task POST_deny_exchange_out_of_stock_transitions_to_denied()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateExchangeViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Deny exchange (direct command invocation)
        var command = new DenyExchange(
            returnId,
            "OutOfStock",
            "Replacement item currently unavailable. Please request a refund or try again later.");
        await _fixture.ExecuteAndWaitAsync(command);

        // Verify state
        using var session = _fixture.GetDocumentSession();
        var returnAggregate = await session.Events.AggregateStreamAsync<Return>(returnId);
        returnAggregate.ShouldNotBeNull();
        returnAggregate.Status.ShouldBe(ReturnStatus.Denied);
        returnAggregate.DenialReason.ShouldBe("OutOfStock");
        returnAggregate.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public async Task POST_inspection_failed_for_exchange_rejects_without_replacement()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateExchangeViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Approve exchange (direct command invocation)
        await _fixture.ExecuteAndWaitAsync(new ApproveExchange(returnId));

        // Receive return (direct command invocation)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Submit failed inspection (direct command invocation)
        var inspectionResults = new List<InspectionLineResult>
        {
            new("PET-CARRIER-M", 1, ItemCondition.WorseThanExpected,
                "Customer damage", false, DispositionDecision.Dispose, null)
        };
        await _fixture.ExecuteAndWaitAsync(new SubmitInspection(returnId, inspectionResults));

        // Verify exchange rejected (no replacement, no refund)
        using var session = _fixture.GetDocumentSession();
        var returnAggregate = await session.Events.AggregateStreamAsync<Return>(returnId);
        returnAggregate.ShouldNotBeNull();
        returnAggregate.Status.ShouldBe(ReturnStatus.Rejected);
        returnAggregate.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public async Task POST_inspection_passed_for_exchange_allows_replacement_shipment()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateExchangeViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Approve exchange (direct command invocation)
        await _fixture.ExecuteAndWaitAsync(new ApproveExchange(returnId));

        // Receive return (direct command invocation)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Submit passed inspection (direct command invocation)
        var inspectionResults = new List<InspectionLineResult>
        {
            new("PET-CARRIER-M", 1, ItemCondition.AsExpected,
                "Good condition", true, DispositionDecision.Restockable, "A-12-3")
        };
        await _fixture.ExecuteAndWaitAsync(new SubmitInspection(returnId, inspectionResults));

        // Verify exchange ready for replacement shipment
        using var session = _fixture.GetDocumentSession();
        var returnAggregate = await session.Events.AggregateStreamAsync<Return>(returnId);
        returnAggregate.ShouldNotBeNull();
        returnAggregate.Status.ShouldBe(ReturnStatus.Inspecting); // Ready for ShipReplacementItem
        returnAggregate.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public async Task POST_ship_replacement_item_completes_exchange()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateExchangeViaApi(orderId, customerId,
            originalPrice: 50.00m, replacementPrice: 40.00m); // $10 price difference

        var returnId = createResponse.ReturnId!.Value;

        // Approve exchange (direct command invocation)
        await _fixture.ExecuteAndWaitAsync(new ApproveExchange(returnId));

        // Receive return (direct command invocation)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Submit passed inspection (direct command invocation)
        var inspectionResults = new List<InspectionLineResult>
        {
            new("PET-CARRIER-M", 1, ItemCondition.AsExpected,
                "Good condition", true, DispositionDecision.Restockable, "A-12-3")
        };
        await _fixture.ExecuteAndWaitAsync(new SubmitInspection(returnId, inspectionResults));

        // Ship replacement item (direct command invocation)
        await _fixture.ExecuteAndWaitAsync(new ShipReplacementItem(returnId, "SHIP-123", "TRACK-456"));

        // Verify exchange completed
        using var session = _fixture.GetDocumentSession();
        var returnAggregate = await session.Events.AggregateStreamAsync<Return>(returnId);
        returnAggregate.ShouldNotBeNull();
        returnAggregate.Status.ShouldBe(ReturnStatus.Completed);
        returnAggregate.ReplacementShipmentId.ShouldBe("SHIP-123");
        returnAggregate.FinalRefundAmount.ShouldBe(10.00m); // Price difference refund
        returnAggregate.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public async Task Cannot_approve_refund_as_exchange()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Create regular refund return
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("DOG-BOWL-01", "Ceramic Dog Bowl", 1, 19.99m, ReturnReason.Other)
                ]
                // No ExchangeRequest
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = result.ReadAsJson<RequestReturnResponse>();
        var returnId = response!.ReturnId!.Value;

        // Try to approve as exchange via HTTP — should fail with 409
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new ApproveExchange(returnId)).ToUrl($"/api/returns/{returnId}/approve-exchange");
            s.StatusCodeShouldBe(HttpStatusCode.Conflict); // 409 - not an exchange
        });
    }

    [Fact]
    public async Task Cannot_deny_refund_as_exchange()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Create regular refund return
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("DOG-BOWL-01", "Ceramic Dog Bowl", 1, 19.99m, ReturnReason.Other)
                ]
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = result.ReadAsJson<RequestReturnResponse>();
        var returnId = response!.ReturnId!.Value;

        // Try to deny as exchange — should fail
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new DenyExchange(returnId, "OutOfStock", "Test"))
                .ToUrl($"/api/returns/{returnId}/deny-exchange");

            s.StatusCodeShouldBe(HttpStatusCode.Conflict); // 409 - not an exchange
        });
    }
}
