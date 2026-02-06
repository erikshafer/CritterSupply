using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Reqnroll;
using Shouldly;

namespace ProductCatalog.Api.IntegrationTests;

[Binding]
public sealed class AddProductSteps
{
    private readonly TestFixture _fixture;
    private readonly ScenarioContext _scenarioContext;

    private AddProduct? _command;
    private IScenarioResult? _result;

    public AddProductSteps(ScenarioContext scenarioContext)
    {
        _fixture = Hooks.GetTestFixture();
        _scenarioContext = scenarioContext;
    }

    [Given(@"the product catalog is empty")]
    public async Task GivenTheProductCatalogIsEmpty()
    {
        await _fixture.CleanAllDocumentsAsync();
    }

    [Given(@"I have a product with SKU ""(.*)""")]
    public void GivenIHaveAProductWithSku(string sku)
    {
        _command = new AddProduct(sku, "Product Name", "Product Description", "Category");
    }

    [Given(@"the product name is ""(.*)""")]
    public void GivenTheProductNameIs(string name)
    {
        _command = _command! with { Name = name };
    }

    [Given(@"the product category is ""(.*)""")]
    public void GivenTheProductCategoryIs(string category)
    {
        _command = _command! with { Category = category };
    }

    [Given(@"the product description is ""(.*)""")]
    public void GivenTheProductDescriptionIs(string description)
    {
        _command = _command! with { Description = description };
    }

    [Given(@"the product has the following images:")]
    public void GivenTheProductHasTheFollowingImages(Table table)
    {
        var images = table.Rows.Select(row => new ProductImageDto(
            row["Url"],
            row["AltText"],
            int.Parse(row["DisplayOrder"])
        )).ToList();

        _command = _command! with { Images = images };
    }

    [Given(@"a product with SKU ""(.*)"" already exists")]
    public async Task GivenAProductWithSkuAlreadyExists(string sku)
    {
        var existingProduct = new AddProduct(
            sku,
            "Existing Product",
            "Description",
            "Dogs");

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(existingProduct).ToUrl("/api/products");
            s.StatusCodeShouldBe(201);
        });
    }

    [When(@"I add the product to the catalog")]
    public async Task WhenIAddTheProductToTheCatalog()
    {
        _result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(_command).ToUrl("/api/products");
            s.IgnoreStatusCode(); // Don't assert status code in Alba - we'll check it in Then steps
        });

        if (_result.Context.Response.StatusCode == 201)
        {
            _scenarioContext["sku"] = _command!.Sku;
        }
    }

    [When(@"I attempt to add another product with SKU ""(.*)""")]
    public async Task WhenIAttemptToAddAnotherProductWithSku(string sku)
    {
        _command = new AddProduct(sku, "Duplicate Product", "Description", "Dogs");

        _result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(_command).ToUrl("/api/products");
            s.IgnoreStatusCode(); // Don't assert status code in Alba - we'll check it in Then steps
        });
    }

    [Then(@"the product should be successfully created")]
    public void ThenTheProductShouldBeSuccessfullyCreated()
    {
        _result.ShouldNotBeNull();
        _result.Context.Response.StatusCode.ShouldBe(201);
    }

    [Then(@"the product should be retrievable by SKU ""(.*)""")]
    public async Task ThenTheProductShouldBeRetrievableBySku(string sku)
    {
        using var session = _fixture.GetDocumentSession();
        var product = await session.LoadAsync<Product>(sku);

        product.ShouldNotBeNull();
        product.Sku.Value.ShouldBe(sku);
    }

    [Then(@"the product status should be ""(.*)""")]
    public async Task ThenTheProductStatusShouldBe(string expectedStatus)
    {
        var sku = _scenarioContext.Get<string>("sku");

        using var session = _fixture.GetDocumentSession();
        var product = await session.LoadAsync<Product>(sku);

        product.ShouldNotBeNull();
        product.Status.ToString().ShouldBe(expectedStatus);
    }

    [Then(@"the product should have (.*) images")]
    public async Task ThenTheProductShouldHaveImages(int expectedCount)
    {
        var sku = _scenarioContext.Get<string>("sku");

        using var session = _fixture.GetDocumentSession();
        var product = await session.LoadAsync<Product>(sku);

        product.ShouldNotBeNull();
        product.Images.Count.ShouldBe(expectedCount);
    }

    [Then(@"the request should fail with status code (.*)")]
    public void ThenTheRequestShouldFailWithStatusCode(int expectedStatusCode)
    {
        _result.ShouldNotBeNull();
        _result.Context.Response.StatusCode.ShouldBe(expectedStatusCode);
    }

    [Then(@"the error message should indicate ""(.*)""")]
    public async Task ThenTheErrorMessageShouldIndicate(string expectedMessage)
    {
        _result.ShouldNotBeNull();
        var responseBody = await _result.ReadAsTextAsync();
        responseBody.ShouldContain(expectedMessage, Case.Insensitive);
    }

    [Then(@"the error message should contain ""(.*)""")]
    public async Task ThenTheErrorMessageShouldContain(string expectedText)
    {
        _result.ShouldNotBeNull();
        var responseBody = await _result.ReadAsTextAsync();
        responseBody.ShouldContain(expectedText, Case.Insensitive);
    }
}
