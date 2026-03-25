namespace VendorPortal.E2ETests.Hooks;

/// <summary>
/// Reqnroll hooks for Playwright browser and page lifecycle management.
///
/// Lifecycle per scenario:
///   [BeforeScenario] → Create fresh browser context → Create new page → Store in ScenarioContext
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
        var browser = _scenarioContext.Get<IBrowser>(ScenarioContextKeys.Browser);
        var fixture = _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);

        // Create a fresh browser context per scenario for complete session isolation
        _browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = fixture.WasmBaseUrl,
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

        // Capture console messages and page errors for debugging
        page.Console += (_, msg) =>
        {
            var level = msg.Type;
            var text = msg.Text;
            Console.WriteLine($"[Browser Console {level}] {text}");
        };

        page.PageError += (_, error) =>
        {
            Console.WriteLine($"[Browser Error] {error}");
        };

        // Log failed HTTP requests
        page.RequestFailed += (_, request) =>
        {
            Console.WriteLine($"[HTTP Failed] {request.Method} {request.Url} - {request.Failure}");
        };

        // Log HTTP responses for debugging (only log 4xx/5xx)
        page.Response += (_, response) =>
        {
            if (response.Status >= 400)
            {
                Console.WriteLine($"[HTTP {response.Status}] {response.Request.Method} {response.Url}");
            }
        };

        _scenarioContext.Set(page, ScenarioContextKeys.Page);
    }

    [AfterScenario(Order = 10)]
    public async Task CloseBrowserContextAndSaveTrace()
    {
        if (_browserContext == null) return;

        var testFailed = _scenarioContext.TestError != null;
        if (testFailed)
        {
            var scenarioTitle = SanitizeFileName(_scenarioContext.ScenarioInfo.Title);
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

    /// <summary>
    /// Sanitize a scenario title for use as a file name.
    /// Removes characters that are invalid on Windows/Linux file systems and
    /// GitHub Actions artifact uploads (double quotes, colons, angle brackets, etc.).
    /// </summary>
    private static string SanitizeFileName(string title) =>
        title
            .Replace(" ", "_")
            .Replace("/", "_")
            .Replace("\"", "")
            .Replace(":", "-")
            .Replace("<", "")
            .Replace(">", "")
            .Replace("|", "-")
            .Replace("*", "")
            .Replace("?", "");
}