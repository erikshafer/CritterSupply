using Storefront.E2ETests.Pages;

namespace Storefront.E2ETests.Features;

/// <summary>
/// Reqnroll step definitions for the storefront protected-route E2E scenarios.
///
/// These scenarios verify that ASP.NET Core's cookie auth middleware correctly challenges
/// unauthenticated requests to @attribute [Authorize] pages and redirects to /login.
///
/// No Background setup (no login, no cart seeding) — each scenario starts with a fresh,
/// unauthenticated browser context by design.
/// </summary>
[Binding]
public sealed class ProtectedRoutesStepDefinitions
{
    private readonly ScenarioContext _scenarioContext;
    private readonly E2ETestFixture _fixture;
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    private LoginPage LoginPage => new(Page);

    public ProtectedRoutesStepDefinitions(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        _fixture = scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Given — Setup
    // ─────────────────────────────────────────────────────────────────────

    [Given(@"I am not logged in")]
    public void GivenIAmNotLoggedIn()
    {
        // Fresh browser context per scenario means no session cookie — no action needed.
        // This step exists for Gherkin readability (documents the unauthenticated precondition).
    }

    [Given(@"I have no active cart in localStorage")]
    public async Task GivenIHaveNoActiveCartInLocalStorage()
    {
        // Clear cart and checkout IDs from localStorage so Checkout.razor redirects to /cart.
        // authHelper.getCheckoutId reads 'checkoutId'; authHelper.getCartId reads 'cartId'.
        await Page.EvaluateAsync("() => { localStorage.removeItem('cartId'); localStorage.removeItem('checkoutId'); }");
    }

    // ─────────────────────────────────────────────────────────────────────
    // When — Actions
    // ─────────────────────────────────────────────────────────────────────

    [When(@"I navigate directly to ""(.*)""")]
    public async Task WhenINavigateDirectlyTo(string path)
    {
        // Navigate to the URL and wait for any redirects to settle
        await Page.GotoAsync(path);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Then — Assertions
    // ─────────────────────────────────────────────────────────────────────

    [Then(@"I should be redirected to ""(.*)""")]
    public async Task ThenIShouldBeRedirectedTo(string expectedPath)
    {
        // Wait for URL to settle on the expected path (handles async Blazor navigation)
        await Page.WaitForURLAsync(
            url => url.Contains(expectedPath),
            new PageWaitForURLOptions { Timeout = 10_000 });

        Page.Url.ShouldContain(
            expectedPath,
            customMessage: $"Expected redirect to '{expectedPath}' but current URL is '{Page.Url}'");
    }
}
