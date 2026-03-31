namespace Backoffice.E2ETests.Hooks;

/// <summary>
/// Reqnroll hooks for E2E test lifecycle management.
/// Manages Playwright browser/page lifecycle and E2ETestFixture cleanup between scenarios.
/// </summary>
[Binding]
public sealed class TestHooks
{
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
    private static E2ETestFixture? _fixture;

    /// <summary>
    /// Runs once before all scenarios in the test run.
    /// Initializes Playwright, browser, and E2ETestFixture (3-server infrastructure).
    /// </summary>
    [BeforeTestRun]
    public static async Task BeforeTestRun()
    {
        // Step 1: Initialize Playwright (install browsers if needed)
        _playwright = await Playwright.CreateAsync();

        // Step 2: Launch Chromium browser (headless by default, headed for debugging)
        // Use PLAYWRIGHT_HEADLESS=false for visual debugging
        var headless = !string.Equals(
            Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADLESS"),
            "false",
            StringComparison.OrdinalIgnoreCase);

        _browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = headless,
            SlowMo = headless ? 0 : 100 // 100ms delay when headed for easier visual inspection
        });

        // Step 3: Initialize E2ETestFixture (starts 3 Kestrel servers + PostgreSQL)
        _fixture = new E2ETestFixture();
        await _fixture.InitializeAsync();
    }

    /// <summary>
    /// Runs before each scenario.
    /// Creates a new browser context and page, stores them in ScenarioContext.
    /// Starts Playwright tracing to capture screenshots, DOM snapshots, and network traffic.
    /// </summary>
    [BeforeScenario]
    public static async Task BeforeScenario(ScenarioContext scenarioContext)
    {
        if (_browser == null || _fixture == null)
            throw new InvalidOperationException("Browser or fixture not initialized. BeforeTestRun did not execute.");

        // Step 1: Create new browser context (isolated cookies, local storage, session storage)
        var context = await _browser.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            IgnoreHTTPSErrors = true, // Accept self-signed certs in test environment
            Locale = "en-US"
        });

        // Step 2: Start Playwright tracing for this scenario
        // Captures screenshots, DOM snapshots, and network traffic for failure diagnosis
        await context.Tracing.StartAsync(new()
        {
            Screenshots = true,  // Capture screenshot on every action
            Snapshots = true,    // Capture DOM snapshots before/after each action
            Sources = true       // Include source code in trace (helpful for debugging)
        });

        // Step 3: Create new page
        var page = await context.NewPageAsync();

        // Step 4: Enable console logging to capture JavaScript errors
        page.Console += (_, msg) =>
        {
            var level = msg.Type switch
            {
                "error" => "ERROR",
                "warning" => "WARN",
                _ => "INFO"
            };
            Console.WriteLine($"[Browser Console {level}] {msg.Text}");
        };

        // Step 5: Store page and fixture in ScenarioContext for step definitions
        scenarioContext.Set(page, ScenarioContextKeys.Page);
        scenarioContext.Set(_fixture, ScenarioContextKeys.Fixture);
        scenarioContext.Set(_playwright, ScenarioContextKeys.Playwright);
        scenarioContext.Set(_browser, ScenarioContextKeys.Browser);
        scenarioContext.Set(context, ScenarioContextKeys.Context);
    }

    /// <summary>
    /// Runs after each scenario.
    /// Cleans Marten data, clears all stub clients, closes browser context/page.
    /// Saves Playwright trace on failure for post-mortem debugging.
    /// </summary>
    [AfterScenario]
    public static async Task AfterScenario(ScenarioContext scenarioContext)
    {
        if (_fixture == null)
            return;

        // Step 1: Clean Marten data for test isolation
        await _fixture.CleanMartenDataAsync();

        // Step 2: Clear all stub client data
        _fixture.ClearAllStubs();

        // Step 3: Stop tracing and save on failure
        if (scenarioContext.TryGetValue(ScenarioContextKeys.Context, out IBrowserContext? context))
        {
            if (scenarioContext.ScenarioExecutionStatus == ScenarioExecutionStatus.TestError)
            {
                var scenarioTitle = SanitizeFileName(scenarioContext.ScenarioInfo.Title);
                var tracePath = $"playwright-traces/{scenarioTitle}-{DateTime.UtcNow:yyyyMMddHHmmss}.zip";
                Directory.CreateDirectory("playwright-traces");

                try
                {
                    await context!.Tracing.StopAsync(new() { Path = tracePath });
                    Console.WriteLine($"✅ Playwright trace saved: {tracePath}");
                    Console.WriteLine($"   View with: pwsh bin/Debug/net10.0/playwright.ps1 show-trace {tracePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed to save Playwright trace: {ex.Message}");
                }
            }
            else
            {
                // Success — discard trace to save disk space
                await context!.Tracing.StopAsync();
            }

            await context!.CloseAsync();
        }

        // Step 4: Close page if still open
        if (scenarioContext.TryGetValue(ScenarioContextKeys.Page, out IPage? page))
        {
            await page!.CloseAsync();
        }
    }

    /// <summary>
    /// Runs once after all scenarios in the test run.
    /// Disposes E2ETestFixture, closes browser, and disposes Playwright.
    /// </summary>
    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }

        if (_browser != null)
        {
            await _browser.CloseAsync();
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }

    /// <summary>
    /// Sanitize a scenario title for use as a file name.
    /// Removes characters that are invalid on Windows/Linux file systems and
    /// GitHub Actions artifact uploads (double quotes, colons, angle brackets, etc.).
    /// </summary>
    private static string SanitizeFileName(string title) =>
        title
            .Replace(" ", "_")
            .Replace("/", "-")
            .Replace("\\", "-")
            .Replace("\"", "")
            .Replace(":", "-")
            .Replace("<", "")
            .Replace(">", "")
            .Replace("|", "-")
            .Replace("*", "")
            .Replace("?", "");
}
