using Marten;
using Messages.Contracts.ProductCatalog;
using Shouldly;
using VendorPortal.ChangeRequests;
using VendorPortal.ChangeRequests.Commands;
using VendorPortal.RealTime;
using VendorPortal.VendorProductCatalog;
using Wolverine.Tracking;

namespace VendorPortal.Api.IntegrationTests;

/// <summary>
/// Integration tests for Phase 4 — Change Request full lifecycle:
/// - Draft, Submit, Withdraw, ProvideAdditionalInfo commands
/// - Auto-withdraw invariant (one active per SKU+Type+Tenant)
/// - Catalog BC response handlers (approve, reject, moreInfo)
/// - SignalR hub message delivery on status change
/// - Tenant isolation (cross-tenant access returns null silently)
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ChangeRequestTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ChangeRequestTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ───────────────────────────────────────────────
    // DraftChangeRequest handler
    // ───────────────────────────────────────────────

    [Fact]
    public async Task DraftChangeRequest_CreatesChangeRequest_InDraftStatus()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var command = new DraftChangeRequest(
            RequestId: requestId,
            VendorTenantId: tenantId,
            SubmittedByUserId: userId,
            Sku: "DOG-FOOD-001",
            Type: ChangeRequestType.Description,
            Title: "Update product description",
            Details: "New description that is more accurate and informative.");

        // Act
        await _fixture.ExecuteMessageAsync(command);

        // Assert
        using var session = _fixture.GetDocumentSession();
        var request = await session.LoadAsync<ChangeRequest>(requestId);

        request.ShouldNotBeNull();
        request.Id.ShouldBe(requestId);
        request.VendorTenantId.ShouldBe(tenantId);
        request.SubmittedByUserId.ShouldBe(userId);
        request.Sku.ShouldBe("DOG-FOOD-001");
        request.Type.ShouldBe(ChangeRequestType.Description);
        request.Status.ShouldBe(ChangeRequestStatus.Draft);
        request.Title.ShouldBe("Update product description");
        request.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task DraftChangeRequest_IsIdempotent_WhenSameIdSentTwice()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var command = new DraftChangeRequest(
            RequestId: requestId,
            VendorTenantId: tenantId,
            SubmittedByUserId: Guid.NewGuid(),
            Sku: "CAT-TOY-001",
            Type: ChangeRequestType.Description,
            Title: "First title",
            Details: "First details.");

        var commandDuplicate = command with { Title = "Second title (should be ignored)" };

        // Act — send same ID twice
        await _fixture.ExecuteMessageAsync(command);
        await _fixture.ExecuteMessageAsync(commandDuplicate);

        // Assert — only one document, first title preserved
        using var session = _fixture.GetDocumentSession();
        var request = await session.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Title.ShouldBe("First title");
    }

    // ───────────────────────────────────────────────
    // SubmitChangeRequest handler
    // ───────────────────────────────────────────────

    [Fact]
    public async Task SubmitChangeRequest_TransitionsToDraftToSubmitted_AndPublishesHubMessage()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "DOG-FOOD-001", ChangeRequestType.Description);

        // Act
        var session = await _fixture.TrackMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Assert — status updated to Submitted
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.Submitted);
        request.SubmittedAt.ShouldNotBeNull();

        // Assert — ChangeRequestStatusUpdated hub message published
        var hubMsg = session.Sent.MessagesOf<ChangeRequestStatusUpdated>().FirstOrDefault();
        hubMsg.ShouldNotBeNull();
        hubMsg.VendorTenantId.ShouldBe(tenantId);
        hubMsg.RequestId.ShouldBe(requestId);
        hubMsg.Status.ShouldBe("Submitted");
    }

    [Fact]
    public async Task SubmitChangeRequest_RejectsRequest_WhenTenantMismatch()
    {
        // Arrange
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, ownerTenantId, "CAT-FOOD-001", ChangeRequestType.Description);

        // Act — attempt to submit with different tenant
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: otherTenantId));

        // Assert — request still in Draft (cross-tenant silently rejected)
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.Draft);
    }

    [Fact]
    public async Task SubmitChangeRequest_SupersedesExistingActive_ForSameSkuAndType()
    {
        // Arrange — create and submit a first request
        var tenantId = Guid.NewGuid();
        var firstRequestId = Guid.NewGuid();
        var secondRequestId = Guid.NewGuid();

        await CreateDraftAsync(firstRequestId, tenantId, "DOG-FOOD-001", ChangeRequestType.Description);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: firstRequestId, VendorTenantId: tenantId));

        // Create a second draft for the same SKU+Type
        await CreateDraftAsync(secondRequestId, tenantId, "DOG-FOOD-001", ChangeRequestType.Description);

        // Act — submit the second; should supersede the first
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: secondRequestId, VendorTenantId: tenantId));

        // Assert
        using var docSession = _fixture.GetDocumentSession();

        var firstRequest = await docSession.LoadAsync<ChangeRequest>(firstRequestId);
        firstRequest.ShouldNotBeNull();
        firstRequest.Status.ShouldBe(ChangeRequestStatus.Superseded);
        firstRequest.ReplacedByRequestId.ShouldBe(secondRequestId);

        var secondRequest = await docSession.LoadAsync<ChangeRequest>(secondRequestId);
        secondRequest.ShouldNotBeNull();
        secondRequest.Status.ShouldBe(ChangeRequestStatus.Submitted);
    }

    [Fact]
    public async Task SubmitChangeRequest_DoesNotSupersede_DifferentType()
    {
        // Arrange — create Description and Image requests for same SKU
        var tenantId = Guid.NewGuid();
        var descriptionRequestId = Guid.NewGuid();
        var imageRequestId = Guid.NewGuid();

        await CreateDraftAsync(descriptionRequestId, tenantId, "DOG-FOOD-001", ChangeRequestType.Description);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: descriptionRequestId, VendorTenantId: tenantId));

        await CreateDraftAsync(imageRequestId, tenantId, "DOG-FOOD-001", ChangeRequestType.Image);

        // Act — submit Image request; Description should NOT be superseded
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: imageRequestId, VendorTenantId: tenantId));

        // Assert — both Submitted, no supersession
        using var docSession = _fixture.GetDocumentSession();

        var descReq = await docSession.LoadAsync<ChangeRequest>(descriptionRequestId);
        descReq.ShouldNotBeNull();
        descReq.Status.ShouldBe(ChangeRequestStatus.Submitted);

        var imgReq = await docSession.LoadAsync<ChangeRequest>(imageRequestId);
        imgReq.ShouldNotBeNull();
        imgReq.Status.ShouldBe(ChangeRequestStatus.Submitted);
    }

    // ───────────────────────────────────────────────
    // WithdrawChangeRequest handler
    // ───────────────────────────────────────────────

    [Fact]
    public async Task WithdrawChangeRequest_TransitionsToWithdrawn_FromDraft()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "DOG-LEASH-001", ChangeRequestType.Description);

        // Act
        var session = await _fixture.TrackMessageAsync(
            new WithdrawChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Assert
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.Withdrawn);
        request.ResolvedAt.ShouldNotBeNull();
        request.IsActive.ShouldBeFalse();

        // Hub message published
        var hubMsg = session.Sent.MessagesOf<ChangeRequestStatusUpdated>().FirstOrDefault();
        hubMsg.ShouldNotBeNull();
        hubMsg.Status.ShouldBe("Withdrawn");
    }

    [Fact]
    public async Task WithdrawChangeRequest_TransitionsToWithdrawn_FromSubmitted()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "CAT-COLLAR-001", ChangeRequestType.Description);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Act
        await _fixture.ExecuteMessageAsync(
            new WithdrawChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Assert
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.Withdrawn);
    }

    [Fact]
    public async Task WithdrawChangeRequest_IgnoresRequest_WhenAlreadyTerminal()
    {
        // Arrange — request already approved
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await CreateDraftAsync(requestId, tenantId, "DOG-BED-001", ChangeRequestType.Description);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));
        await _fixture.ExecuteMessageAsync(
            new DescriptionChangeApproved(requestId, "DOG-BED-001", tenantId, DateTimeOffset.UtcNow));

        // Act — attempt to withdraw approved request
        await _fixture.ExecuteMessageAsync(
            new WithdrawChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Assert — still Approved
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.Approved);
    }

    // ───────────────────────────────────────────────
    // Catalog BC response handlers — approve
    // ───────────────────────────────────────────────

    [Fact]
    public async Task DescriptionChangeApproved_TransitionsToApproved_AndPublishesBothHubMessages()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "DOG-FOOD-001", ChangeRequestType.Description, userId);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Act
        var session = await _fixture.TrackMessageAsync(
            new DescriptionChangeApproved(requestId, "DOG-FOOD-001", tenantId, DateTimeOffset.UtcNow));

        // Assert — status updated
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.Approved);
        request.ResolvedAt.ShouldNotBeNull();
        request.IsActive.ShouldBeFalse();

        // Tenant broadcast published
        var tenantMsg = session.Sent.MessagesOf<ChangeRequestStatusUpdated>().FirstOrDefault();
        tenantMsg.ShouldNotBeNull();
        tenantMsg.VendorTenantId.ShouldBe(tenantId);
        tenantMsg.Status.ShouldBe("Approved");

        // Personal decision toast published
        var personalMsg = session.Sent.MessagesOf<ChangeRequestDecisionPersonal>().FirstOrDefault();
        personalMsg.ShouldNotBeNull();
        personalMsg.VendorUserId.ShouldBe(userId);
        personalMsg.Decision.ShouldBe("Approved");
        personalMsg.Reason.ShouldBeNull();
    }

    [Fact]
    public async Task DescriptionChangeRejected_TransitionsToRejected_WithReason()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "DOG-FOOD-001", ChangeRequestType.Description, userId);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Act
        var session = await _fixture.TrackMessageAsync(
            new DescriptionChangeRejected(requestId, "DOG-FOOD-001", tenantId, "Description too vague", DateTimeOffset.UtcNow));

        // Assert
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.Rejected);
        request.RejectionReason.ShouldBe("Description too vague");

        var personalMsg = session.Sent.MessagesOf<ChangeRequestDecisionPersonal>().FirstOrDefault();
        personalMsg.ShouldNotBeNull();
        personalMsg.Decision.ShouldBe("Rejected");
        personalMsg.Reason.ShouldBe("Description too vague");
    }

    [Fact]
    public async Task CatalogResponse_IsIdempotent_OnAlreadyApproved()
    {
        // Arrange — approve a request
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "CAT-FOOD-001", ChangeRequestType.Description);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));
        await _fixture.ExecuteMessageAsync(
            new DescriptionChangeApproved(requestId, "CAT-FOOD-001", tenantId, DateTimeOffset.UtcNow));

        // Act — send approval again (at-least-once delivery)
        await _fixture.ExecuteMessageAsync(
            new DescriptionChangeApproved(requestId, "CAT-FOOD-001", tenantId, DateTimeOffset.UtcNow));

        // Assert — still Approved, no exception
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.Approved);
    }

    // ───────────────────────────────────────────────
    // MoreInfoRequested handler
    // ───────────────────────────────────────────────

    [Fact]
    public async Task MoreInfoRequestedForChangeRequest_TransitionsToNeedsMoreInfo()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "DOG-FOOD-001", ChangeRequestType.Description, userId);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Act
        var session = await _fixture.TrackMessageAsync(
            new MoreInfoRequestedForChangeRequest(
                requestId, "DOG-FOOD-001", tenantId,
                "Can you provide a source for this claim?",
                DateTimeOffset.UtcNow));

        // Assert
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.NeedsMoreInfo);
        request.Question.ShouldBe("Can you provide a source for this claim?");
        request.IsActive.ShouldBeTrue();

        // Personal toast published
        var personalMsg = session.Sent.MessagesOf<ChangeRequestDecisionPersonal>().FirstOrDefault();
        personalMsg.ShouldNotBeNull();
        personalMsg.Decision.ShouldBe("NeedsMoreInfo");
    }

    // ───────────────────────────────────────────────
    // ProvideAdditionalInfo handler
    // ───────────────────────────────────────────────

    [Fact]
    public async Task ProvideAdditionalInfo_TransitionsBackToSubmitted()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "DOG-FOOD-001", ChangeRequestType.Description);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));
        await _fixture.ExecuteMessageAsync(
            new MoreInfoRequestedForChangeRequest(
                requestId, "DOG-FOOD-001", tenantId,
                "What is the protein source?",
                DateTimeOffset.UtcNow));

        // Act
        await _fixture.ExecuteMessageAsync(
            new ProvideAdditionalInfo(
                RequestId: requestId,
                VendorTenantId: tenantId,
                Response: "The protein source is wild-caught salmon."));

        // Assert
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.Submitted);
        // Response is now stored in the structured InfoResponses list
        request.InfoResponses.ShouldNotBeEmpty();
        request.InfoResponses[0].Response.ShouldContain("wild-caught salmon");
    }

    // ───────────────────────────────────────────────
    // HTTP endpoint tests
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetChangeRequests_ReturnsEmpty_WhenNoRequestsExist()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var jwt = _fixture.CreateTestJwt(tenantId, role: "Admin");

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/change-requests");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<ChangeRequestListResponseDto>();

        // Assert
        response.ShouldNotBeNull();
        response.TotalCount.ShouldBe(0);
        response.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetChangeRequests_ReturnsTenantRequests_OnlyForAuthenticatedTenant()
    {
        // Arrange — create requests for two tenants
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await CreateDraftAsync(Guid.NewGuid(), tenantAId, "DOG-FOOD-001", ChangeRequestType.Description);
        await CreateDraftAsync(Guid.NewGuid(), tenantAId, "CAT-FOOD-001", ChangeRequestType.Image);
        await CreateDraftAsync(Guid.NewGuid(), tenantBId, "BIRD-SEED-001", ChangeRequestType.Description);

        var jwtA = _fixture.CreateTestJwt(tenantAId, role: "Admin");

        // Act — tenant A queries
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/change-requests");
            s.WithRequestHeader("Authorization", $"Bearer {jwtA}");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<ChangeRequestListResponseDto>();

        // Assert — only tenant A's 2 requests
        response.ShouldNotBeNull();
        response.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetChangeRequests_ReturnsUnauthorized_WithoutJwt()
    {
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/change-requests");
            s.StatusCodeShouldBe(401);
        });
    }

    [Fact]
    public async Task GetChangeRequest_ReturnsNotFound_ForDifferentTenant()
    {
        // Arrange
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, ownerTenantId, "DOG-FOOD-001", ChangeRequestType.Description);

        var jwtOther = _fixture.CreateTestJwt(otherTenantId, role: "Admin");

        // Act — other tenant tries to access
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/vendor-portal/change-requests/{requestId}");
            s.WithRequestHeader("Authorization", $"Bearer {jwtOther}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task GetDashboard_ReturnsPendingChangeRequestCount_FromDatabase()
    {
        // Arrange — create 2 drafts and 1 submitted for one tenant
        var tenantId = Guid.NewGuid();

        await CreateDraftAsync(Guid.NewGuid(), tenantId, "DOG-FOOD-001", ChangeRequestType.Description);
        await CreateDraftAsync(Guid.NewGuid(), tenantId, "CAT-FOOD-001", ChangeRequestType.Image);

        var submittedId = Guid.NewGuid();
        await CreateDraftAsync(submittedId, tenantId, "FISH-FOOD-001", ChangeRequestType.DataCorrection);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: submittedId, VendorTenantId: tenantId));

        var jwt = _fixture.CreateTestJwt(tenantId, role: "Admin");

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/dashboard");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<DashboardSummaryDto>();

        // Assert — 3 pending (2 Draft + 1 Submitted)
        response.ShouldNotBeNull();
        response.PendingChangeRequests.ShouldBe(3);
    }

    // ───────────────────────────────────────────────
    // P0: Additional tenant isolation (handler-level)
    // ───────────────────────────────────────────────

    [Fact]
    public async Task ProvideAdditionalInfo_IgnoresRequest_WhenTenantMismatch()
    {
        // Arrange — put request in NeedsMoreInfo for tenantA
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantAId, "DOG-FOOD-001", ChangeRequestType.Description);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantAId));
        await _fixture.ExecuteMessageAsync(
            new MoreInfoRequestedForChangeRequest(
                requestId, "DOG-FOOD-001", tenantAId, "What is the protein source?", DateTimeOffset.UtcNow));

        // Act — tenant B attempts to provide info for tenant A's request
        await _fixture.ExecuteMessageAsync(
            new ProvideAdditionalInfo(
                RequestId: requestId,
                VendorTenantId: tenantBId,
                Response: "Injected response from wrong tenant."));

        // Assert — request stays in NeedsMoreInfo, cross-tenant silently rejected
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.NeedsMoreInfo);
    }

    [Fact]
    public async Task WithdrawChangeRequest_IgnoresRequest_WhenTenantMismatch()
    {
        // Arrange
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, ownerTenantId, "CAT-BED-001", ChangeRequestType.Description);

        // Act — different tenant attempts to withdraw
        await _fixture.ExecuteMessageAsync(
            new WithdrawChangeRequest(RequestId: requestId, VendorTenantId: otherTenantId));

        // Assert — request still in Draft, cross-tenant silently rejected
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.Draft);
    }

    // ───────────────────────────────────────────────
    // P1: State transition guards
    // ───────────────────────────────────────────────

    [Fact]
    public async Task SubmitChangeRequest_IsIdempotent_WhenAlreadySubmitted()
    {
        // Arrange — submit a request, then attempt to submit it again
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "DOG-FOOD-002", ChangeRequestType.Description);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Verify it's Submitted
        using (var s1 = _fixture.GetDocumentSession())
        {
            var req = await s1.LoadAsync<ChangeRequest>(requestId);
            req.ShouldNotBeNull();
            req.Status.ShouldBe(ChangeRequestStatus.Submitted);
        }

        // Act — submit again (at-least-once delivery duplicate)
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Assert — still Submitted, no side effects
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.Submitted);
    }

    [Fact]
    public async Task ProvideAdditionalInfo_IsIgnored_WhenRequestIsNotInNeedsMoreInfo()
    {
        // Arrange — request is in Draft (not NeedsMoreInfo)
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "BIRD-CAGE-001", ChangeRequestType.Description);

        // Act — attempt to provide info while still Draft
        await _fixture.ExecuteMessageAsync(
            new ProvideAdditionalInfo(
                RequestId: requestId,
                VendorTenantId: tenantId,
                Response: "This response should be ignored."));

        // Assert — request stays in Draft; invalid transition rejected
        using var docSession = _fixture.GetDocumentSession();
        var request = await docSession.LoadAsync<ChangeRequest>(requestId);
        request.ShouldNotBeNull();
        request.Status.ShouldBe(ChangeRequestStatus.Draft);
        request.AdditionalNotes.ShouldBeNull();
    }

    // ───────────────────────────────────────────────
    // P2: Role authorization on HTTP endpoints
    // ───────────────────────────────────────────────

    [Fact]
    public async Task DraftChangeRequest_Returns403_WhenReadOnlyRole()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var jwt = _fixture.CreateTestJwt(tenantId, role: "ReadOnly");

        var body = new
        {
            RequestId = Guid.NewGuid(),
            Sku = "DOG-FOOD-001",
            Type = "Description",
            Title = "Update description",
            Details = "Some new details."
        };

        // Act & Assert — ReadOnly role cannot create drafts
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(body).ToUrl("/api/vendor-portal/change-requests/draft");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(403);
        });
    }

    [Fact]
    public async Task SubmitChangeRequest_Returns403_WhenReadOnlyRole()
    {
        // Arrange — create a Draft as Admin first (via handler bypass), then try HTTP submit as ReadOnly
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await CreateDraftAsync(requestId, tenantId, "CAT-TREE-001", ChangeRequestType.Description);

        var jwt = _fixture.CreateTestJwt(tenantId, role: "ReadOnly");

        // Act & Assert — ReadOnly role cannot submit
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Url($"/api/vendor-portal/change-requests/{requestId}/submit");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(403);
        });
    }

    [Fact]
    public async Task DraftChangeRequest_Returns403_WhenTenantIsSuspended()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var jwt = _fixture.CreateTestJwt(tenantId, role: "Admin", tenantStatus: "Suspended");

        var body = new
        {
            RequestId = Guid.NewGuid(),
            Sku = "DOG-FOOD-001",
            Type = "Description",
            Title = "Update description",
            Details = "Some new details."
        };

        // Act & Assert — Suspended tenant cannot draft
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(body).ToUrl("/api/vendor-portal/change-requests/draft");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(403);
        });
    }

    [Fact]
    public async Task SubmitChangeRequest_Returns403_WhenTenantIsSuspended()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await CreateDraftAsync(requestId, tenantId, "CAT-TREE-001", ChangeRequestType.Description);

        var jwt = _fixture.CreateTestJwt(tenantId, role: "Admin", tenantStatus: "Suspended");

        // Act & Assert — Suspended tenant cannot submit via HTTP
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Url($"/api/vendor-portal/change-requests/{requestId}/submit");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(403);
        });
    }

    // ───────────────────────────────────────────────
    // P3: Edge cases
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetChangeRequests_FiltersCorrectly_ByStatus()
    {
        // Arrange — create 2 Drafts and 1 Submitted for one tenant
        var tenantId = Guid.NewGuid();

        await CreateDraftAsync(Guid.NewGuid(), tenantId, "DOG-FOOD-001", ChangeRequestType.Description);
        await CreateDraftAsync(Guid.NewGuid(), tenantId, "CAT-FOOD-001", ChangeRequestType.Image);

        var submittedId = Guid.NewGuid();
        await CreateDraftAsync(submittedId, tenantId, "FISH-FOOD-001", ChangeRequestType.DataCorrection);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: submittedId, VendorTenantId: tenantId));

        var jwt = _fixture.CreateTestJwt(tenantId, role: "Admin");

        // Act — filter by Draft status
        var draftResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/change-requests?status=Draft");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(200);
        });
        var draftResponse = await draftResult.ReadAsJsonAsync<ChangeRequestListResponseDto>();

        // Act — filter by Submitted status
        var submittedResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/change-requests?status=Submitted");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(200);
        });
        var submittedResponse = await submittedResult.ReadAsJsonAsync<ChangeRequestListResponseDto>();

        // Assert
        draftResponse.ShouldNotBeNull();
        draftResponse.TotalCount.ShouldBe(2);

        submittedResponse.ShouldNotBeNull();
        submittedResponse.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetDashboard_ExcludesTerminalStates_FromPendingCount()
    {
        // Arrange — 1 Draft, 1 Approved (terminal), 1 Rejected (terminal), 1 Withdrawn (terminal)
        var tenantId = Guid.NewGuid();

        // Draft (active — should count)
        await CreateDraftAsync(Guid.NewGuid(), tenantId, "DOG-FOOD-001", ChangeRequestType.Description);

        // Approved (terminal — should NOT count)
        var approvedId = Guid.NewGuid();
        await CreateDraftAsync(approvedId, tenantId, "CAT-FOOD-001", ChangeRequestType.Description);
        await _fixture.ExecuteMessageAsync(new SubmitChangeRequest(RequestId: approvedId, VendorTenantId: tenantId));
        await _fixture.ExecuteMessageAsync(
            new DescriptionChangeApproved(approvedId, "CAT-FOOD-001", tenantId, DateTimeOffset.UtcNow));

        // Rejected (terminal — should NOT count)
        var rejectedId = Guid.NewGuid();
        await CreateDraftAsync(rejectedId, tenantId, "FISH-FOOD-001", ChangeRequestType.Description);
        await _fixture.ExecuteMessageAsync(new SubmitChangeRequest(RequestId: rejectedId, VendorTenantId: tenantId));
        await _fixture.ExecuteMessageAsync(
            new DescriptionChangeRejected(rejectedId, "FISH-FOOD-001", tenantId, "Not acceptable", DateTimeOffset.UtcNow));

        // Withdrawn (terminal — should NOT count)
        var withdrawnId = Guid.NewGuid();
        await CreateDraftAsync(withdrawnId, tenantId, "BIRD-SEED-001", ChangeRequestType.Description);
        await _fixture.ExecuteMessageAsync(new WithdrawChangeRequest(RequestId: withdrawnId, VendorTenantId: tenantId));

        var jwt = _fixture.CreateTestJwt(tenantId, role: "Admin");

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/dashboard");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<DashboardSummaryDto>();

        // Assert — only the 1 Draft counts; the 3 terminal ones do not
        response.ShouldNotBeNull();
        response.PendingChangeRequests.ShouldBe(1);
    }

    // ───────────────────────────────────────────────
    // P4: ChangeType field on hub messages
    // ───────────────────────────────────────────────

    [Fact]
    public async Task DescriptionChangeApproved_PersonalDecisionMessage_IncludesCorrectChangeType()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "DOG-FOOD-001", ChangeRequestType.Description, userId);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Act
        var session = await _fixture.TrackMessageAsync(
            new DescriptionChangeApproved(requestId, "DOG-FOOD-001", tenantId, DateTimeOffset.UtcNow));

        // Assert — ChangeType field on the personal decision message matches the request type
        var personalMsg = session.Sent.MessagesOf<ChangeRequestDecisionPersonal>().FirstOrDefault();
        personalMsg.ShouldNotBeNull();
        personalMsg.ChangeType.ShouldBe("Description");
    }

    [Fact]
    public async Task ImageChangeApproved_PersonalDecisionMessage_IncludesCorrectChangeType()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "DOG-TOY-001", ChangeRequestType.Image, userId);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Act — Catalog BC approves the image change
        var session = await _fixture.TrackMessageAsync(
            new ImageChangeApproved(requestId, "DOG-TOY-001", tenantId, DateTimeOffset.UtcNow));

        // Assert
        var personalMsg = session.Sent.MessagesOf<ChangeRequestDecisionPersonal>().FirstOrDefault();
        personalMsg.ShouldNotBeNull();
        personalMsg.Decision.ShouldBe("Approved");
        personalMsg.ChangeType.ShouldBe("Image");
    }

    [Fact]
    public async Task DataCorrectionRejected_PersonalDecisionMessage_IncludesCorrectChangeType()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        await CreateDraftAsync(requestId, tenantId, "HAMSTER-WHEEL-001", ChangeRequestType.DataCorrection, userId);
        await _fixture.ExecuteMessageAsync(
            new SubmitChangeRequest(RequestId: requestId, VendorTenantId: tenantId));

        // Act — Catalog BC rejects the data correction
        var session = await _fixture.TrackMessageAsync(
            new DataCorrectionRejected(requestId, "HAMSTER-WHEEL-001", tenantId, "Incorrect data format", DateTimeOffset.UtcNow));

        // Assert
        var personalMsg = session.Sent.MessagesOf<ChangeRequestDecisionPersonal>().FirstOrDefault();
        personalMsg.ShouldNotBeNull();
        personalMsg.Decision.ShouldBe("Rejected");
        personalMsg.ChangeType.ShouldBe("DataCorrection");
        personalMsg.Reason.ShouldBe("Incorrect data format");
    }

    // ───────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────

    private async Task CreateDraftAsync(
        Guid requestId,
        Guid tenantId,
        string sku,
        ChangeRequestType type,
        Guid? userId = null)
    {
        var command = new DraftChangeRequest(
            RequestId: requestId,
            VendorTenantId: tenantId,
            SubmittedByUserId: userId ?? Guid.NewGuid(),
            Sku: sku,
            Type: type,
            Title: $"Test request for {sku}",
            Details: "Test details for integration test.");

        await _fixture.ExecuteMessageAsync(command);
    }

    // DTO models for HTTP response deserialization
    private sealed record ChangeRequestListResponseDto(
        IReadOnlyList<ChangeRequestItemDto> Items,
        int TotalCount);

    private sealed record ChangeRequestItemDto(
        Guid Id,
        Guid VendorTenantId,
        string Sku,
        string Type,
        string Status,
        string Title,
        DateTimeOffset CreatedAt,
        DateTimeOffset? SubmittedAt,
        DateTimeOffset? ResolvedAt);

    private sealed record DashboardSummaryDto(
        Guid VendorTenantId,
        string TenantName,
        string UserEmail,
        string UserRole,
        int TotalSkus,
        int PendingChangeRequests,
        int ActiveLowStockAlerts,
        string Message);
}
