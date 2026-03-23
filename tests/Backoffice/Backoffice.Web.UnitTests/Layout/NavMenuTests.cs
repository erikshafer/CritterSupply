using System.Security.Claims;
using Backoffice.Web.Layout;
using Bunit.TestDoubles;

namespace Backoffice.Web.Tests.Layout;

/// <summary>
/// bUnit smoke tests for NavMenu.
/// Verifies that new navigation items are present and correctly linked.
/// </summary>
public sealed class NavMenuTests : BunitTestBase
{
    private BunitAuthorizationContext SetupCustomerServiceRole()
    {
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("test@backoffice.test");
        authContext.SetClaims(
            new Claim(ClaimTypes.Role, "customer-service"),
            new Claim(ClaimTypes.Email, "test@backoffice.test"),
            new Claim(ClaimTypes.Name, "Test User")
        );
        authContext.SetPolicies("CustomerService");
        return authContext;
    }

    [Fact]
    public void NavMenu_CustomerServiceRole_ShowsOrderSearchLink()
    {
        SetupCustomerServiceRole();

        var cut = RenderWithMud<NavMenu>();

        // Wait for authorization to resolve
        cut.WaitForState(() => cut.Markup.Contains("Order Search"), timeout: TimeSpan.FromSeconds(5));

        var orderSearchLink = cut.Find("a[href='/orders/search']");
        orderSearchLink.ShouldNotBeNull();
        orderSearchLink.TextContent.Trim().ShouldContain("Order Search");
    }

    [Fact]
    public void NavMenu_CustomerServiceRole_ShowsReturnManagementLink()
    {
        SetupCustomerServiceRole();

        var cut = RenderWithMud<NavMenu>();

        // Wait for authorization to resolve
        cut.WaitForState(() => cut.Markup.Contains("Return Management"), timeout: TimeSpan.FromSeconds(5));

        var returnManagementLink = cut.Find("a[href='/returns']");
        returnManagementLink.ShouldNotBeNull();
        returnManagementLink.TextContent.Trim().ShouldContain("Return Management");
    }
}
