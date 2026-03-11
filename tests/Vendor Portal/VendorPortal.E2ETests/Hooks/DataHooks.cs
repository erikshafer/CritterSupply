namespace VendorPortal.E2ETests.Hooks;

/// <summary>
/// Reqnroll hooks for test data seeding and cleanup.
///
/// Lifecycle per scenario:
///   [BeforeTestRun]  → Start E2E fixture (Playwright + 3 servers + containers) once for entire run
///   [BeforeScenario] → Store fixture in context, reset Marten data
///   [AfterScenario]  → Clean database
///   [AfterTestRun]   → Dispose E2E fixture (stop servers, stop containers)
/// </summary>
[Binding]
public sealed class DataHooks
{
    private static E2ETestFixture _fixture = null!;
    private static IPlaywright _playwright = null!;
    private static IBrowser _browser = null!;

    private readonly ScenarioContext _scenarioContext;

    public DataHooks(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    /// <summary>
    /// Start all infrastructure once for the entire E2E test run.
    /// Starting 3 Kestrel servers + TestContainers takes 10–15 seconds —
    /// we pay that cost once rather than per-scenario.
    /// </summary>
    [BeforeTestRun(Order = 1)]
    public static async Task StartInfrastructure()
    {
        _fixture = new E2ETestFixture();
        await _fixture.InitializeAsync();

        _playwright = await Playwright.CreateAsync();

        // Default to headless=true in all environments.
        // Set PLAYWRIGHT_HEADLESS=false only for local visual debugging.
        var headless = !string.Equals(
            Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADLESS"),
            "false",
            StringComparison.OrdinalIgnoreCase);

        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
            SlowMo = headless ? 0 : 100
        });
    }

    [BeforeScenario(Order = 1)]
    public void StoreFixtureInContext()
    {
        _scenarioContext.Set(_fixture, ScenarioContextKeys.Fixture);
        _scenarioContext.Set(_playwright, ScenarioContextKeys.Playwright);
        _scenarioContext.Set(_browser, ScenarioContextKeys.Browser);
    }

    [AfterScenario(Order = 100)]
    public async Task CleanDatabase()
    {
        // Clean Marten event store + documents between scenarios for isolation.
        // EF Core seed data (tenant + users) is shared and never cleaned.
        await _fixture.CleanMartenDataAsync();
    }

    [AfterTestRun(Order = 100)]
    public static async Task StopInfrastructure()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
        await _fixture.DisposeAsync();
    }
}
