using System.Net;
using System.Net.Http.Json;
using Backoffice.Web.Auth;
using Backoffice.Web.Hub;
using Backoffice.Web.Pages.Returns;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MudBlazor;

namespace Backoffice.Web.Tests.Pages.Returns;

/// <summary>
/// bUnit smoke tests for ReturnManagement page.
/// These tests verify markup structure and basic rendering.
/// Full integration testing with HTTP calls happens in integration tests.
/// </summary>
public sealed class ReturnManagementTests : BunitTestBase
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ISnackbar> _snackbarMock;
    private readonly Mock<ILogger<ReturnManagement>> _loggerMock;
    private readonly Mock<ILogger<BackofficeHubService>> _hubServiceLoggerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly BackofficeAuthState _authState;
    private readonly BackofficeHubService _hubService;
    private readonly SessionExpiredService _sessionExpiredService;

    public ReturnManagementTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _snackbarMock = new Mock<ISnackbar>();
        _loggerMock = new Mock<ILogger<ReturnManagement>>();
        _hubServiceLoggerMock = new Mock<ILogger<BackofficeHubService>>();
        _configurationMock = new Mock<IConfiguration>();

        // Create real auth state with test data
        _authState = new BackofficeAuthState();
        _authState.SetAuthenticated(
            "test-access-token",
            "test@example.com",
            "Test",
            "User",
            "customer-service",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(1));

        // Mock configuration for hub URL
        _configurationMock.Setup(c => c["BackofficeApiUrl"]).Returns("https://localhost:5243");

        // Setup HTTP mock to return empty array (for initial load)
        var httpClient = new HttpClient(new MockHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Array.Empty<object>())
            })));
        _httpClientFactoryMock.Setup(f => f.CreateClient("BackofficeApi")).Returns(httpClient);

        // Create real hub service with mocked dependencies
        _hubService = new BackofficeHubService(_authState, _configurationMock.Object, _hubServiceLoggerMock.Object);

        // Create real session expired service
        _sessionExpiredService = new SessionExpiredService();

        // Register services
        Services.AddSingleton(_httpClientFactoryMock.Object);
        Services.AddSingleton(_snackbarMock.Object);
        Services.AddSingleton(_loggerMock.Object);
        Services.AddSingleton(_authState);
        Services.AddSingleton(_hubService);
        Services.AddSingleton(_sessionExpiredService);
        Services.AddSingleton<NavigationManager>(new MockNavigationManager());
        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new MockAuthenticationStateProvider("customer-service"));
    }

    [Fact]
    public void ReturnManagement_InitialRender_ShowsStatusFilter()
    {
        var cut = RenderWithMud<ReturnManagement>();

        var statusFilter = cut.Find("[data-testid='status-filter']");
        statusFilter.ShouldNotBeNull();
    }

    [Fact]
    public void ReturnManagement_InitialRender_ShowsLoadReturnsButton()
    {
        var cut = RenderWithMud<ReturnManagement>();

        var loadButton = cut.Find("[data-testid='load-returns-btn']");
        loadButton.ShouldNotBeNull();
    }

    [Fact]
    public void ReturnManagement_WithNoReturns_ShowsNoReturnsAlert()
    {
        var cut = RenderWithMud<ReturnManagement>();

        // Wait for loading to complete
        cut.WaitForState(() => !cut.Markup.Contains("Loading returns"), timeout: TimeSpan.FromSeconds(5));

        var alert = cut.Find("[data-testid='no-returns-alert']");
        alert.ShouldNotBeNull();
        alert.TextContent.ShouldContain("No returns found");
    }

    [Fact]
    public void ReturnManagement_PageTitle_IsCorrect()
    {
        var cut = RenderWithMud<ReturnManagement>();

        cut.Markup.ShouldContain("Return Management");
    }

    [Fact]
    public void ReturnManagement_ShowsRefreshButton_WhenReturnsLoaded()
    {
        var cut = RenderWithMud<ReturnManagement>();

        // Wait for loading to complete
        cut.WaitForState(() => !cut.Markup.Contains("Loading returns"), timeout: TimeSpan.FromSeconds(5));

        var refreshButton = cut.Find("[data-testid='refresh-returns-btn']");
        refreshButton.ShouldNotBeNull();
    }

    [Fact]
    public void ReturnManagement_HasCorrectDataTestIds()
    {
        var cut = RenderWithMud<ReturnManagement>();

        // Verify key data-testid attributes exist for E2E testing
        cut.Find("[data-testid='status-filter']").ShouldNotBeNull();
        cut.Find("[data-testid='load-returns-btn']").ShouldNotBeNull();
    }
}
