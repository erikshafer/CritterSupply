namespace Storefront.E2ETests.Hooks;

/// <summary>
/// Reqnroll hooks for Playwright browser and page lifecycle management.
///
/// Lifecycle per scenario:
///   [BeforeScenario] → Launch browser context → Create new page → Store in ScenarioContext
///   [AfterScenario]  → Close page → Close browser context → Save trace on failure
/// </summary>
[Binding]
public sealed class PlaywrightHooks
{
    private readonly ScenarioContext _scenarioContext;
    private IBrowserContext? _browserContext;

    public PlaywrightHooks(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [BeforeScenario(Order = 10)]
    public async Task CreateBrowserContextAndPage()
    {
        var playwright = _scenarioContext.Get<IPlaywright>(ScenarioContextKeys.Playwright);
        var browser = _scenarioContext.Get<IBrowser>(ScenarioContextKeys.Browser);
        var fixture = _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);

        // Create a fresh browser context per scenario for complete session isolation
        _browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = fixture.StorefrontWebBaseUrl,
            // Record video only on failure (controlled via trace)
            RecordVideoDir = null
        });

        // Start tracing for this scenario — captures DOM snapshots + network on failure
        await _browserContext.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = false
        });

        var page = await _browserContext.NewPageAsync();

        // Store in ScenarioContext for access by step definitions and Page Object Models
        _scenarioContext.Set(page, ScenarioContextKeys.Page);
    }

    [AfterScenario(Order = 10)]
    public async Task CloseBrowserContextAndSaveTrace()
    {
        if (_browserContext == null) return;

        var testFailed = _scenarioContext.TestError != null;
        if (testFailed)
        {
            // Save Playwright trace for failed scenarios — upload as CI artifact
            var scenarioTitle = _scenarioContext.ScenarioInfo.Title
                .Replace(" ", "_")
                .Replace("/", "_");
            var traceDir = Path.Combine(
                Directory.GetCurrentDirectory(),
                "playwright-traces",
                $"{scenarioTitle}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");

            Directory.CreateDirectory(Path.GetDirectoryName(traceDir)!);

            await _browserContext.Tracing.StopAsync(new TracingStopOptions
            {
                Path = traceDir
            });
        }
        else
        {
            await _browserContext.Tracing.StopAsync();
        }

        await _browserContext.CloseAsync();
        _browserContext = null;
    }
}
