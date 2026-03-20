using Backoffice.E2ETests.Pages;
using Microsoft.Playwright;
using Reqnroll;
using Shouldly;

namespace Backoffice.E2ETests.StepDefinitions;

[Binding]
public sealed class PricingAdminSteps
{
    private readonly ScenarioContext _scenarioContext;

    public PricingAdminSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    [Given(@"stub pricing client has product ""(.*)"" with current price ""(.*)""")]
    public void GivenStubPricingClientHasProductWithCurrentPrice(string sku, string price)
    {
        var decimalPrice = decimal.Parse(price.Replace("$", ""));
        Fixture.StubPricingClient.SetCurrentPrice(sku, decimalPrice);
    }

    [Given(@"stub pricing client has floor price ""(.*)"" for SKU ""(.*)""")]
    public void GivenStubPricingClientHasFloorPrice(string price, string sku)
    {
        var decimalPrice = decimal.Parse(price.Replace("$", ""));
        Fixture.StubPricingClient.SetFloorPrice(sku, decimalPrice);
    }

    [Given(@"stub pricing client has ceiling price ""(.*)"" for SKU ""(.*)""")]
    public void GivenStubPricingClientHasCeilingPrice(string price, string sku)
    {
        var decimalPrice = decimal.Parse(price.Replace("$", ""));
        Fixture.StubPricingClient.SetCeilingPrice(sku, decimalPrice);
    }

    [When(@"I navigate to the price edit page for SKU ""(.*)""")]
    public async Task WhenINavigateToPriceEditPage(string sku)
    {
        var priceEditPage = new PriceEditPage(Page, Fixture.WasmBaseUrl);
        await priceEditPage.NavigateAsync(sku);
    }

    [When(@"I set the price to ""(.*)""")]
    public async Task WhenISetThePrice(string price)
    {
        var priceEditPage = new PriceEditPage(Page, Fixture.WasmBaseUrl);
        await priceEditPage.SetPriceAsync(price);
    }

    [When(@"I submit the price change")]
    public async Task WhenISubmitThePriceChange()
    {
        var priceEditPage = new PriceEditPage(Page, Fixture.WasmBaseUrl);
        await priceEditPage.SubmitPriceAsync();
    }

    [Then(@"I should see the current price ""(.*)""")]
    public async Task ThenIShouldSeeTheCurrentPrice(string expectedPrice)
    {
        var priceEditPage = new PriceEditPage(Page, Fixture.WasmBaseUrl);
        var actualPrice = await priceEditPage.GetCurrentPriceAsync();
        actualPrice.ShouldContain(expectedPrice);
    }

    [Then(@"the current price should be ""(.*)""")]
    public async Task ThenTheCurrentPriceShouldBe(string expectedPrice)
    {
        // Allow UI update after submission
        await Page.WaitForTimeoutAsync(500);
        var priceEditPage = new PriceEditPage(Page, Fixture.WasmBaseUrl);
        var actualPrice = await priceEditPage.GetCurrentPriceAsync();
        actualPrice.ShouldContain(expectedPrice);
    }

    [Then(@"the submit button should be disabled")]
    public async Task ThenTheSubmitButtonShouldBeDisabled()
    {
        var priceEditPage = new PriceEditPage(Page, Fixture.WasmBaseUrl);
        var isDisabled = await priceEditPage.IsSubmitButtonDisabledAsync();
        isDisabled.ShouldBeTrue();
    }

    [Then(@"I should see the success message ""(.*)""")]
    public async Task ThenIShouldSeeTheSuccessMessage(string expectedMessage)
    {
        // Wait for success message to appear
        await Page.WaitForTimeoutAsync(1000);
        var priceEditPage = new PriceEditPage(Page, Fixture.WasmBaseUrl);
        var actualMessage = await priceEditPage.GetSuccessMessageAsync();
        actualMessage.ShouldContain(expectedMessage);
    }

    [Then(@"I should see the error message ""(.*)""")]
    public async Task ThenIShouldSeeTheErrorMessage(string expectedMessage)
    {
        // Wait for error message to appear
        await Page.WaitForTimeoutAsync(1000);
        var priceEditPage = new PriceEditPage(Page, Fixture.WasmBaseUrl);
        var actualMessage = await priceEditPage.GetErrorMessageAsync();
        actualMessage.ShouldContain(expectedMessage);
    }

    [Then(@"I should see the price edit form")]
    public async Task ThenIShouldSeeThePriceEditForm()
    {
        var priceEditPage = new PriceEditPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await priceEditPage.IsPriceEditFormVisibleAsync();
        isVisible.ShouldBeTrue();
    }
}
