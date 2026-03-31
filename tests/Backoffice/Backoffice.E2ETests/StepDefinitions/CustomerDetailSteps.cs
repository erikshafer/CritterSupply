using Backoffice.E2ETests.Pages;

namespace Backoffice.E2ETests.StepDefinitions;

/// <summary>
/// Step definitions for the Customer Detail page scenarios (M35.0 Session 2).
/// These steps use the corrected data-testid locators matching the actual
/// CustomerSearch.razor and CustomerDetail.razor pages.
/// </summary>
[Binding]
public sealed class CustomerDetailSteps
{
    private readonly ScenarioContext _scenarioContext;

    public CustomerDetailSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    /// <summary>
    /// Retrieves the customer ID stored by the "customer {name} exists with email {email}" step.
    /// Throws a descriptive error if the Given step was not called for this email.
    /// </summary>
    private Guid GetCustomerIdForEmail(string email)
    {
        var key = $"CustomerId_{email}";
        if (!_scenarioContext.ContainsKey(key))
            throw new InvalidOperationException(
                $"No customer ID found for email '{email}'. " +
                $"Ensure the Given step 'customer \"...\" exists with email \"{email}\"' was called first.");

        return (Guid)_scenarioContext[key];
    }

    // ─── Navigation Steps ──────────────────────────────────────────────

    [When(@"I navigate to the customer search page")]
    public async Task WhenINavigateToTheCustomerSearchPage()
    {
        var searchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        await searchPage.NavigateToSearchPageAsync();
    }

    [When(@"I perform a customer search for ""(.*)""")]
    public async Task WhenIPerformACustomerSearchFor(string query)
    {
        var searchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        await searchPage.PerformSearchAsync(query);
    }

    [When(@"I click view details for customer ""(.*)""")]
    public async Task WhenIClickViewDetailsForCustomer(string email)
    {
        var customerId = GetCustomerIdForEmail(email);
        var searchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        await searchPage.ClickViewDetailsAsync(customerId);
    }

    [When(@"I click back to customer search")]
    public async Task WhenIClickBackToCustomerSearch()
    {
        var detailPage = new CustomerDetailPage(Page, Fixture.WasmBaseUrl);
        await detailPage.ClickBackToSearchAsync();
    }

    [When(@"I click on the first order in the detail order history")]
    public async Task WhenIClickOnTheFirstOrderInTheDetailOrderHistory()
    {
        var detailPage = new CustomerDetailPage(Page, Fixture.WasmBaseUrl);
        await detailPage.ClickFirstOrderAsync();
    }

    [When(@"I navigate to customer detail for a non-existent customer")]
    public async Task WhenINavigateToCustomerDetailForANonExistentCustomer()
    {
        var fakeId = Guid.NewGuid();
        var detailPage = new CustomerDetailPage(Page, Fixture.WasmBaseUrl);
        await detailPage.NavigateAsync(fakeId);
    }

    // ─── Given Steps (customer address seeding) ────────────────────────

    [Given(@"customer ""(.*)"" has address ""(.*)"" as default")]
    public void GivenCustomerHasAddressAsDefault(string email, string nickname)
    {
        var customerId = GetCustomerIdForEmail(email);
        Fixture.StubCustomerIdentityClient.AddAddress(customerId, Guid.NewGuid(), nickname, isDefault: true);
    }

    [Given(@"customer ""(.*)"" has address ""(.*)""")]
    public void GivenCustomerHasAddress(string email, string nickname)
    {
        var customerId = GetCustomerIdForEmail(email);
        Fixture.StubCustomerIdentityClient.AddAddress(customerId, Guid.NewGuid(), nickname, isDefault: false);
    }

    // ─── Assertion Steps — Search Page ─────────────────────────────────

    [Then(@"the search results should contain ""(.*)""")]
    public async Task ThenTheSearchResultsShouldContain(string expectedName)
    {
        var searchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        var found = await searchPage.HasSearchResultForNameAsync(expectedName);
        found.ShouldBeTrue($"Expected search results to contain '{expectedName}'");
    }

    [Then(@"I should see no customer search results")]
    public async Task ThenIShouldSeeNoCustomerSearchResults()
    {
        var searchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        var noResults = await searchPage.IsNoSearchResultsFoundAsync();
        noResults.ShouldBeTrue("Expected 'no search results' message to be visible");
    }

    [Then(@"I should be on the customer search page")]
    public void ThenIShouldBeOnTheCustomerSearchPage()
    {
        var searchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        searchPage.IsOnCustomerSearchPage().ShouldBeTrue("Expected URL to contain /customers/search");
    }

    // ─── Assertion Steps — Detail Page ─────────────────────────────────

    [Then(@"I should be on the customer detail page")]
    public async Task ThenIShouldBeOnTheCustomerDetailPage()
    {
        var detailPage = new CustomerDetailPage(Page, Fixture.WasmBaseUrl);
        detailPage.IsOnCustomerDetailPage().ShouldBeTrue("Expected URL to contain /customers/{guid}");
        (await detailPage.IsPageHeadingVisibleAsync()).ShouldBeTrue("Expected page heading to be visible");
    }

    [Then(@"I should see first name ""(.*)""")]
    public async Task ThenIShouldSeeFirstName(string expectedFirstName)
    {
        var detailPage = new CustomerDetailPage(Page, Fixture.WasmBaseUrl);
        var actualName = await detailPage.GetFirstNameAsync();
        actualName.ShouldBe(expectedFirstName);
    }

    [Then(@"I should see last name ""(.*)""")]
    public async Task ThenIShouldSeeLastName(string expectedLastName)
    {
        var detailPage = new CustomerDetailPage(Page, Fixture.WasmBaseUrl);
        var actualName = await detailPage.GetLastNameAsync();
        actualName.ShouldBe(expectedLastName);
    }

    [Then(@"I should see detail email ""(.*)""")]
    public async Task ThenIShouldSeeDetailEmail(string expectedEmail)
    {
        var detailPage = new CustomerDetailPage(Page, Fixture.WasmBaseUrl);
        var actualEmail = await detailPage.GetEmailAsync();
        actualEmail.ShouldBe(expectedEmail);
    }

    [Then(@"I should see (\d+) orders in the detail order history")]
    public async Task ThenIShouldSeeOrdersInTheDetailOrderHistory(int expectedCount)
    {
        var detailPage = new CustomerDetailPage(Page, Fixture.WasmBaseUrl);
        var actualCount = await detailPage.GetOrderCountAsync();
        actualCount.ShouldBe(expectedCount);
    }

    [Then(@"I should see the no orders message on the detail page")]
    public async Task ThenIShouldSeeTheNoOrdersMessageOnTheDetailPage()
    {
        var detailPage = new CustomerDetailPage(Page, Fixture.WasmBaseUrl);
        var visible = await detailPage.IsNoOrdersMessageVisibleAsync();
        visible.ShouldBeTrue("Expected 'no orders' message to be visible");
    }

    [Then(@"I should see (\d+) addresses in the detail addresses table")]
    public async Task ThenIShouldSeeAddressesInTheDetailAddressesTable(int expectedCount)
    {
        var detailPage = new CustomerDetailPage(Page, Fixture.WasmBaseUrl);
        var actualCount = await detailPage.GetAddressCountAsync();
        actualCount.ShouldBe(expectedCount);
    }

    [Then(@"I should be on an order detail page")]
    public void ThenIShouldBeOnAnOrderDetailPage()
    {
        Page.Url.ShouldContain("/orders/");
        Page.Url.ShouldNotContain("/orders/search");
    }

    [Then(@"I should see the customer not found alert")]
    public async Task ThenIShouldSeeTheCustomerNotFoundAlert()
    {
        var detailPage = new CustomerDetailPage(Page, Fixture.WasmBaseUrl);
        var visible = await detailPage.IsNotFoundVisibleAsync();
        visible.ShouldBeTrue("Expected 'customer not found' alert to be visible");
    }
}
