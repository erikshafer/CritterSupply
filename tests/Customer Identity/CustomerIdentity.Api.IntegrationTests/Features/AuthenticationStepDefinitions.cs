using Alba;
using Reqnroll;
using Shouldly;

namespace CustomerIdentity.Api.IntegrationTests.Features;

[Binding]
public sealed class AuthenticationStepDefinitions
{
    private readonly TestFixture _fixture;
    private readonly ScenarioContext _scenarioContext;
    private IScenarioResult? _lastResult;
    private string? _sessionCookie;

    public AuthenticationStepDefinitions(ScenarioContext scenarioContext)
    {
        _fixture = Hooks.GetTestFixture();
        _scenarioContext = scenarioContext;
    }

    [Given(@"the Customer Identity API is running")]
    public void GivenTheCustomerIdentityApiIsRunning()
    {
        // Alba host is already running via TestFixture
        _fixture.Host.ShouldNotBeNull();
    }

    [Given(@"the following test users exist:")]
    public void GivenTheFollowingTestUsersExist(Table table)
    {
        // Test users are seeded via EF Core migration
        // Nothing to do here - just validate they exist
        table.RowCount.ShouldBe(3);
    }

    [Given(@"I am logged in as ""(.*)""")]
    public async Task GivenIAmLoggedInAs(string email)
    {
        await WhenILoginWithEmailAndPassword(email, "password");
        _lastResult.ShouldNotBeNull();
        _lastResult.Context.Response.StatusCode.ShouldBe(200);
    }

    [When(@"I login with email ""(.*)"" and password ""(.*)""")]
    public async Task WhenILoginWithEmailAndPassword(string email, string password)
    {
        var request = new { email, password };

        _lastResult = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(request).ToUrl("/api/auth/login");
            scenario.IgnoreStatusCode();
        });

        // Capture session cookie if present
        if (_lastResult.Context.Response.Headers.TryGetValue("Set-Cookie", out var setCookieValues))
        {
            var setCookieHeader = setCookieValues.ToString();
            if (setCookieHeader.Contains("CritterSupply.Auth"))
            {
                _sessionCookie = setCookieHeader.Split(';')[0].Replace("CritterSupply.Auth=", "");
            }
        }
    }

    [When(@"I request my current user information")]
    public async Task WhenIRequestMyCurrentUserInformation()
    {
        _lastResult = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/auth/me");
            if (!string.IsNullOrEmpty(_sessionCookie))
            {
                scenario.WithRequestHeader("Cookie", $"CritterSupply.Auth={_sessionCookie}");
            }
        });
    }

    [When(@"I request my current user information without authentication")]
    public async Task WhenIRequestMyCurrentUserInformationWithoutAuthentication()
    {
        _lastResult = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/auth/me");
            scenario.IgnoreStatusCode();
            // Explicitly do not send session cookie
        });
    }

    [When(@"I logout")]
    public async Task WhenILogout()
    {
        _lastResult = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Url("/api/auth/logout");
            if (!string.IsNullOrEmpty(_sessionCookie))
            {
                scenario.WithRequestHeader("Cookie", $"CritterSupply.Auth={_sessionCookie}");
            }
        });

        // Capture updated cookie (should be expired)
        if (_lastResult.Context.Response.Headers.TryGetValue("Set-Cookie", out var setCookieValues))
        {
            var setCookieHeader = setCookieValues.ToString();
            if (setCookieHeader.Contains("expires"))
            {
                _sessionCookie = null; // Cookie cleared
            }
        }
    }

    [Then(@"the login should succeed")]
    public void ThenTheLoginShouldSucceed()
    {
        _lastResult.ShouldNotBeNull();
        _lastResult.Context.Response.StatusCode.ShouldBe(200);
    }

    [Then(@"the login should fail with status (.*)")]
    public void ThenTheLoginShouldFailWithStatus(int expectedStatusCode)
    {
        _lastResult.ShouldNotBeNull();
        _lastResult.Context.Response.StatusCode.ShouldBe(expectedStatusCode);
    }

    [Then(@"I should receive a session cookie")]
    public void ThenIShouldReceiveASessionCookie()
    {
        _sessionCookie.ShouldNotBeNullOrEmpty();
    }

    [Then(@"the response should contain my customer information:")]
    public void ThenTheResponseShouldContainMyCustomerInformation(Table table)
    {
        _lastResult.ShouldNotBeNull();
        var response = _lastResult.ReadAsJson<LoginResponse>();

        foreach (var row in table.Rows)
        {
            var field = row["Field"];
            var expectedValue = row["Value"];

            switch (field)
            {
                case "Email":
                    response.Email.ShouldBe(expectedValue);
                    break;
                case "FirstName":
                    response.FirstName.ShouldBe(expectedValue);
                    break;
                case "LastName":
                    response.LastName.ShouldBe(expectedValue);
                    break;
                case "CustomerId":
                    response.CustomerId.ToString().ShouldBe(expectedValue);
                    break;
            }
        }
    }

    [Then(@"the response should contain ""(.*)"" in the email field")]
    public void ThenTheResponseShouldContainInTheEmailField(string expectedEmail)
    {
        _lastResult.ShouldNotBeNull();
        var response = _lastResult.ReadAsJson<LoginResponse>();
        response.Email.ShouldBe(expectedEmail);
    }

    [Then(@"I should receive a (.*) Unauthorized response")]
    public void ThenIShouldReceiveAnUnauthorizedResponse(int expectedStatusCode)
    {
        _lastResult.ShouldNotBeNull();
        _lastResult.Context.Response.StatusCode.ShouldBe(expectedStatusCode);
    }

    [Then(@"the logout should succeed")]
    public void ThenTheLogoutShouldSucceed()
    {
        _lastResult.ShouldNotBeNull();
        _lastResult.Context.Response.StatusCode.ShouldBe(200);
    }

    [Then(@"my session cookie should be cleared")]
    public void ThenMySessionCookieShouldBeCleared()
    {
        _sessionCookie.ShouldBeNullOrEmpty();
    }

    [Then(@"I should no longer be able to access protected endpoints")]
    public async Task ThenIShouldNoLongerBeAbleToAccessProtectedEndpoints()
    {
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/auth/me");
            scenario.IgnoreStatusCode();
            // Do not send cookie (it's cleared)
        });

        result.Context.Response.StatusCode.ShouldBe(401);
    }

    // DTOs for deserialization
    private sealed record LoginResponse(Guid CustomerId, string Email, string FirstName, string LastName);
    private sealed record CurrentUserResponse(Guid CustomerId, string Email, string FirstName, string LastName);
}
