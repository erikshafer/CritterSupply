using Backoffice.E2ETests.Pages;
using Microsoft.Playwright;
using Reqnroll;
using Shouldly;

namespace Backoffice.E2ETests.StepDefinitions;

/// <summary>
/// Shared step definitions for customer service scenarios.
/// Provides Given steps for customer/order seeding and login used by both
/// CustomerService.feature and CustomerDetail.feature.
///
/// Session 3: Removed step definitions for inline-details, order-history,
/// and return-request scenarios that targeted non-existent page elements.
/// When/Then steps now live in CustomerDetailSteps.cs (two-page flow).
/// Classification: test bug fix — aspirational step defs matched aspirational locators.
/// </summary>
[Binding]
public sealed class CustomerServiceSteps
{
    private readonly ScenarioContext _scenarioContext;

    public CustomerServiceSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    [Given(@"I am logged in as a customer service admin")]
    public async Task GivenIAmLoggedInAsACustomerServiceAdmin()
    {
        // Seed admin user
        Fixture.SeedAdminUser(
            WellKnownTestData.AdminUsers.Alice,
            WellKnownTestData.AdminUsers.AliceEmail,
            "Alice Anderson",
            "Password123!");

        // Log in
        var loginPage = new LoginPage(Page, Fixture.WasmBaseUrl);
        await loginPage.NavigateAsync();
        await loginPage.LoginAndWaitForDashboardAsync(WellKnownTestData.AdminUsers.AliceEmail, "Password123!");
    }

    [Given(@"customer ""(.*)"" exists with email ""(.*)""")]
    public void GivenCustomerExistsWithEmail(string name, string email)
    {
        var customerId = Guid.NewGuid();
        Fixture.StubCustomerIdentityClient.AddCustomer(customerId, email, name);
        _scenarioContext[$"CustomerId_{email}"] = customerId;
    }

    [Given(@"customer ""(.*)"" has (\d+) orders")]
    public void GivenCustomerHasOrders(string email, int orderCount)
    {
        var customerId = (Guid)_scenarioContext[$"CustomerId_{email}"];

        for (int i = 0; i < orderCount; i++)
        {
            var orderId = Guid.NewGuid();
            Fixture.StubOrdersClient.AddOrder(
                orderId,
                customerId,
                "Confirmed",
                DateTimeOffset.UtcNow.AddDays(-(i + 1)),
                99.99m);
        }
    }
}
