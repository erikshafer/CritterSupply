using System.Net;
using Returns.ReturnProcessing;
using Returns.Api.Queries;

namespace Returns.Api.IntegrationTests;

/// <summary>
/// Integration tests for the full return lifecycle endpoints:
/// POST /api/returns/{id}/approve, deny, receive, and inspection.
/// Validates state guards (409 Conflict), event stream persistence,
/// and integration message publishing.
/// </summary>
[Collection("Integration")]
public sealed class ReturnLifecycleEndpointTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ReturnLifecycleEndpointTests(TestFixture fixture)
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

    /// <summary>
    /// Creates a return via the POST endpoint and returns the response.
    /// The return will be auto-approved for non-Other reasons.
    /// </summary>
    private async Task<RequestReturnResponse> CreateReturnViaApi(
        Guid orderId, Guid customerId,
        string sku = "DOG-BOWL-01", string productName = "Ceramic Dog Bowl",
        int quantity = 1, decimal unitPrice = 19.99m,
        ReturnReason reason = ReturnReason.Defective)
    {
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem(sku, productName, quantity, unitPrice, reason)
                ]
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = result.ReadAsJson<RequestReturnResponse>();
        response.ShouldNotBeNull();
        return response;
    }

    /// <summary>
    /// Creates a return in "Requested" state (using "Other" reason which requires CS review).
    /// </summary>
    private async Task<RequestReturnResponse> CreateReturnUnderReview(
        Guid orderId, Guid customerId)
    {
        var response = await CreateReturnViaApi(orderId, customerId,
            reason: ReturnReason.Other);
        response.Status.ShouldBe("UnderReview");
        return response;
    }

    #endregion

    #region POST /api/returns — Additional scenarios

    [Fact]
    public async Task POST_returns_denied_when_eligibility_window_expired()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();

        // Create an expired eligibility window
        using var session = _fixture.GetDocumentSession();
        session.Store(new ReturnEligibilityWindow
        {
            Id = orderId,
            OrderId = orderId,
            CustomerId = customerId,
            DeliveredAt = DateTimeOffset.UtcNow.AddDays(-35),
            WindowExpiresAt = DateTimeOffset.UtcNow.AddDays(-5), // Expired 5 days ago
            EligibleItems = []
        });
        await session.SaveChangesAsync();

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("DOG-BOWL-01", "Ceramic Dog Bowl", 1, 19.99m,
                        ReturnReason.Defective)
                ]
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = result.ReadAsJson<RequestReturnResponse>();
        response.ShouldNotBeNull();
        response.Status.ShouldBe("Denied");
        response.DenialReason.ShouldBe("OutsideReturnWindow");
        response.ReturnId.ShouldBeNull();
    }

    [Fact]
    public async Task POST_returns_under_review_for_Other_reason()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("DOG-BOWL-01", "Ceramic Dog Bowl", 1, 19.99m,
                        ReturnReason.Other, "Changed my mind about the color")
                ]
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = result.ReadAsJson<RequestReturnResponse>();
        response.ShouldNotBeNull();
        response.Status.ShouldBe("UnderReview");
        response.ReturnId.ShouldNotBeNull();
        response.ShipByDate.ShouldBeNull(); // No ship-by date until approved
        response.TotalRestockingFee.ShouldBe(3.00m); // 15% of $19.99 = $3.00
    }

    [Fact]
    public async Task POST_returns_approved_with_multi_item_mixed_reasons()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("DOG-BOWL-01", "Ceramic Dog Bowl", 2, 19.99m,
                        ReturnReason.Defective),
                    new RequestReturnItem("CAT-TOY-05", "Interactive Laser", 1, 29.99m,
                        ReturnReason.Unwanted)
                ]
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = result.ReadAsJson<RequestReturnResponse>();
        response.ShouldNotBeNull();
        response.Status.ShouldBe("Approved"); // No "Other" reason, so auto-approved
        response.TotalRestockingFee.ShouldBe(4.50m); // 15% on $29.99 Unwanted
        response.EstimatedTotalRefund.ShouldBe(39.98m + 29.99m - 4.50m);
        response.Items!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task POST_returns_persists_event_stream()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var response = await CreateReturnViaApi(orderId, customerId);
        response.Status.ShouldBe("Approved");

        // Verify events persisted in Marten
        using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(response.ReturnId!.Value);

        events.ShouldNotBeNull();
        events.Count.ShouldBeGreaterThanOrEqualTo(2); // ReturnRequested + ReturnApproved
        events[0].EventType.ShouldBe(typeof(ReturnRequested));
        events[1].EventType.ShouldBe(typeof(ReturnApproved));
    }

    #endregion

    #region POST /api/returns/{id}/approve

    [Fact]
    public async Task POST_approve_transitions_return_to_Approved()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Create a return under review (Other reason)
        var createResponse = await CreateReturnUnderReview(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Approve it by invoking command directly (not HTTP)
        var command = new ApproveReturn(returnId);
        await _fixture.ExecuteAndWaitAsync(command);

        // Query document session directly to verify aggregate state
        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);

        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(ReturnStatus.Approved);
        aggregate.ApprovedAt.ShouldNotBeNull();
        aggregate.ShipByDeadline.ShouldNotBeNull();
        aggregate.EstimatedRefundAmount.ShouldBeGreaterThan(0);

        // Verify HTTP GET endpoint also works
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/returns/{returnId}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var summary = getResult.ReadAsJson<ReturnSummaryResponse>();
        summary.ShouldNotBeNull();
        summary.Status.ShouldBe("Approved");
    }

    [Fact]
    public async Task POST_approve_returns_409_when_already_approved()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Create an auto-approved return (Defective reason)
        var createResponse = await CreateReturnViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;
        createResponse.Status.ShouldBe("Approved");

        // Try to approve again → should fail with 409
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new ApproveReturn(returnId))
                .ToUrl($"/api/returns/{returnId}/approve");

            s.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    [Fact]
    public async Task POST_approve_returns_404_for_nonexistent_return()
    {
        var nonExistentId = Guid.CreateVersion7();

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new ApproveReturn(nonExistentId))
                .ToUrl($"/api/returns/{nonExistentId}/approve");

            s.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    #endregion

    #region POST /api/returns/{id}/deny

    [Fact]
    public async Task POST_deny_transitions_return_to_Denied()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Create a return under review
        var createResponse = await CreateReturnUnderReview(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Deny it by invoking command directly (not HTTP)
        var command = new DenyReturn(returnId, "PolicyViolation", "Item is non-returnable.");
        await _fixture.ExecuteAndWaitAsync(command);

        // Query document session directly to verify aggregate state
        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);

        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(ReturnStatus.Denied);
        aggregate.DenialReason.ShouldBe("PolicyViolation");
        aggregate.DenialMessage.ShouldBe("Item is non-returnable.");
        aggregate.DeniedAt.ShouldNotBeNull();

        // Verify HTTP GET endpoint also works
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/returns/{returnId}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var summary = getResult.ReadAsJson<ReturnSummaryResponse>();
        summary.ShouldNotBeNull();
        summary.Status.ShouldBe("Denied");
    }

    [Fact]
    public async Task POST_deny_returns_409_when_return_not_in_Requested_state()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Create auto-approved return (Defective)
        var createResponse = await CreateReturnViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;
        createResponse.Status.ShouldBe("Approved");

        // Try to deny an already-approved return → 409
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new DenyReturn(returnId, "TooLate", "Window expired"))
                .ToUrl($"/api/returns/{returnId}/deny");

            s.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    [Fact]
    public async Task POST_deny_returns_404_for_nonexistent_return()
    {
        var nonExistentId = Guid.CreateVersion7();

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new DenyReturn(nonExistentId, "Reason", "Message"))
                .ToUrl($"/api/returns/{nonExistentId}/deny");

            s.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    #endregion

    #region POST /api/returns/{id}/receive

    [Fact]
    public async Task POST_receive_transitions_return_to_Received()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Create and auto-approve
        var createResponse = await CreateReturnViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Receive it (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Query document session directly to verify aggregate state
        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);

        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(ReturnStatus.Received);
        aggregate.ReceivedAt.ShouldNotBeNull();

        // Verify HTTP GET endpoint also works
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/returns/{returnId}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var summary = getResult.ReadAsJson<ReturnSummaryResponse>();
        summary.ShouldNotBeNull();
        summary.Status.ShouldBe("Received");
    }

    [Fact]
    public async Task POST_receive_returns_409_when_not_in_Approved_state()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Create under review (Other reason) — still in Requested state
        var createResponse = await CreateReturnUnderReview(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Try to receive without approval → 409
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new ReceiveReturn(returnId))
                .ToUrl($"/api/returns/{returnId}/receive");

            s.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    [Fact]
    public async Task POST_receive_returns_404_for_nonexistent_return()
    {
        var nonExistentId = Guid.CreateVersion7();

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new ReceiveReturn(nonExistentId))
                .ToUrl($"/api/returns/{nonExistentId}/receive");

            s.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    #endregion

    #region POST /api/returns/{id}/inspection — passing

    [Fact]
    public async Task POST_inspection_pass_transitions_return_to_Completed()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Create → Approve → Receive
        var createResponse = await CreateReturnViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Receive the return (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Submit passing inspection (invoke command directly)
        var inspectionCommand = new SubmitInspection(
            ReturnId: returnId,
            Results:
            [
                new InspectionLineResult("DOG-BOWL-01", 1, ItemCondition.AsExpected,
                    "Item matches description", true, DispositionDecision.Restockable, "A-12-3")
            ]);
        await _fixture.ExecuteAndWaitAsync(inspectionCommand);

        // Query document session directly to verify aggregate state
        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);

        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(ReturnStatus.Completed);
        aggregate.FinalRefundAmount.ShouldNotBeNull();
        aggregate.FinalRefundAmount.Value.ShouldBeGreaterThan(0);
        aggregate.CompletedAt.ShouldNotBeNull();

        // Verify HTTP GET endpoint also works
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/returns/{returnId}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var summary = getResult.ReadAsJson<ReturnSummaryResponse>();
        summary.ShouldNotBeNull();
        summary.Status.ShouldBe("Completed");
    }

    [Fact]
    public async Task POST_inspection_pass_persists_all_events_in_stream()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateReturnViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Receive (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Submit passing inspection (invoke command directly)
        var inspectionCommand = new SubmitInspection(
            ReturnId: returnId,
            Results:
            [
                new InspectionLineResult("DOG-BOWL-01", 1, ItemCondition.AsExpected,
                    "Good", true, DispositionDecision.Restockable, "A-1")
            ]);
        await _fixture.ExecuteAndWaitAsync(inspectionCommand);

        // Verify the full event stream
        using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(returnId);

        events.ShouldNotBeNull();
        events.Count.ShouldBeGreaterThanOrEqualTo(5);
        // ReturnRequested → ReturnApproved → ReturnReceived → InspectionStarted → InspectionPassed
        events[0].EventType.ShouldBe(typeof(ReturnRequested));
        events[1].EventType.ShouldBe(typeof(ReturnApproved));
        events[2].EventType.ShouldBe(typeof(ReturnReceived));
        events[3].EventType.ShouldBe(typeof(InspectionStarted));
        events[4].EventType.ShouldBe(typeof(InspectionPassed));
    }

    #endregion

    #region POST /api/returns/{id}/inspection — failing

    [Fact]
    public async Task POST_inspection_fail_transitions_return_to_Rejected()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateReturnViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Receive (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Submit failing inspection (WorseThanExpected + Dispose) (invoke command directly)
        var inspectionCommand = new SubmitInspection(
            ReturnId: returnId,
            Results:
            [
                new InspectionLineResult("DOG-BOWL-01", 1, ItemCondition.WorseThanExpected,
                    "Customer damage visible, water stains", false, DispositionDecision.Dispose, null)
            ]);
        await _fixture.ExecuteAndWaitAsync(inspectionCommand);

        // Query document session directly to verify aggregate state
        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);

        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(ReturnStatus.Rejected);
        aggregate.FinalRefundAmount.ShouldBeNull(); // No refund on rejection

        // Verify HTTP GET endpoint also works
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/returns/{returnId}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var summary = getResult.ReadAsJson<ReturnSummaryResponse>();
        summary.ShouldNotBeNull();
        summary.Status.ShouldBe("Rejected");
    }

    [Fact]
    public async Task POST_inspection_returns_409_when_not_in_Received_or_Inspecting_state()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Create auto-approved return but don't receive it
        var createResponse = await CreateReturnViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;
        createResponse.Status.ShouldBe("Approved");

        // Try to submit inspection without receiving → 409
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new SubmitInspection(
                ReturnId: returnId,
                Results:
                [
                    new InspectionLineResult("DOG-BOWL-01", 1, ItemCondition.AsExpected,
                        "Good", true, DispositionDecision.Restockable, "A-1")
                ]
            )).ToUrl($"/api/returns/{returnId}/inspection");

            s.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    [Fact]
    public async Task POST_inspection_returns_404_for_nonexistent_return()
    {
        var nonExistentId = Guid.CreateVersion7();

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new SubmitInspection(
                ReturnId: nonExistentId,
                Results:
                [
                    new InspectionLineResult("DOG-BOWL-01", 1, ItemCondition.AsExpected,
                        "Good", true, DispositionDecision.Restockable, "A-1")
                ]
            )).ToUrl($"/api/returns/{nonExistentId}/inspection");

            s.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task POST_inspection_quarantine_disposition_results_in_Rejected()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateReturnViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Receive (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Submit quarantine disposition inspection (invoke command directly)
        var inspectionCommand = new SubmitInspection(
            ReturnId: returnId,
            Results:
            [
                new InspectionLineResult("DOG-BOWL-01", 1, ItemCondition.WorseThanExpected,
                    "Safety concern - unusual chemical odor", false, DispositionDecision.Quarantine, null)
            ]);
        await _fixture.ExecuteAndWaitAsync(inspectionCommand);

        // Query document session directly to verify aggregate state
        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);

        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(ReturnStatus.Rejected);

        // Verify HTTP GET endpoint also works
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/returns/{returnId}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var summary = getResult.ReadAsJson<ReturnSummaryResponse>();
        summary.ShouldNotBeNull();
        summary.Status.ShouldBe("Rejected");
    }

    [Fact]
    public async Task POST_inspection_return_to_customer_disposition_results_in_Rejected()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateReturnViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Receive (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Submit return-to-customer disposition inspection (invoke command directly)
        var inspectionCommand = new SubmitInspection(
            ReturnId: returnId,
            Results:
            [
                new InspectionLineResult("DOG-BOWL-01", 1, ItemCondition.WorseThanExpected,
                    "Wrong SKU received", false, DispositionDecision.ReturnToCustomer, null)
            ]);
        await _fixture.ExecuteAndWaitAsync(inspectionCommand);

        // Query document session directly to verify aggregate state
        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);

        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(ReturnStatus.Rejected);

        // Verify HTTP GET endpoint also works
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/returns/{returnId}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var summary = getResult.ReadAsJson<ReturnSummaryResponse>();
        summary.ShouldNotBeNull();
        summary.Status.ShouldBe("Rejected");
    }

    #endregion

    #region POST /api/returns/{id}/inspection — mixed

    [Fact]
    public async Task POST_inspection_mixed_results_transitions_to_Completed_with_partial_refund()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Create a multi-item return via POST
        var createResult = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("DOG-BOWL-01", "Ceramic Dog Bowl", 2, 19.99m,
                        ReturnReason.Defective),
                    new RequestReturnItem("CAT-TOY-05", "Interactive Laser", 1, 29.99m,
                        ReturnReason.Unwanted),
                    new RequestReturnItem("CAT-BED-01", "Cat Bed", 1, 49.99m,
                        ReturnReason.Defective)
                ]
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var createResponse = createResult.ReadAsJson<RequestReturnResponse>();
        createResponse.ShouldNotBeNull();
        createResponse.Status.ShouldBe("Approved");
        var returnId = createResponse.ReturnId!.Value;

        // Receive (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Submit mixed inspection: 2 pass, 1 fail (invoke command directly)
        var inspectionCommand = new SubmitInspection(
            ReturnId: returnId,
            Results:
            [
                new InspectionLineResult("DOG-BOWL-01", 2, ItemCondition.AsExpected,
                    "Confirmed defective", true, DispositionDecision.Restockable, "A-12"),
                new InspectionLineResult("CAT-TOY-05", 1, ItemCondition.WorseThanExpected,
                    "Customer damage visible", false, DispositionDecision.Dispose, null),
                new InspectionLineResult("CAT-BED-01", 1, ItemCondition.AsExpected,
                    "Confirmed defective", true, DispositionDecision.Restockable, "B-05")
            ]);
        await _fixture.ExecuteAndWaitAsync(inspectionCommand);

        // Query document session directly to verify aggregate state
        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);

        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(ReturnStatus.Completed);
        aggregate.FinalRefundAmount.ShouldNotBeNull();
        // Partial refund covers only passed items (DOG-BOWL defective $39.98 + CAT-BED defective $49.99 = $89.97)
        aggregate.FinalRefundAmount.Value.ShouldBe(89.97m);
        aggregate.CompletedAt.ShouldNotBeNull();

        // Verify HTTP GET endpoint also works
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/returns/{returnId}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var summary = getResult.ReadAsJson<ReturnSummaryResponse>();
        summary.ShouldNotBeNull();
        summary.Status.ShouldBe("Completed");
        summary.FinalRefundAmount!.Value.ShouldBe(89.97m);
    }

    [Fact]
    public async Task POST_inspection_mixed_persists_InspectionMixed_event()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Create a multi-item return
        var createResult = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("DOG-BOWL-01", "Ceramic Dog Bowl", 1, 19.99m,
                        ReturnReason.Defective),
                    new RequestReturnItem("CAT-TOY-05", "Interactive Laser", 1, 29.99m,
                        ReturnReason.Unwanted)
                ]
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var createResponse = createResult.ReadAsJson<RequestReturnResponse>();
        createResponse.ShouldNotBeNull();
        var returnId = createResponse.ReturnId!.Value;

        // Receive (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Submit mixed inspection: 1 pass, 1 fail (invoke command directly)
        var inspectionCommand = new SubmitInspection(
            ReturnId: returnId,
            Results:
            [
                new InspectionLineResult("DOG-BOWL-01", 1, ItemCondition.AsExpected,
                    "Good", true, DispositionDecision.Restockable, "A-1"),
                new InspectionLineResult("CAT-TOY-05", 1, ItemCondition.WorseThanExpected,
                    "Damaged", false, DispositionDecision.Dispose, null)
            ]);
        await _fixture.ExecuteAndWaitAsync(inspectionCommand);

        // Verify the event stream contains InspectionMixed (not InspectionPassed or InspectionFailed)
        using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(returnId);

        events.ShouldNotBeNull();
        events.Count.ShouldBeGreaterThanOrEqualTo(5);
        // ReturnRequested → ReturnApproved → ReturnReceived → InspectionStarted → InspectionMixed
        events[0].EventType.ShouldBe(typeof(ReturnRequested));
        events[1].EventType.ShouldBe(typeof(ReturnApproved));
        events[2].EventType.ShouldBe(typeof(ReturnReceived));
        events[3].EventType.ShouldBe(typeof(InspectionStarted));
        events[4].EventType.ShouldBe(typeof(InspectionMixed));
    }

    #endregion

    #region Full lifecycle through HTTP

    [Fact]
    public async Task Full_lifecycle_Request_Receive_Inspect_Pass_through_HTTP()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Step 1: Request return (auto-approved for Defective)
        var createResponse = await CreateReturnViaApi(orderId, customerId,
            sku: "DOG-BOWL-01", productName: "Ceramic Dog Bowl",
            quantity: 2, unitPrice: 19.99m, reason: ReturnReason.Defective);
        createResponse.Status.ShouldBe("Approved");
        var returnId = createResponse.ReturnId!.Value;

        // Step 2: Receive (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Step 3: Pass inspection (invoke command directly)
        var inspectionCommand = new SubmitInspection(
            ReturnId: returnId,
            Results:
            [
                new InspectionLineResult("DOG-BOWL-01", 2, ItemCondition.AsExpected,
                    "Confirmed defective, cracked base", true, DispositionDecision.Restockable, "A-12")
            ]);
        await _fixture.ExecuteAndWaitAsync(inspectionCommand);

        // Step 4: Query document session directly to verify aggregate state
        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);

        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(ReturnStatus.Completed);
        aggregate.FinalRefundAmount!.Value.ShouldBe(39.98m); // No restocking fee for Defective

        // Step 5: Verify HTTP GET endpoint also works
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/returns/{returnId}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var summary = getResult.ReadAsJson<ReturnSummaryResponse>();
        summary.ShouldNotBeNull();
        summary.ReturnId.ShouldBe(returnId);
        summary.OrderId.ShouldBe(orderId);
        summary.Status.ShouldBe("Completed");
    }

    [Fact]
    public async Task Full_lifecycle_Request_Review_Approve_Receive_Inspect_Fail_through_HTTP()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        // Step 1: Request return with "Other" reason (requires CS review)
        var createResponse = await CreateReturnUnderReview(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Step 2: CS agent approves (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new ApproveReturn(returnId));

        // Step 3: Receive (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Step 4: Fail inspection (invoke command directly)
        var inspectionCommand = new SubmitInspection(
            ReturnId: returnId,
            Results:
            [
                new InspectionLineResult("DOG-BOWL-01", 1, ItemCondition.WorseThanExpected,
                    "Severe customer-caused damage", false, DispositionDecision.Dispose, null)
            ]);
        await _fixture.ExecuteAndWaitAsync(inspectionCommand);

        // Step 5: Query document session directly to verify aggregate state
        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);

        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(ReturnStatus.Rejected);
        aggregate.FinalRefundAmount.ShouldBeNull(); // No refund for rejected returns

        // Step 6: Verify HTTP GET endpoint also works
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/returns/{returnId}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var summary = getResult.ReadAsJson<ReturnSummaryResponse>();
        summary.ShouldNotBeNull();
        summary.Status.ShouldBe("Rejected");
    }

    #endregion

    #region State guard: cannot act on terminal states

    [Fact]
    public async Task POST_receive_returns_409_on_Denied_return()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateReturnUnderReview(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Deny it (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new DenyReturn(returnId, "Policy", "Non-returnable"));

        // Try to receive a denied return → 409
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new ReceiveReturn(returnId))
                .ToUrl($"/api/returns/{returnId}/receive");
            s.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    [Fact]
    public async Task POST_approve_returns_409_on_Denied_return()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateReturnUnderReview(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Deny it (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new DenyReturn(returnId, "Policy", "Non-returnable"));

        // Try to approve a denied return → 409
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new ApproveReturn(returnId))
                .ToUrl($"/api/returns/{returnId}/approve");
            s.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    [Fact]
    public async Task POST_inspection_returns_409_on_Completed_return()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResponse = await CreateReturnViaApi(orderId, customerId);
        var returnId = createResponse.ReturnId!.Value;

        // Receive (invoke command directly)
        await _fixture.ExecuteAndWaitAsync(new ReceiveReturn(returnId));

        // Pass inspection (invoke command directly)
        var inspectionCommand = new SubmitInspection(
            ReturnId: returnId,
            Results:
            [
                new InspectionLineResult("DOG-BOWL-01", 1, ItemCondition.AsExpected,
                    "Good", true, DispositionDecision.Restockable, "A-1")
            ]);
        await _fixture.ExecuteAndWaitAsync(inspectionCommand);

        // Try to inspect again after Completed → 409
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new SubmitInspection(
                ReturnId: returnId,
                Results:
                [
                    new InspectionLineResult("DOG-BOWL-01", 1, ItemCondition.AsExpected,
                        "Good", true, DispositionDecision.Restockable, "A-1")
                ]
            )).ToUrl($"/api/returns/{returnId}/inspection");
            s.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    #endregion
}
