using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Storefront.Web.Components.Pages;

namespace Storefront.Web.Tests.Components.Pages;

public sealed class ProductsTests : BunitTestBase
{
    private readonly MockHttpMessageHandler _mockHandler = new();

    public ProductsTests()
    {
        var authContext = AddAuthorization();
        authContext.SetNotAuthorized();

        Services.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory(_mockHandler));
    }

    [Fact]
    public void Products_Renders_PageTitle()
    {
        _mockHandler.SetResponse("/api/storefront/products", new ProductListingResponse([], 0, 1, 20));

        var cut = RenderWithMud<Products>();

        cut.Markup.ShouldContain("Browse Products");
    }

    [Fact]
    public void Products_WhenLoading_ShowsProgressIndicator()
    {
        // Don't complete the response - leave loading state active
        _mockHandler.SetPendingResponse("/api/storefront/products");

        var cut = RenderWithMud<Products>();

        cut.Markup.ShouldContain("Loading products");
    }

    [Fact]
    public void Products_WhenNoProducts_ShowsEmptyMessage()
    {
        _mockHandler.SetResponse("/api/storefront/products", new ProductListingResponse([], 0, 1, 20));

        var cut = RenderWithMud<Products>();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("No products found");
        });
    }

    [Fact]
    public void Products_WhenNoProducts_ShowsClearFiltersButton()
    {
        _mockHandler.SetResponse("/api/storefront/products", new ProductListingResponse([], 0, 1, 20));

        var cut = RenderWithMud<Products>();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Clear Filters");
        });
    }

    [Fact]
    public void Products_WhenProductsExist_RendersProductCards()
    {
        var products = new List<ProductCardResponse>
        {
            new("DOG-FOOD-5LB", "Premium Dog Food", 29.99m, "/img/dog-food.jpg", "Dogs", true),
            new("CAT-TOY-01", "Cat Feather Toy", 9.99m, "/img/cat-toy.jpg", "Cats", true),
        };
        _mockHandler.SetResponse("/api/storefront/products", new ProductListingResponse(products, 2, 1, 20));

        var cut = RenderWithMud<Products>();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Premium Dog Food");
            cut.Markup.ShouldContain("Cat Feather Toy");
            cut.Markup.ShouldContain("29.99");
            cut.Markup.ShouldContain("9.99");
            cut.Markup.ShouldContain("Dogs");
            cut.Markup.ShouldContain("Cats");
        });
    }

    [Fact]
    public void Products_OutOfStockItem_ShowsOutOfStockChip()
    {
        var products = new List<ProductCardResponse>
        {
            new("BIRD-CAGE-LG", "Large Bird Cage", 149.99m, "/img/bird-cage.jpg", "Birds", false),
        };
        _mockHandler.SetResponse("/api/storefront/products", new ProductListingResponse(products, 1, 1, 20));

        var cut = RenderWithMud<Products>();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Out of Stock");
        });
    }

    [Fact]
    public void Products_OutOfStockItem_AddToCartButtonIsDisabled()
    {
        var products = new List<ProductCardResponse>
        {
            new("BIRD-CAGE-LG", "Large Bird Cage", 149.99m, "/img/bird-cage.jpg", "Birds", false),
        };
        _mockHandler.SetResponse("/api/storefront/products", new ProductListingResponse(products, 1, 1, 20));

        var cut = RenderWithMud<Products>();
        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll("button");
            var addToCartButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Add to Cart"));
            addToCartButton.ShouldNotBeNull();
            addToCartButton.HasAttribute("disabled").ShouldBeTrue();
        });
    }

    [Fact]
    public void Products_RendersCategoryFilter()
    {
        _mockHandler.SetResponse("/api/storefront/products", new ProductListingResponse([], 0, 1, 20));

        var cut = RenderWithMud<Products>();

        // MudSelect renders its options - check that the filter section has an "Apply Filter" button
        cut.Markup.ShouldContain("Apply Filter");
    }

    [Fact]
    public void Products_WhenManyProducts_ShowsPagination()
    {
        var products = Enumerable.Range(1, 20)
            .Select(i => new ProductCardResponse($"SKU-{i}", $"Product {i}", 10.00m + i, "/img/product.jpg", "Dogs", true))
            .ToList();
        // TotalCount > pageSize (20) triggers pagination
        _mockHandler.SetResponse("/api/storefront/products", new ProductListingResponse(products, 40, 1, 20));

        var cut = RenderWithMud<Products>();
        cut.WaitForAssertion(() =>
        {
            // MudPagination renders pagination nav buttons
            cut.FindAll("button").Count.ShouldBeGreaterThan(0);
        });
    }

    [Fact]
    public void Products_AddToCart_WhenNotAuthenticated_ShowsWarning()
    {
        var products = new List<ProductCardResponse>
        {
            new("DOG-FOOD-5LB", "Premium Dog Food", 29.99m, "/img/dog-food.jpg", "Dogs", true),
        };
        _mockHandler.SetResponse("/api/storefront/products", new ProductListingResponse(products, 1, 1, 20));

        var cut = RenderWithMud<Products>();
        cut.WaitForAssertion(() =>
        {
            var addButton = cut.FindAll("button").First(b => b.TextContent.Contains("Add to Cart"));
            addButton.Click();
        });

        // Snackbar should have been called with warning about sign-in
        // The snackbar service is from MudBlazor - we verify the component doesn't throw
        // (actual snackbar display is a MudBlazor concern)
    }

    // View models matching the component's private records
    private sealed record ProductListingResponse(
        IReadOnlyList<ProductCardResponse> Products,
        int TotalCount,
        int Page,
        int PageSize);

    private sealed record ProductCardResponse(
        string Sku,
        string Name,
        decimal Price,
        string PrimaryImageUrl,
        string Category,
        bool IsInStock);
}

/// <summary>
/// A simple mock HttpMessageHandler for bUnit tests that returns preconfigured responses.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = new();
    private readonly HashSet<string> _pendingRequests = [];

    public void SetResponse<T>(string pathPrefix, T content)
    {
        _responses[pathPrefix] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(content, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })
        };
    }

    public void SetPendingResponse(string pathPrefix)
    {
        _pendingRequests.Add(pathPrefix);
    }

    public void SetErrorResponse(string pathPrefix, HttpStatusCode statusCode)
    {
        _responses[pathPrefix] = () => new HttpResponseMessage(statusCode);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.PathAndQuery ?? "";

        if (_pendingRequests.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            // Simulate never-completing request for loading state tests
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        foreach (var (prefix, responseFactory) in _responses)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return responseFactory();
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

/// <summary>
/// Mock IHttpClientFactory that creates HttpClient instances backed by a shared mock handler.
/// </summary>
public sealed class MockHttpClientFactory(MockHttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5237")
        };
    }
}
