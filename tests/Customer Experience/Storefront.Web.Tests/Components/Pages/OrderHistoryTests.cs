using Storefront.Web.Components.Pages;

namespace Storefront.Web.Tests.Components.Pages;

public sealed class OrderHistoryTests : BunitTestBase
{
    [Fact]
    public void OrderHistory_RendersPageTitle()
    {
        var cut = RenderWithMud<OrderHistory>();

        cut.Markup.ShouldContain("Order History");
    }

    [Fact]
    public void OrderHistory_RendersSubtitle()
    {
        var cut = RenderWithMud<OrderHistory>();

        cut.Markup.ShouldContain("Track your past orders and shipment status");
    }

    [Fact]
    public void OrderHistory_RendersEmptyState()
    {
        var cut = RenderWithMud<OrderHistory>();

        cut.Markup.ShouldContain("No orders yet");
        cut.Markup.ShouldContain("Your order history will appear here once you've placed an order.");
    }

    [Fact]
    public void OrderHistory_EmptyState_HasStartShoppingLink()
    {
        var cut = RenderWithMud<OrderHistory>();

        var links = cut.FindAll("a[href]");
        var hrefs = links.Select(l => l.GetAttribute("href")).ToList();
        hrefs.ShouldContain("/products");
    }

    [Fact]
    public void OrderHistory_DoesNotRenderStubOrders()
    {
        var cut = RenderWithMud<OrderHistory>();

        // Stub orders with fake order IDs and dollar amounts must not appear
        cut.Markup.ShouldNotContain("129.99");
        cut.Markup.ShouldNotContain("45.00");
        cut.Markup.ShouldNotContain("89.99");
    }
}
