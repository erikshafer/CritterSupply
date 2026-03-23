using Alba;
using Marten;
using Messages.Contracts.VendorIdentity;
using Shouldly;
using VendorPortal.VendorAccount;


namespace VendorPortal.Api.IntegrationTests;

/// <summary>
/// Integration tests for Phase 5 — VendorAccount, Saved Dashboard Views, and Notification Preferences:
/// - VendorTenantCreated handler initializes VendorAccount with default preferences
/// - SaveDashboardView / DeleteDashboardView lifecycle
/// - UpdateNotificationPreferences opt-out model
/// - HTTP endpoint tests for account management
/// - Multi-tenant isolation (cross-tenant access returns empty/default)
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class VendorAccountTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public VendorAccountTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ───────────────────────────────────────────────
    // VendorTenantCreated → VendorAccount initialization
    // ───────────────────────────────────────────────

    [Fact]
    public async Task VendorTenantCreated_InitializesVendorAccount_WithDefaultPreferences()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var message = new VendorTenantCreated(
            VendorTenantId: tenantId,
            OrganizationName: "Acme Pet Supplies",
            ContactEmail: "admin@acmepets.com",
            CreatedAt: DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteMessageAsync(message);

        // Assert
        using var session = _fixture.GetDocumentSession();
        var account = await session.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId);

        account.ShouldNotBeNull();
        account.Id.ShouldBe(tenantId);
        account.VendorTenantId.ShouldBe(tenantId);
        account.OrganizationName.ShouldBe("Acme Pet Supplies");
        account.ContactEmail.ShouldBe("admin@acmepets.com");
        account.NotificationPreferences.ShouldNotBeNull();
        account.NotificationPreferences.LowStockAlerts.ShouldBeTrue();
        account.NotificationPreferences.ChangeRequestDecisions.ShouldBeTrue();
        account.NotificationPreferences.InventoryUpdates.ShouldBeTrue();
        account.NotificationPreferences.SalesMetrics.ShouldBeTrue();
        account.SavedDashboardViews.ShouldBeEmpty();
    }

    [Fact]
    public async Task VendorTenantCreated_IsIdempotent_WhenSameTenantCreatedTwice()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var message = new VendorTenantCreated(
            VendorTenantId: tenantId,
            OrganizationName: "Acme Pet Supplies",
            ContactEmail: "admin@acmepets.com",
            CreatedAt: DateTimeOffset.UtcNow);

        // Act — send twice
        await _fixture.ExecuteMessageAsync(message);
        await _fixture.ExecuteMessageAsync(message);

        // Assert — still only one account
        using var session = _fixture.GetDocumentSession();
        var account = await session.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId);
        account.ShouldNotBeNull();
        account.OrganizationName.ShouldBe("Acme Pet Supplies");
    }

    // ───────────────────────────────────────────────
    // SaveDashboardView handler
    // ───────────────────────────────────────────────

    [Fact]
    public async Task SaveDashboardView_CreatesView_InAccount()
    {
        // Arrange — create account first
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);

        var command = new SaveDashboardViewCommand(
            VendorTenantId: tenantId,
            ViewName: "My Low Stock Overview",
            FilterCriteria: new DashboardFilterCriteria
            {
                LowStockOnly = true,
                WarehouseId = "WH-001",
            });

        // Act
        await _fixture.ExecuteMessageAsync(command);

        // Assert — verify persisted via Marten session
        using var session = _fixture.GetDocumentSession();
        var account = await session.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId);
        account.ShouldNotBeNull();
        account.SavedDashboardViews.Count.ShouldBe(1);
        account.SavedDashboardViews[0].ViewName.ShouldBe("My Low Stock Overview");
        account.SavedDashboardViews[0].FilterCriteria.LowStockOnly.ShouldBe(true);
        account.SavedDashboardViews[0].FilterCriteria.WarehouseId.ShouldBe("WH-001");
        account.SavedDashboardViews[0].ViewId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task SaveDashboardView_AllowsMultipleViews_PerAccount()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);

        var command1 = new SaveDashboardViewCommand(tenantId, "View A", new DashboardFilterCriteria());
        var command2 = new SaveDashboardViewCommand(tenantId, "View B", new DashboardFilterCriteria { LowStockOnly = true });

        // Act
        await _fixture.ExecuteMessageAsync(command1);
        await _fixture.ExecuteMessageAsync(command2);

        // Assert
        using var session = _fixture.GetDocumentSession();
        var account = await session.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId);
        account.ShouldNotBeNull();
        account.SavedDashboardViews.Count.ShouldBe(2);
    }

    // ───────────────────────────────────────────────
    // DeleteDashboardView handler
    // ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteDashboardView_RemovesView_FromAccount()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);

        // Save a view first
        var saveCommand = new SaveDashboardViewCommand(tenantId, "Temp View", new DashboardFilterCriteria());
        await _fixture.ExecuteMessageAsync(saveCommand);

        // Retrieve the saved view ID
        using var sessionBefore = _fixture.GetDocumentSession();
        var accountBefore = await sessionBefore.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId);
        accountBefore.ShouldNotBeNull();
        accountBefore.SavedDashboardViews.Count.ShouldBe(1);
        var viewId = accountBefore.SavedDashboardViews[0].ViewId;

        var deleteCommand = new DeleteDashboardViewCommand(tenantId, viewId);

        // Act
        await _fixture.ExecuteMessageAsync(deleteCommand);

        // Assert
        using var sessionAfter = _fixture.GetDocumentSession();
        var accountAfter = await sessionAfter.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId);
        accountAfter.ShouldNotBeNull();
        accountAfter.SavedDashboardViews.ShouldBeEmpty();
    }

    // ───────────────────────────────────────────────
    // UpdateNotificationPreferences handler
    // ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateNotificationPreferences_PersistsChanges()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);

        var command = new UpdateNotificationPreferencesCommand(
            VendorTenantId: tenantId,
            LowStockAlerts: false,
            ChangeRequestDecisions: true,
            InventoryUpdates: false,
            SalesMetrics: true);

        // Act
        await _fixture.ExecuteMessageAsync(command);

        // Assert — verify persisted
        using var session = _fixture.GetDocumentSession();
        var account = await session.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId);
        account.ShouldNotBeNull();
        account.NotificationPreferences.LowStockAlerts.ShouldBeFalse();
        account.NotificationPreferences.ChangeRequestDecisions.ShouldBeTrue();
        account.NotificationPreferences.InventoryUpdates.ShouldBeFalse();
        account.NotificationPreferences.SalesMetrics.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateNotificationPreferences_DisableAll_ThenReEnable()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);

        // Disable all
        var disableAll = new UpdateNotificationPreferencesCommand(tenantId, false, false, false, false);
        await _fixture.ExecuteMessageAsync(disableAll);

        // Re-enable all
        var enableAll = new UpdateNotificationPreferencesCommand(tenantId, true, true, true, true);
        await _fixture.ExecuteMessageAsync(enableAll);

        // Assert
        using var session = _fixture.GetDocumentSession();
        var account = await session.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId);
        account.ShouldNotBeNull();
        account.NotificationPreferences.LowStockAlerts.ShouldBeTrue();
        account.NotificationPreferences.ChangeRequestDecisions.ShouldBeTrue();
        account.NotificationPreferences.InventoryUpdates.ShouldBeTrue();
        account.NotificationPreferences.SalesMetrics.ShouldBeTrue();
    }

    // ───────────────────────────────────────────────
    // HTTP Endpoint Tests — Dashboard Views
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetDashboardViews_ReturnsEmptyList_WhenNoAccount()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var jwt = _fixture.CreateTestJwt(tenantId);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(200);
        });

        var response = result.ReadAsJson<DashboardViewsResponse>();
        response.ShouldNotBeNull();
        response.Views.ShouldBeEmpty();
    }

    [Fact]
    public async Task PostDashboardView_CreatesAndReturns201()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);
        var jwt = _fixture.CreateTestJwt(tenantId);

        var request = new { ViewName = "My Test View", FilterCriteria = new { LowStockOnly = true } };

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(request).ToUrl("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(201);
        });

        var response = result.ReadAsJson<SaveDashboardViewApiResponse>();
        response.ShouldNotBeNull();
        response.ViewName.ShouldBe("My Test View");
        response.ViewId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task PostDashboardView_Returns404_WhenNoAccount()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var jwt = _fixture.CreateTestJwt(tenantId);
        var request = new { ViewName = "Ghost View" };

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(request).ToUrl("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task PostDashboardView_Returns400_WhenViewNameEmpty()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);
        var jwt = _fixture.CreateTestJwt(tenantId);
        var request = new { ViewName = "  " };

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(request).ToUrl("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task DeleteDashboardView_Returns204_WhenViewExists()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);
        var jwt = _fixture.CreateTestJwt(tenantId);

        // Create a view first via HTTP
        var request = new { ViewName = "To Be Deleted" };
        var createResult = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(request).ToUrl("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(201);
        });

        var created = createResult.ReadAsJson<SaveDashboardViewApiResponse>();
        created.ShouldNotBeNull();

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"/api/vendor-portal/account/dashboard-views/{created.ViewId}");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(204);
        });
    }

    [Fact]
    public async Task DeleteDashboardView_Returns404_WhenViewDoesNotExist()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);
        var jwt = _fixture.CreateTestJwt(tenantId);

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"/api/vendor-portal/account/dashboard-views/{Guid.NewGuid()}");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(404);
        });
    }

    // ───────────────────────────────────────────────
    // HTTP Endpoint Tests — Notification Preferences
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetNotificationPreferences_ReturnsDefaults_WhenNoAccount()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var jwt = _fixture.CreateTestJwt(tenantId);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/account/preferences");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(200);
        });

        var response = result.ReadAsJson<NotificationPreferencesApiResponse>();
        response.ShouldNotBeNull();
        response.LowStockAlerts.ShouldBeTrue();
        response.ChangeRequestDecisions.ShouldBeTrue();
        response.InventoryUpdates.ShouldBeTrue();
        response.SalesMetrics.ShouldBeTrue();
    }

    [Fact]
    public async Task PutNotificationPreferences_UpdatesAndReturns200()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);
        var jwt = _fixture.CreateTestJwt(tenantId);

        var request = new
        {
            LowStockAlerts = false,
            ChangeRequestDecisions = true,
            InventoryUpdates = false,
            SalesMetrics = true
        };

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(request).ToUrl("/api/vendor-portal/account/preferences");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(200);
        });

        var response = result.ReadAsJson<NotificationPreferencesApiResponse>();
        response.ShouldNotBeNull();
        response.LowStockAlerts.ShouldBeFalse();
        response.ChangeRequestDecisions.ShouldBeTrue();
        response.InventoryUpdates.ShouldBeFalse();
        response.SalesMetrics.ShouldBeTrue();
    }

    [Fact]
    public async Task PutNotificationPreferences_Returns404_WhenNoAccount()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var jwt = _fixture.CreateTestJwt(tenantId);
        var request = new
        {
            LowStockAlerts = true,
            ChangeRequestDecisions = true,
            InventoryUpdates = true,
            SalesMetrics = true
        };

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(request).ToUrl("/api/vendor-portal/account/preferences");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(404);
        });
    }

    // ───────────────────────────────────────────────
    // Multi-tenant isolation
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetDashboardViews_ReturnsEmpty_ForDifferentTenant()
    {
        // Arrange — create account for tenant A
        var tenantA = Guid.NewGuid();
        await CreateTestAccount(tenantA);

        // Save a view for tenant A
        var saveCommand = new SaveDashboardViewCommand(tenantA, "Tenant A View", new DashboardFilterCriteria());
        await _fixture.ExecuteMessageAsync(saveCommand);

        // Tenant B tries to list views
        var tenantB = Guid.NewGuid();
        var jwtB = _fixture.CreateTestJwt(tenantB);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwtB}");
            s.StatusCodeShouldBe(200);
        });

        var response = result.ReadAsJson<DashboardViewsResponse>();
        response.ShouldNotBeNull();
        response.Views.ShouldBeEmpty();
    }

    [Fact]
    public async Task Endpoints_Return401_WithoutJwt()
    {
        // Act & Assert — GET dashboard views
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/account/dashboard-views");
            s.StatusCodeShouldBe(401);
        });

        // GET preferences
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/account/preferences");
            s.StatusCodeShouldBe(401);
        });
    }

    // QA Required #2: Write endpoints also require JWT
    [Fact]
    public async Task WriteEndpoints_Return401_WithoutJwt()
    {
        // POST dashboard view
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { ViewName = "test" })
                .ToUrl("/api/vendor-portal/account/dashboard-views");
            s.StatusCodeShouldBe(401);
        });

        // DELETE dashboard view
        await _fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"/api/vendor-portal/account/dashboard-views/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(401);
        });

        // PUT preferences
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(new
            {
                LowStockAlerts = true, ChangeRequestDecisions = true,
                InventoryUpdates = true, SalesMetrics = true
            }).ToUrl("/api/vendor-portal/account/preferences");
            s.StatusCodeShouldBe(401);
        });
    }

    // QA Required #1: Duplicate view name constraint (matches feature file spec)
    [Fact]
    public async Task PostDashboardView_Returns409_WhenDuplicateViewName()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);
        var jwt = _fixture.CreateTestJwt(tenantId);

        var request = new { ViewName = "Top Products", FilterCriteria = new { LowStockOnly = true } };

        // Create first view — should succeed
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(request).ToUrl("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(201);
        });

        // Act — attempt duplicate name
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(request).ToUrl("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(409);
        });
    }

    // QA Required #1 (case-insensitive): Duplicate check ignores case
    [Fact]
    public async Task PostDashboardView_Returns409_WhenDuplicateViewName_CaseInsensitive()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);
        var jwt = _fixture.CreateTestJwt(tenantId);

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { ViewName = "My View" }).ToUrl("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(201);
        });

        // Act — same name different case
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { ViewName = "my view" }).ToUrl("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(409);
        });
    }

    // QA Required #3: Cross-tenant DELETE isolation
    [Fact]
    public async Task DeleteDashboardView_CannotDeleteAnotherTenantView()
    {
        // Arrange — tenant A creates a view
        var tenantA = Guid.NewGuid();
        await CreateTestAccount(tenantA);
        var saveCommand = new SaveDashboardViewCommand(tenantA, "Tenant A Private", new DashboardFilterCriteria());
        await _fixture.ExecuteMessageAsync(saveCommand);

        using var session = _fixture.GetDocumentSession();
        var accountA = await session.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantA);
        accountA.ShouldNotBeNull();
        var viewId = accountA.SavedDashboardViews[0].ViewId;

        // Tenant B tries to delete tenant A's view by ID
        var tenantB = Guid.NewGuid();
        await CreateTestAccount(tenantB);
        var jwtB = _fixture.CreateTestJwt(tenantB);

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"/api/vendor-portal/account/dashboard-views/{viewId}");
            s.WithRequestHeader("Authorization", $"Bearer {jwtB}");
            s.StatusCodeShouldBe(404); // B's account doesn't have this view
        });

        // Assert — tenant A's view is still intact
        using var verifySession = _fixture.GetDocumentSession();
        var accountAAfter = await verifySession.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantA);
        accountAAfter.ShouldNotBeNull();
        accountAAfter.SavedDashboardViews.Count.ShouldBe(1);
    }

    // QA Recommended #4: POST → GET round-trip
    [Fact]
    public async Task PostDashboardView_PersistedView_IsReturnedByGet()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);
        var jwt = _fixture.CreateTestJwt(tenantId);

        // POST
        var request = new { ViewName = "Persisted View", FilterCriteria = new { LowStockOnly = true, SkuFilter = "DOG-" } };
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(request).ToUrl("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(201);
        });

        // GET
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(200);
        });

        var response = getResult.ReadAsJson<DashboardViewsResponse>();
        response.ShouldNotBeNull();
        response.Views.Count.ShouldBe(1);
        response.Views[0].ViewName.ShouldBe("Persisted View");
    }

    // QA Recommended #5: DELETE → GET round-trip
    [Fact]
    public async Task DeleteDashboardView_ViewIsGone_WhenFetchedAfterDelete()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);
        var jwt = _fixture.CreateTestJwt(tenantId);

        // Create and then delete
        var createResult = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { ViewName = "Ephemeral" }).ToUrl("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(201);
        });
        var created = createResult.ReadAsJson<SaveDashboardViewApiResponse>();
        created.ShouldNotBeNull();

        await _fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"/api/vendor-portal/account/dashboard-views/{created.ViewId}");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(204);
        });

        // GET — should be empty
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(200);
        });

        var response = getResult.ReadAsJson<DashboardViewsResponse>();
        response.ShouldNotBeNull();
        response.Views.ShouldBeEmpty();
    }

    // QA Recommended #6: Account exists but no saved views
    [Fact]
    public async Task GetDashboardViews_ReturnsEmptyList_WhenAccountExistsButNoViews()
    {
        // Arrange — account exists but no views saved
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);
        var jwt = _fixture.CreateTestJwt(tenantId);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/account/dashboard-views");
            s.WithRequestHeader("Authorization", $"Bearer {jwt}");
            s.StatusCodeShouldBe(200);
        });

        var response = result.ReadAsJson<DashboardViewsResponse>();
        response.ShouldNotBeNull();
        response.Views.ShouldBeEmpty();
    }

    // QA Recommended #7: Full filter criteria serialization round-trip
    [Fact]
    public async Task SaveDashboardView_PreservesAllFilterCriteriaFields()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);

        var filterCriteria = new DashboardFilterCriteria
        {
            DateFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            SkuFilter = "DOG-FOOD-",
            LowStockOnly = true,
            WarehouseId = "WH-EAST-001",
        };

        var command = new SaveDashboardViewCommand(tenantId, "Full Filter Test", filterCriteria);

        // Act
        await _fixture.ExecuteMessageAsync(command);

        // Assert — all 5 fields persisted
        using var session = _fixture.GetDocumentSession();
        var account = await session.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId);
        account.ShouldNotBeNull();
        account.SavedDashboardViews.Count.ShouldBe(1);

        var saved = account.SavedDashboardViews[0];
        saved.FilterCriteria.DateFrom.ShouldBe(filterCriteria.DateFrom);
        saved.FilterCriteria.DateTo.ShouldBe(filterCriteria.DateTo);
        saved.FilterCriteria.SkuFilter.ShouldBe("DOG-FOOD-");
        saved.FilterCriteria.LowStockOnly.ShouldBe(true);
        saved.FilterCriteria.WarehouseId.ShouldBe("WH-EAST-001");
    }

    // QA Recommended #8: UpdatedAt timestamp assertions
    [Fact]
    public async Task UpdateNotificationPreferences_UpdatesTimestamp()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await CreateTestAccount(tenantId);

        using var sessionBefore = _fixture.GetDocumentSession();
        var accountBefore = await sessionBefore.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId);
        accountBefore.ShouldNotBeNull();
        var createdAt = accountBefore.CreatedAt;

        // Small delay to ensure timestamp difference
        await Task.Delay(50);

        // Act
        var command = new UpdateNotificationPreferencesCommand(tenantId, false, false, false, false);
        await _fixture.ExecuteMessageAsync(command);

        // Assert
        using var sessionAfter = _fixture.GetDocumentSession();
        var accountAfter = await sessionAfter.LoadAsync<VendorPortal.VendorAccount.VendorAccount>(tenantId);
        accountAfter.ShouldNotBeNull();
        accountAfter.UpdatedAt.ShouldBeGreaterThan(createdAt);
    }

    // ───────────────────────────────────────────────
    // Helper: create a VendorAccount for a given tenant
    // ───────────────────────────────────────────────

    private async Task CreateTestAccount(Guid tenantId)
    {
        var message = new VendorTenantCreated(
            VendorTenantId: tenantId,
            OrganizationName: "Test Vendor",
            ContactEmail: "test@vendor.com",
            CreatedAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteMessageAsync(message);
    }

    // DTO records for JSON deserialization in HTTP tests
    private sealed record DashboardViewsResponse(IReadOnlyList<DashboardViewItem> Views);

    private sealed record DashboardViewItem(
        Guid ViewId,
        string ViewName,
        DateTimeOffset CreatedAt);

    private sealed record SaveDashboardViewApiResponse(
        Guid ViewId,
        string ViewName,
        DateTimeOffset CreatedAt);

    private sealed record NotificationPreferencesApiResponse(
        bool LowStockAlerts,
        bool ChangeRequestDecisions,
        bool InventoryUpdates,
        bool SalesMetrics);
}
