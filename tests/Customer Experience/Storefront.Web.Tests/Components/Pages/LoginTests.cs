using Microsoft.Extensions.DependencyInjection;
using Storefront.Web.Components.Pages;

namespace Storefront.Web.Tests.Components.Pages;

public sealed class LoginTests : BunitTestBase
{
    [Fact]
    public void Login_RendersSignInHeading()
    {
        var cut = Render<Login>();

        cut.Markup.ShouldContain("Sign In");
    }

    [Fact]
    public void Login_RendersBrandName()
    {
        var cut = Render<Login>();

        cut.Markup.ShouldContain("CritterSupply");
    }

    [Fact]
    public void Login_RendersWelcomeMessage()
    {
        var cut = Render<Login>();

        cut.Markup.ShouldContain("Welcome back! Sign in to access your cart and orders.");
    }

    [Fact]
    public void Login_RendersEmailField()
    {
        var cut = Render<Login>();

        // MudTextField renders an input; look for the label
        cut.Markup.ShouldContain("Email");
    }

    [Fact]
    public void Login_RendersPasswordField()
    {
        var cut = Render<Login>();

        cut.Markup.ShouldContain("Password");
    }

    [Fact]
    public void Login_RendersSignInButton()
    {
        var cut = Render<Login>();

        cut.Markup.ShouldContain("Sign In");
    }

    [Fact]
    public void Login_RendersDemoAccountInfo()
    {
        var cut = Render<Login>();

        cut.Markup.ShouldContain("Demo accounts");
        cut.Markup.ShouldContain("alice@critter.test");
        cut.Markup.ShouldContain("bob@critter.test");
        cut.Markup.ShouldContain("charlie@critter.test");
    }

    [Fact]
    public void Login_RendersDemoPassword()
    {
        var cut = Render<Login>();

        cut.Markup.ShouldContain("password");
    }
}
