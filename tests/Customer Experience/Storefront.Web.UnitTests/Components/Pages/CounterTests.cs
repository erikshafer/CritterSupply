using Storefront.Web.Components.Pages;

namespace Storefront.Web.Tests.Components.Pages;

public sealed class CounterTests : BunitTestBase
{
    [Fact]
    public void Counter_Renders_InitialCountOfZero()
    {
        var cut = Render<Counter>();

        cut.Find("p[role='status']").TextContent.ShouldContain("Current count: 0");
    }

    [Fact]
    public void Counter_ClickButton_IncrementsCount()
    {
        var cut = Render<Counter>();

        cut.Find("button").Click();

        cut.Find("p[role='status']").TextContent.ShouldContain("Current count: 1");
    }

    [Fact]
    public void Counter_MultipleClicks_IncrementsCorrectly()
    {
        var cut = Render<Counter>();
        var button = cut.Find("button");

        button.Click();
        button.Click();
        button.Click();

        cut.Find("p[role='status']").TextContent.ShouldContain("Current count: 3");
    }

    [Fact]
    public void Counter_HasPageTitle()
    {
        var cut = Render<Counter>();

        cut.Find("h1").TextContent.ShouldBe("Counter");
    }

    [Fact]
    public void Counter_ButtonHasExpectedText()
    {
        var cut = Render<Counter>();

        cut.Find("button").TextContent.ShouldBe("Click me");
    }
}
