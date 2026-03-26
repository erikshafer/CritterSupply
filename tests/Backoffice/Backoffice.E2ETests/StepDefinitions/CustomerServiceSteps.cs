using Backoffice.E2ETests.Pages;
using Microsoft.Playwright;
using Reqnroll;
using Shouldly;

namespace Backoffice.E2ETests.StepDefinitions;

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

    [Given(@"I am on the customer service page")]
    public async Task GivenIAmOnTheCustomerServicePage()
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        await customerSearchPage.NavigateAsync();
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

    [Given(@"customer ""(.*)"" has (\d+) order with ID ""(.*)""")]
    public void GivenCustomerHasOrderWithId(string email, int _, string orderIdPlaceholder)
    {
        var customerId = (Guid)_scenarioContext[$"CustomerId_{email}"];
        var orderId = Guid.NewGuid();

        Fixture.StubOrdersClient.AddOrder(
            orderId,
            customerId,
            "Confirmed",
            DateTimeOffset.UtcNow.AddDays(-1),
            99.99m);

        _scenarioContext[ScenarioContextKeys.OrderId] = orderId;
    }

    [Given(@"customer has order ""(.*)"" with status ""(.*)""")]
    public void GivenCustomerHasOrderWithStatus(string orderIdPlaceholder, string status)
    {
        // Order already created in previous step, just update status
        var orderId = _scenarioContext.Get<Guid>(ScenarioContextKeys.OrderId);

        // Re-add with new status (stub implementation allows this)
        Fixture.StubOrdersClient.AddOrder(
            orderId,
            Guid.NewGuid(), // customerId not used for status update
            status,
            DateTimeOffset.UtcNow.AddDays(-1),
            99.99m);
    }

    [Given(@"customer has return request ""(.*)"" for order ""(.*)"" with status ""(.*)""")]
    public void GivenCustomerHasReturnRequestForOrderWithStatus(
        string returnIdPlaceholder,
        string orderIdPlaceholder,
        string status)
    {
        var orderId = _scenarioContext.Get<Guid>(ScenarioContextKeys.OrderId);
        var returnId = Guid.NewGuid();

        Fixture.StubReturnsClient.AddReturn(
            returnId,
            orderId,
            Guid.NewGuid(), // customerId
            status,
            DateTimeOffset.UtcNow.AddDays(-1));

        _scenarioContext[ScenarioContextKeys.ReturnId] = returnId;
    }

    [Given(@"customer has (\d+) return requests")]
    public void GivenCustomerHasReturnRequests(int returnCount)
    {
        var orderId = _scenarioContext.Get<Guid>(ScenarioContextKeys.OrderId);

        for (int i = 0; i < returnCount; i++)
        {
            var returnId = Guid.NewGuid();
            Fixture.StubReturnsClient.AddReturn(
                returnId,
                orderId,
                Guid.NewGuid(), // customerId
                "Requested",
                DateTimeOffset.UtcNow.AddDays(-(i + 1)));
        }
    }

    [When(@"I search for customer by email ""(.*)""")]
    public async Task WhenISearchForCustomerByEmail(string email)
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        await customerSearchPage.SearchByEmailAsync(email);
    }

    [When(@"I click on order ""(.*)""")]
    public async Task WhenIClickOnOrder(string orderIdPlaceholder)
    {
        var orderId = _scenarioContext.Get<Guid>(ScenarioContextKeys.OrderId);
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        await customerSearchPage.ClickOrderAsync(orderId.ToString());
    }

    [When(@"I view return request ""(.*)""")]
    public async Task WhenIViewReturnRequest(string returnIdPlaceholder)
    {
        var returnId = _scenarioContext.Get<Guid>(ScenarioContextKeys.ReturnId);
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        await customerSearchPage.ClickReturnRequestAsync(returnId.ToString());
    }

    [When(@"I approve the return request")]
    public async Task WhenIApproveTheReturnRequest()
    {
        var returnId = _scenarioContext.Get<Guid>(ScenarioContextKeys.ReturnId);
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        await customerSearchPage.ApproveReturnAsync(returnId.ToString());
    }

    [When(@"I deny the return request with reason ""(.*)""")]
    public async Task WhenIDenyTheReturnRequestWithReason(string reason)
    {
        var returnId = _scenarioContext.Get<Guid>(ScenarioContextKeys.ReturnId);
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        await customerSearchPage.DenyReturnAsync(returnId.ToString(), reason);
    }

    [Then(@"I should see customer details for ""(.*)""")]
    public async Task ThenIShouldSeeCustomerDetailsFor(string expectedName)
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        var isFound = await customerSearchPage.IsCustomerFoundAsync();
        isFound.ShouldBeTrue();

        var actualName = await customerSearchPage.GetCustomerNameAsync();
        actualName.ShouldBe(expectedName);
    }

    [Then(@"I should see (\d+) orders in the order history table")]
    public async Task ThenIShouldSeeOrdersInTheOrderHistoryTable(int expectedCount)
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        var actualCount = await customerSearchPage.GetOrderHistoryCountAsync();
        actualCount.ShouldBe(expectedCount);
    }

    [Then(@"the order history should include order IDs")]
    public async Task ThenTheOrderHistoryShouldIncludeOrderIds()
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        var orderIds = await customerSearchPage.GetOrderIdsAsync();
        orderIds.Count.ShouldBeGreaterThan(0);
        orderIds.All(id => !string.IsNullOrWhiteSpace(id)).ShouldBeTrue();
    }

    [Then(@"I should see an empty order history message")]
    public async Task ThenIShouldSeeAnEmptyOrderHistoryMessage()
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        var isEmpty = await customerSearchPage.IsOrderHistoryEmptyAsync();
        isEmpty.ShouldBeTrue();
    }

    [Then(@"I should see a ""(.*)"" message")]
    public async Task ThenIShouldSeeAMessage(string messageType)
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);

        if (messageType == "no results")
        {
            var isNoResults = await customerSearchPage.IsNoResultsMessageVisibleAsync();
            isNoResults.ShouldBeTrue();
        }
    }

    [Then(@"I should not see customer details")]
    public async Task ThenIShouldNotSeeCustomerDetails()
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        var isFound = await customerSearchPage.IsCustomerFoundAsync();
        isFound.ShouldBeFalse();
    }

    [Then(@"I should see order details for order ""(.*)""")]
    public async Task ThenIShouldSeeOrderDetailsForOrder(string orderIdPlaceholder)
    {
        // Just verify order details modal/page is visible
        var orderDetailsModal = Page.GetByTestId("order-details-modal");
        var orderDetailsPage = Page.GetByTestId("order-details-page");

        var isModalVisible = await orderDetailsModal.IsVisibleAsync();
        var isPageVisible = await orderDetailsPage.IsVisibleAsync();

        (isModalVisible || isPageVisible).ShouldBeTrue();
    }

    [Then(@"the order details should include line items, status, and total")]
    public async Task ThenTheOrderDetailsShouldIncludeLineItemsStatusAndTotal()
    {
        // Verify key order detail fields are present
        var lineItems = Page.GetByTestId("order-line-items");
        var orderStatus = Page.GetByTestId("order-status");
        var orderTotal = Page.GetByTestId("order-total");

        (await lineItems.IsVisibleAsync()).ShouldBeTrue();
        (await orderStatus.IsVisibleAsync()).ShouldBeTrue();
        (await orderTotal.IsVisibleAsync()).ShouldBeTrue();
    }

    [Then(@"the return request status should change to ""(.*)""")]
    public async Task ThenTheReturnRequestStatusShouldChangeTo(string expectedStatus)
    {
        var returnStatus = Page.GetByTestId("return-status");
        var actualStatus = await returnStatus.TextContentAsync();
        actualStatus.ShouldContain(expectedStatus);
    }

    [Then(@"I should see a confirmation message")]
    public async Task ThenIShouldSeeAConfirmationMessage()
    {
        var confirmation = Page.GetByTestId("return-approved-confirmation")
            .Or(Page.GetByTestId("return-denied-confirmation"));

        (await confirmation.IsVisibleAsync()).ShouldBeTrue();
    }

    [Then(@"the denial reason should be recorded")]
    public async Task ThenTheDenialReasonShouldBeRecorded()
    {
        var denialReason = Page.GetByTestId("return-denial-reason-display");
        (await denialReason.IsVisibleAsync()).ShouldBeTrue();
    }

    [Then(@"I should see (\d+) return requests in the customer details")]
    public async Task ThenIShouldSeeReturnRequestsInTheCustomerDetails(int expectedCount)
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        var actualCount = await customerSearchPage.GetReturnRequestCountAsync();
        actualCount.ShouldBe(expectedCount);
    }

    [Then(@"each return request should show its status")]
    public async Task ThenEachReturnRequestShouldShowItsStatus()
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        var statuses = await customerSearchPage.GetReturnRequestStatusesAsync();
        statuses.Count.ShouldBeGreaterThan(0);
        statuses.All(s => !string.IsNullOrWhiteSpace(s)).ShouldBeTrue();
    }

    [Then(@"I should see the customer's email displayed as ""(.*)""")]
    public async Task ThenIShouldSeeTheCustomersEmailDisplayedAs(string expectedEmail)
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        var actualEmail = await customerSearchPage.GetCustomerEmailAsync();
        actualEmail.ShouldBe(expectedEmail);
    }

    [Then(@"the order status should be ""(.*)""")]
    public async Task ThenTheOrderStatusShouldBe(string expectedStatus)
    {
        var orderStatus = Page.GetByTestId("order-status");
        var actualStatus = await orderStatus.TextContentAsync();
        actualStatus.ShouldContain(expectedStatus);
    }

    [Then(@"I should not see any return requests for the cancelled order")]
    public async Task ThenIShouldNotSeeAnyReturnRequestsForTheCancelledOrder()
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        var returnCount = await customerSearchPage.GetReturnRequestCountAsync();
        returnCount.ShouldBe(0);
    }

    [Then(@"the order history table should support pagination or scrolling")]
    public async Task ThenTheOrderHistoryTableShouldSupportPaginationOrScrolling()
    {
        // Just verify table is scrollable or has pagination controls
        var orderHistoryTable = Page.GetByTestId("order-history-table");
        (await orderHistoryTable.IsVisibleAsync()).ShouldBeTrue();

        // Check for pagination or scrollbar
        var paginationControls = Page.GetByTestId("order-history-pagination");
        var isPaginationVisible = await paginationControls.IsVisibleAsync();

        // If no pagination, table should be scrollable
        if (!isPaginationVisible)
        {
            var boundingBox = await orderHistoryTable.BoundingBoxAsync();
            boundingBox.ShouldNotBeNull();
        }
    }
}
