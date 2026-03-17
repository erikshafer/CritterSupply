using Microsoft.Playwright;
using Reqnroll;

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

        // Step 2: Launch Chromium browser in headless mode
        // Use headless: false for local debugging
        _browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            SlowMo = 50 // 50ms delay between actions for debugging (remove in CI)
        });

        // Step 3: Initialize E2ETestFixture (starts 3 Kestrel servers + PostgreSQL)
        _fixture = new E2ETestFixture();
        await _fixture.InitializeAsync();
    }

    /// <summary>
    /// Runs before each scenario.
    /// Creates a new browser context and page, stores them in ScenarioContext.
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

        // Step 2: Create new page
        var page = await context.NewPageAsync();

        // Step 3: Store page and fixture in ScenarioContext for step definitions
        scenarioContext.Set(page, ScenarioContextKeys.Page);
        scenarioContext.Set(_fixture, ScenarioContextKeys.Fixture);
        scenarioContext.Set(_playwright, ScenarioContextKeys.Playwright);
        scenarioContext.Set(_browser, ScenarioContextKeys.Browser);
    }

    /// <summary>
    /// Runs after each scenario.
    /// Cleans Marten data, clears all stub clients, closes browser context/page.
    /// Captures Playwright trace on failure for debugging.
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

        // Step 3: Close browser context and page
        if (scenarioContext.TryGetValue(ScenarioContextKeys.Page, out IPage? page))
        {
            var context = page.Context;

            // Capture trace on failure for debugging
            if (scenarioContext.ScenarioExecutionStatus == ScenarioExecutionStatus.TestError)
            {
                var tracePath = $"playwright-traces/{scenarioContext.ScenarioInfo.Title.Replace(" ", "_")}-{DateTime.UtcNow:yyyyMMddHHmmss}.zip";
                Directory.CreateDirectory("playwright-traces");

                try
                {
                    await context.Tracing.StopAsync(new() { Path = tracePath });
                    Console.WriteLine($"Playwright trace saved: {tracePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save Playwright trace: {ex.Message}");
                }
            }

            await page.CloseAsync();
            await context.CloseAsync();
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
}
