namespace Backoffice.E2ETests;

/// <summary>
/// Type-safe string constants for ScenarioContext keys.
/// Prevents typo-based KeyNotFoundException at runtime.
/// </summary>
internal static class ScenarioContextKeys
{
    public const string Fixture = "E2ETestFixture";
    public const string Playwright = "Playwright";
    public const string Browser = "Browser";
    public const string Page = "Page";
    public const string AdminUserId = "AdminUserId";
    public const string OrderId = "OrderId";
    public const string ReturnId = "ReturnId";
    public const string AlertId = "AlertId";
}
