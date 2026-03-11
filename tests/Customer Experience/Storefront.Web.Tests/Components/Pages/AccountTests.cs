using System.Security.Claims;
using Bunit.TestDoubles;
using Storefront.Web.Components.Pages;

namespace Storefront.Web.Tests.Components.Pages;

public sealed class AccountTests : BunitTestBase
{
    private BunitAuthorizationContext SetupAuthenticatedUser()
    {
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("alice@critter.test");
        authContext.SetClaims(
            new Claim("CustomerId", "a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
            new Claim(ClaimTypes.Email, "alice@critter.test"),
            new Claim(ClaimTypes.GivenName, "Alice"),
            new Claim(ClaimTypes.Surname, "Wonder")
        );
        return authContext;
    }

    [Fact]
    public void Account_WhenAuthenticated_RendersCustomerInfo()
    {
        SetupAuthenticatedUser();

        var cut = Render<Account>();

        cut.Markup.ShouldContain("My Account");
        cut.Markup.ShouldContain("Alice");
        cut.Markup.ShouldContain("Wonder");
        cut.Markup.ShouldContain("alice@critter.test");
        cut.Markup.ShouldContain("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    }

    [Fact]
    public void Account_WhenAuthenticated_RendersNavigationLinks()
    {
        SetupAuthenticatedUser();

        var cut = Render<Account>();

        var links = cut.FindAll("a[href]");
        var hrefs = links.Select(l => l.GetAttribute("href")).ToList();
        hrefs.ShouldContain("/orders");
        hrefs.ShouldContain("/cart");
    }

    [Fact]
    public void Account_WhenNotAuthenticated_ShowsLoadingState()
    {
        var authContext = this.AddAuthorization();
        authContext.SetNotAuthorized();

        var cut = Render<Account>();

        // When not authenticated, _customerInfo is null, so loading spinner is shown
        cut.Markup.ShouldContain("Loading account information");
    }

    [Fact]
    public void Account_RendersAccountInformationSection()
    {
        SetupAuthenticatedUser();

        var cut = Render<Account>();

        cut.Markup.ShouldContain("Account Information");
    }
}
