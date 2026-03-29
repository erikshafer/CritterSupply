using Backoffice.Clients;
using Backoffice.E2ETests.Pages;
using Microsoft.Playwright;
using Reqnroll;
using Shouldly;

namespace Backoffice.E2ETests.StepDefinitions;

/// <summary>
/// Step definitions for order search and order detail E2E scenarios.
/// Covers order seeding, search, results verification, detail navigation, and back navigation.
/// </summary>
[Binding]
public sealed class OrderSearchSteps
{
    private readonly ScenarioContext _scenarioContext;

    public OrderSearchSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    // ─── Given Steps ───────────────────────────────────────────────────────

    [Given(@"an order exists with ID ""(.*)""")]
    public void GivenAnOrderExistsWithId(string orderIdString)
    {
        var orderId = Guid.Parse(orderIdString);

        Fixture.StubOrdersClient.AddOrder(
            orderId: orderId,
            customerId: WellKnownTestData.Customers.TestCustomer,
            status: "Confirmed",
            placedAt: DateTimeOffset.UtcNow.AddHours(-2),
            totalAmount: WellKnownTestData.Orders.TestOrderTotal,
            new OrderLineItemDto("DOG-BOWL-01", "Ceramic Dog Bowl", 2, 19.99m),
            new OrderLineItemDto("CAT-LASER-01", "Interactive Cat Laser", 1, 29.99m));
    }

    // ─── When Steps — Navigation ───────────────────────────────────────────

    [When(@"I navigate to the order search page")]
    public async Task WhenINavigateToTheOrderSearchPage()
    {
        var searchPage = new OrderSearchPage(Page, Fixture.WasmBaseUrl);
        await searchPage.NavigateAsync();
    }

    [When(@"I search for order ""(.*)""")]
    public async Task WhenISearchForOrder(string query)
    {
        var searchPage = new OrderSearchPage(Page, Fixture.WasmBaseUrl);
        await searchPage.SearchOrderAsync(query);
    }

    [When(@"I click view details for order ""(.*)""")]
    public async Task WhenIClickViewDetailsForOrder(string orderIdString)
    {
        var orderId = Guid.Parse(orderIdString);
        var searchPage = new OrderSearchPage(Page, Fixture.WasmBaseUrl);
        await searchPage.ClickViewOrderAsync(orderId);
    }

    [When(@"I click the back button")]
    public async Task WhenIClickTheBackButton()
    {
        var detailPage = new OrderDetailPage(Page, Fixture.WasmBaseUrl);
        await detailPage.ClickBackAsync();
    }

    // ─── Then Steps — Search Results ───────────────────────────────────────

    [Then(@"I should see (\d+) orders? in the search results")]
    public async Task ThenIShouldSeeOrdersInTheSearchResults(int expectedCount)
    {
        var searchPage = new OrderSearchPage(Page, Fixture.WasmBaseUrl);
        var actualCount = await searchPage.GetResultCountAsync();
        actualCount.ShouldBe(expectedCount);
    }

    [Then(@"I should see a no results message")]
    public async Task ThenIShouldSeeANoResultsMessage()
    {
        var searchPage = new OrderSearchPage(Page, Fixture.WasmBaseUrl);
        var visible = await searchPage.IsNoResultsAlertVisibleAsync();
        visible.ShouldBeTrue("Expected 'no results' alert to be visible");
    }

    // ─── Then Steps — Order Detail ─────────────────────────────────────────

    [Then(@"I should be on the order detail page")]
    public async Task ThenIShouldBeOnTheOrderDetailPage()
    {
        var detailPage = new OrderDetailPage(Page, Fixture.WasmBaseUrl);
        (await detailPage.IsOnOrderDetailPageAsync()).ShouldBeTrue("Expected URL to contain /orders/{guid}");
    }

    [Then(@"I should see the order ID displayed")]
    public async Task ThenIShouldSeeTheOrderIdDisplayed()
    {
        var detailPage = new OrderDetailPage(Page, Fixture.WasmBaseUrl);
        (await detailPage.IsPageHeadingVisibleAsync()).ShouldBeTrue("Expected order ID heading to be visible");
    }

    [Then(@"I should see the order status")]
    public async Task ThenIShouldSeeTheOrderStatus()
    {
        var detailPage = new OrderDetailPage(Page, Fixture.WasmBaseUrl);
        var status = await detailPage.GetOrderStatusAsync();
        status.ShouldNotBeNullOrEmpty("Expected order status to be displayed");
    }

    [Then(@"I should be on the order search page")]
    public async Task ThenIShouldBeOnTheOrderSearchPage()
    {
        var searchPage = new OrderSearchPage(Page, Fixture.WasmBaseUrl);
        searchPage.IsOnOrderSearchPage().ShouldBeTrue("Expected URL to contain /orders/search");
    }
}
