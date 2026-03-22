using Backoffice.Web.Layout;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Backoffice.Web.Tests.Layout;

/// <summary>
/// bUnit smoke tests for NavMenu.
/// Verifies that new navigation items are present and correctly linked.
/// </summary>
public sealed class NavMenuTests : BunitTestBase
{
    public NavMenuTests()
    {
        Services.AddAuthorizationCore(options =>
        {
            // Register policies matching Backoffice authorization policies
            options.AddPolicy("CustomerService", policy =>
                policy.RequireRole("customer-service", "operations-manager", "system-admin"));
            options.AddPolicy("Executive", policy =>
                policy.RequireRole("executive"));
            options.AddPolicy("OperationsManager", policy =>
                policy.RequireRole("operations-manager", "system-admin"));
            options.AddPolicy("WarehouseClerk", policy =>
                policy.RequireRole("warehouse-clerk"));
            options.AddPolicy("PricingManager", policy =>
                policy.RequireRole("pricing-manager"));
            options.AddPolicy("CopyWriter", policy =>
                policy.RequireRole("copy-writer"));
            options.AddPolicy("SystemAdmin", policy =>
                policy.RequireRole("system-admin"));
        });

        Services.AddSingleton<AuthenticationStateProvider>(
            new MockAuthenticationStateProvider("customer-service"));
    }

    [Fact]
    public void NavMenu_CustomerServiceRole_ShowsOrderSearchLink()
    {
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
        var cut = RenderWithMud<NavMenu>();

        // Wait for authorization to resolve
        cut.WaitForState(() => cut.Markup.Contains("Return Management"), timeout: TimeSpan.FromSeconds(5));

        var returnManagementLink = cut.Find("a[href='/returns']");
        returnManagementLink.ShouldNotBeNull();
        returnManagementLink.TextContent.Trim().ShouldContain("Return Management");
    }
}
