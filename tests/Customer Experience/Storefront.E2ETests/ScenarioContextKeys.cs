namespace Storefront.E2ETests;

/// <summary>
/// Keys for accessing shared state in Reqnroll's ScenarioContext.
/// Using constants prevents typo-based runtime failures.
/// </summary>
internal static class ScenarioContextKeys
{
    public const string Fixture = "E2ETestFixture";
    public const string Playwright = "Playwright";
    public const string Browser = "Browser";
    public const string Page = "Page";
    public const string CartId = "CartId";
    public const string CheckoutId = "CheckoutId";
    public const string OrderId = "OrderId";
    public const string SessionCookie = "SessionCookie";
    public const string SelectedAddressNickname = "SelectedAddressNickname";
    public const string SelectedShippingMethod = "SelectedShippingMethod";
}
