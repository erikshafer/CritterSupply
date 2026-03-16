using Storefront.Web.Components.Pages;

namespace Storefront.Web.Tests.Components.Pages;

public sealed class HomeTests : BunitTestBase
{
    [Fact]
    public void Home_RendersHeroBanner()
    {
        var cut = Render<Home>();

        var heroTitle = cut.Find(".cs-hero-title");
        heroTitle.TextContent.ShouldContain("Everything Your Pet Needs");
    }

    [Fact]
    public void Home_RendersHeroSubtitle()
    {
        var cut = Render<Home>();

        var heroSubtitle = cut.Find(".cs-hero-subtitle");
        heroSubtitle.TextContent.ShouldContain("Stocked for every season");
    }

    [Fact]
    public void Home_ShopNowButton_LinksToProducts()
    {
        var cut = Render<Home>();

        var shopNowLink = cut.Find(".cs-hero a[href='/products']");
        shopNowLink.ShouldNotBeNull();
    }

    [Fact]
    public void Home_RendersFourTrustItems()
    {
        var cut = Render<Home>();

        var trustItems = cut.FindAll(".cs-trust-item");
        trustItems.Count.ShouldBe(4);
    }

    [Fact]
    public void Home_TrustItems_ContainExpectedLabels()
    {
        var cut = Render<Home>();

        var markup = cut.Markup;
        markup.ShouldContain("Free Shipping");
        markup.ShouldContain("Easy Returns");
        markup.ShouldContain("Secure Checkout");
        markup.ShouldContain("Expert Support");
    }

    [Fact]
    public void Home_RendersFourQuickLinkCards()
    {
        var cut = Render<Home>();

        var markup = cut.Markup;
        markup.ShouldContain("Browse Products");
        markup.ShouldContain("Cart");
        markup.ShouldContain("Checkout");
        markup.ShouldContain("Order History");
    }

    [Fact]
    public void Home_QuickLinks_HaveCorrectHrefs()
    {
        var cut = Render<Home>();

        var links = cut.FindAll("a[href]");
        var hrefs = links.Select(l => l.GetAttribute("href")).ToList();

        hrefs.ShouldContain("/products");
        hrefs.ShouldContain("/cart");
        hrefs.ShouldContain("/checkout");
        hrefs.ShouldContain("/orders");
    }

    [Fact]
    public void Home_RendersDemoModeAlert()
    {
        var cut = Render<Home>();

        cut.Markup.ShouldContain("Demo Mode");
        cut.Markup.ShouldContain("reference architecture");
    }
}
