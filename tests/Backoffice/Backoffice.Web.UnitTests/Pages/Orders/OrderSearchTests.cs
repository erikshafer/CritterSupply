using System.Security.Claims;
using Backoffice.Web.Auth;
using Backoffice.Web.Hub;
using Backoffice.Web.Pages.Orders;
using Bunit.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MudBlazor;

namespace Backoffice.Web.Tests.Pages.Orders;

/// <summary>
/// bUnit smoke tests for OrderSearch page.
/// These tests verify markup structure and basic rendering.
/// Full integration testing with HTTP calls happens in integration tests.
/// </summary>
public sealed class OrderSearchTests : BunitTestBase
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ISnackbar> _snackbarMock;
    private readonly Mock<ILogger<OrderSearch>> _loggerMock;
    private readonly Mock<ILogger<BackofficeHubService>> _hubServiceLoggerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly BackofficeAuthState _authState;
    private readonly BackofficeHubService _hubService;
    private readonly SessionExpiredService _sessionExpiredService;

    public OrderSearchTests()
    {
        // Setup bUnit authorization
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("test@backoffice.test");
        authContext.SetClaims(
            new Claim(ClaimTypes.Role, "customer-service"),
            new Claim(ClaimTypes.Email, "test@backoffice.test"),
            new Claim(ClaimTypes.Name, "Test User")
        );
        authContext.SetPolicies("CustomerService");

        // Mock HTTP client factory
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _snackbarMock = new Mock<ISnackbar>();
        _loggerMock = new Mock<ILogger<OrderSearch>>();
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
    }

    [Fact]
    public void OrderSearch_InitialRender_ShowsSearchInput()
    {
        var cut = RenderWithMud<OrderSearch>();

        var searchInput = cut.Find("[data-testid='order-search-input']");
        searchInput.ShouldNotBeNull();
    }

    [Fact]
    public void OrderSearch_InitialRender_ShowsSearchButton()
    {
        var cut = RenderWithMud<OrderSearch>();

        var searchButton = cut.Find("[data-testid='search-order-btn']");
        searchButton.ShouldNotBeNull();
        searchButton.TextContent.ShouldContain("Search");
    }

    [Fact]
    public void OrderSearch_WithEmptyQuery_DoesNotShowResults()
    {
        var cut = RenderWithMud<OrderSearch>();

        var tables = cut.FindAll("[data-testid='order-results-table']");
        tables.Count.ShouldBe(0);
    }

    [Fact]
    public void OrderSearch_PageTitle_IsCorrect()
    {
        var cut = RenderWithMud<OrderSearch>();

        cut.Markup.ShouldContain("Order Search");
    }

    [Fact]
    public void OrderSearch_HasCorrectDataTestIds()
    {
        var cut = RenderWithMud<OrderSearch>();

        // Verify key data-testid attributes exist for E2E testing
        cut.Find("[data-testid='order-search-input']").ShouldNotBeNull();
        cut.Find("[data-testid='search-order-btn']").ShouldNotBeNull();
    }
}
