using Storefront.Web.Components.Pages;

namespace Storefront.Web.Tests.Components.Pages;

public sealed class NotFoundTests : BunitTestBase
{
    [Fact]
    public void NotFound_RendersPageNotFoundHeading()
    {
        var cut = Render<NotFound>();

        cut.Markup.ShouldContain("Page Not Found");
    }

    [Fact]
    public void NotFound_RendersApologyMessage()
    {
        var cut = Render<NotFound>();

        cut.Markup.ShouldContain("Sorry, we couldn't find the page you're looking for");
    }

    [Fact]
    public void NotFound_HasBackToHomeLink()
    {
        var cut = Render<NotFound>();

        var homeLink = cut.Find("a[href='/']");
        homeLink.ShouldNotBeNull();
        homeLink.TextContent.ShouldContain("Back to Home");
    }

    [Fact]
    public void NotFound_HasBrowseProductsLink()
    {
        var cut = Render<NotFound>();

        var productsLink = cut.Find("a[href='/products']");
        productsLink.ShouldNotBeNull();
        productsLink.TextContent.ShouldContain("Browse Products");
    }
}
