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
    public void OrderHistory_RendersTableHeaders()
    {
        var cut = RenderWithMud<OrderHistory>();

        cut.Markup.ShouldContain("Order ID");
        cut.Markup.ShouldContain("Date");
        cut.Markup.ShouldContain("Status");
        cut.Markup.ShouldContain("Total");
    }

    [Fact]
    public void OrderHistory_RendersThreeHardcodedOrders()
    {
        var cut = RenderWithMud<OrderHistory>();

        // The component has 3 hardcoded orders: Delivered, Shipped, Placed
        cut.Markup.ShouldContain("Delivered");
        cut.Markup.ShouldContain("Shipped");
        cut.Markup.ShouldContain("Placed");
    }

    [Fact]
    public void OrderHistory_RendersOrderTotals()
    {
        var cut = RenderWithMud<OrderHistory>();

        // Hardcoded totals: 129.99, 45.00, 89.99
        // Use numeric portion only — currency symbol varies by locale
        cut.Markup.ShouldContain("129.99");
        cut.Markup.ShouldContain("45.00");
        cut.Markup.ShouldContain("89.99");
    }
}
