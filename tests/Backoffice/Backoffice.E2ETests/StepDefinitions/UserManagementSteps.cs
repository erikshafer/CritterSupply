using Backoffice.Clients;
using Backoffice.E2ETests.Pages;
using Microsoft.Playwright;
using Reqnroll;
using Shouldly;

namespace Backoffice.E2ETests.StepDefinitions;

[Binding]
public sealed class UserManagementSteps
{
    private readonly ScenarioContext _scenarioContext;

    public UserManagementSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    // --- Given Steps: Stub User Configuration ---

    [Given(@"3 users exist in the system:")]
    [Given(@"(\d+) users exist in the system:")]
    public void GivenUsersExistInTheSystem(int expectedCount, Table table)
    {
        foreach (var row in table.Rows)
        {
            var email = row["Email"];
            var firstName = row["FirstName"];
            var lastName = row["LastName"];
            var role = row["Role"];
            var status = row["Status"];

            var user = new BackofficeUserSummaryDto(
                Id: Guid.NewGuid(),
                Email: email,
                FirstName: firstName,
                LastName: lastName,
                Role: role,
                Status: status,
                CreatedAt: DateTimeOffset.UtcNow.AddMonths(-1),
                LastLoginAt: DateTimeOffset.UtcNow.AddDays(-7),
                DeactivatedAt: status == "Deactivated" ? DateTimeOffset.UtcNow.AddDays(-1) : null);

            Fixture.StubBackofficeIdentityClient.AddUser(user);
        }

        // Verify count matches
        var actualCount = Fixture.StubBackofficeIdentityClient.ListUsersAsync().Result.Count;
        actualCount.ShouldBe(expectedCount);
    }

    [Given(@"user ""(.*)"" exists with name ""(.*)""")]
    public void GivenUserExistsWithName(string email, string fullName)
    {
        var names = fullName.Split(' ', 2);
        var firstName = names[0];
        var lastName = names.Length > 1 ? names[1] : string.Empty;

        var user = new BackofficeUserSummaryDto(
            Id: Guid.NewGuid(),
            Email: email,
            FirstName: firstName,
            LastName: lastName,
            Role: "CustomerService",
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow.AddMonths(-1),
            LastLoginAt: null,
            DeactivatedAt: null);

        Fixture.StubBackofficeIdentityClient.AddUser(user);

        // Store userId in scenario context for later use in edit scenarios
        _scenarioContext.Set(user.Id, $"UserId-{email}");
    }

    [Given(@"user ""(.*)"" exists")]
    public void GivenUserExists(string email)
    {
        var user = new BackofficeUserSummaryDto(
            Id: Guid.NewGuid(),
            Email: email,
            FirstName: "Test",
            LastName: "User",
            Role: "CustomerService",
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow.AddMonths(-1),
            LastLoginAt: null,
            DeactivatedAt: null);

        Fixture.StubBackofficeIdentityClient.AddUser(user);

        // Store userId in scenario context for later use in edit scenarios
        _scenarioContext.Set(user.Id, $"UserId-{email}");
    }

    [Given(@"user ""(.*)"" exists with role ""(.*)""")]
    public void GivenUserExistsWithRole(string email, string role)
    {
        var user = new BackofficeUserSummaryDto(
            Id: Guid.NewGuid(),
            Email: email,
            FirstName: "Test",
            LastName: "User",
            Role: role,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow.AddMonths(-1),
            LastLoginAt: null,
            DeactivatedAt: null);

        Fixture.StubBackofficeIdentityClient.AddUser(user);

        // Store userId in scenario context for later use in edit scenarios
        _scenarioContext.Set(user.Id, $"UserId-{email}");
    }

    [Given(@"user ""(.*)"" exists with status ""(.*)""")]
    public void GivenUserExistsWithStatus(string email, string status)
    {
        var user = new BackofficeUserSummaryDto(
            Id: Guid.NewGuid(),
            Email: email,
            FirstName: "Test",
            LastName: "User",
            Role: "CustomerService",
            Status: status,
            CreatedAt: DateTimeOffset.UtcNow.AddMonths(-1),
            LastLoginAt: null,
            DeactivatedAt: status == "Deactivated" ? DateTimeOffset.UtcNow.AddDays(-1) : null);

        Fixture.StubBackofficeIdentityClient.AddUser(user);

        // Store userId in scenario context for later use in edit scenarios
        _scenarioContext.Set(user.Id, $"UserId-{email}");
    }

    [Given(@"the session will expire")]
    public void GivenTheSessionWillExpire()
    {
        Fixture.StubBackofficeIdentityClient.SimulateSessionExpired = true;
    }

    // --- When Steps: Navigation ---

    [When(@"I navigate to ""(.*)""")]
    public async Task WhenINavigateTo(string url)
    {
        // Handle dynamic userId replacement in URL
        if (url.Contains("{userId}"))
        {
            // Get the userId from scenario context (stored by Given step)
            var userIdEntry = _scenarioContext
                .Where(kv => kv.Key.StartsWith("UserId-"))
                .FirstOrDefault();

            if (userIdEntry.Value is Guid userId)
            {
                url = url.Replace("{userId}", userId.ToString());
            }
        }

        await Page.GotoAsync($"{Fixture.WasmBaseUrl}{url}", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [When(@"I search for ""(.*)""")]
    public async Task WhenISearchFor(string searchTerm)
    {
        var userListPage = new UserListPage(Page, Fixture.WasmBaseUrl);
        await userListPage.SearchForUserAsync(searchTerm);
    }

    // --- When Steps: User List Actions ---

    [When(@"I click the ""Create User"" button")]
    public async Task WhenIClickTheCreateUserButton()
    {
        var createUserButton = Page.GetByTestId("create-user-button");
        await createUserButton.ClickAsync();
        // Wait for navigation
        await Page.WaitForURLAsync(url => url.Contains("/users/create"), new() { Timeout = 10_000 });
    }

    // --- When Steps: User Create Form ---

    [When(@"I fill in ""(.*)"" with ""(.*)""")]
    public async Task WhenIFillInWith(string fieldTestId, string value)
    {
        var field = Page.GetByTestId(fieldTestId);
        await field.FillAsync(value);
    }

    [When(@"I select ""(.*)"" from role dropdown")]
    public async Task WhenISelectFromRoleDropdown(string roleName)
    {
        var userCreatePage = new UserCreatePage(Page, Fixture.WasmBaseUrl);
        await userCreatePage.SelectRoleAsync(roleName);
    }

    [When(@"I click ""(.*)""")]
    public async Task WhenIClick(string buttonTestId)
    {
        var button = Page.GetByTestId(buttonTestId);
        await button.ClickAsync();
        await Page.WaitForTimeoutAsync(1000); // Wait for operation to complete
    }

    // --- Then Steps: User List Assertions ---

    [Then(@"I should see (\d+) users? in the table")]
    public async Task ThenIShouldSeeUsersInTheTable(int expectedCount)
    {
        var userListPage = new UserListPage(Page, Fixture.WasmBaseUrl);
        var actualCount = await userListPage.GetVisibleUserCountAsync();
        actualCount.ShouldBe(expectedCount);
    }

    [Then(@"I should see the ""Create User"" button")]
    public async Task ThenIShouldSeeTheCreateUserButton()
    {
        var userListPage = new UserListPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await userListPage.IsCreateUserButtonVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    [Then(@"I should see ""(.*)""")]
    public async Task ThenIShouldSee(string text)
    {
        // Check if text is visible anywhere on the page
        var locator = Page.Locator($"text={text}");
        var isVisible = await locator.IsVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    [Then(@"I should not see ""(.*)""")]
    public async Task ThenIShouldNotSee(string testId)
    {
        var element = Page.GetByTestId(testId);
        var isVisible = await element.IsVisibleAsync();
        isVisible.ShouldBeFalse();
    }

    // --- Then Steps: Navigation Assertions ---

    [Then(@"I should be redirected to ""(.*)"" within (\d+) seconds?")]
    public async Task ThenIShouldBeRedirectedToWithinSeconds(string expectedUrl, int timeoutSeconds)
    {
        await Page.WaitForURLAsync(
            url => url.Contains(expectedUrl),
            new() { Timeout = timeoutSeconds * 1000 });
    }

    [Then(@"I should be redirected to ""(.*)""")]
    public async Task ThenIShouldBeRedirectedTo(string expectedUrl)
    {
        await Page.WaitForURLAsync(
            url => url.Contains(expectedUrl),
            new() { Timeout = 10_000 });
    }

    [Then(@"I should still be on ""(.*)""")]
    public async Task ThenIShouldStillBeOn(string expectedUrl)
    {
        var currentUrl = Page.Url;
        currentUrl.ShouldContain(expectedUrl);
    }

    // --- Then Steps: Button State Assertions ---

    [Then(@"""(.*)"" should be disabled")]
    public async Task ThenShouldBeDisabled(string buttonTestId)
    {
        var button = Page.GetByTestId(buttonTestId);
        var isDisabled = await button.IsDisabledAsync();
        isDisabled.ShouldBeTrue();
    }

    [Then(@"""(.*)"" should be enabled")]
    public async Task ThenShouldBeEnabled(string buttonTestId)
    {
        var button = Page.GetByTestId(buttonTestId);
        var isDisabled = await button.IsDisabledAsync();
        isDisabled.ShouldBeFalse();
    }

    // --- Then Steps: User Data Assertions ---

    [Then(@"the user's role should be ""(.*)""")]
    public async Task ThenTheUsersRoleShouldBe(string expectedRole)
    {
        // Wait a moment for the UI to update
        await Page.WaitForTimeoutAsync(1000);

        // Verify role changed in stub
        var userIdEntry = _scenarioContext
            .Where(kv => kv.Key.StartsWith("UserId-"))
            .FirstOrDefault();

        if (userIdEntry.Value is not Guid userId)
        {
            throw new InvalidOperationException("UserId not found in scenario context");
        }

        var users = await Fixture.StubBackofficeIdentityClient.ListUsersAsync();
        var user = users.FirstOrDefault(u => u.Id == userId);
        user.ShouldNotBeNull();
        user.Role.ShouldBe(expectedRole);
    }

    [Then(@"the user's status should be ""(.*)""")]
    public async Task ThenTheUsersStatusShouldBe(string expectedStatus)
    {
        // Wait a moment for the UI to update
        await Page.WaitForTimeoutAsync(1000);

        // Verify status changed in stub
        var userIdEntry = _scenarioContext
            .Where(kv => kv.Key.StartsWith("UserId-"))
            .FirstOrDefault();

        if (userIdEntry.Value is not Guid userId)
        {
            throw new InvalidOperationException("UserId not found in scenario context");
        }

        var users = await Fixture.StubBackofficeIdentityClient.ListUsersAsync();
        var user = users.FirstOrDefault(u => u.Id == userId);
        user.ShouldNotBeNull();
        user.Status.ShouldBe(expectedStatus);
    }
}
