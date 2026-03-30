using Marten;
using Marketplaces.CategoryMappings;
using System.Net;
using System.Net.Http.Json;

namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Integration tests for CategoryMapping CRUD operations and seed data.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class CategoryMappingTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CategoryMappingTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _fixture.CleanAllDocumentsAsync();
    }

    // -------------------------------------------------------------------------
    // Set (Upsert)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetCategoryMapping_ValidRequest_Returns201()
    {
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new
            {
                ChannelCode = "TEST_CH",
                InternalCategory = "TestCategory",
                MarketplaceCategoryId = "TEST-CAT-001",
                MarketplaceCategoryPath = "Pet > Test"
            }).ToUrl("/api/category-mappings");
            s.StatusCodeShouldBe(201);
        });

        var body = result.ReadAsJson<System.Text.Json.JsonElement>();
        body.GetProperty("id").GetString().ShouldBe("TEST_CH:TestCategory");
        body.GetProperty("channelCode").GetString().ShouldBe("TEST_CH");
        body.GetProperty("internalCategory").GetString().ShouldBe("TestCategory");
        body.GetProperty("marketplaceCategoryId").GetString().ShouldBe("TEST-CAT-001");
    }

    [Fact]
    public async Task SetCategoryMapping_ExistingMapping_Returns200_WithUpdatedValues()
    {
        // Create initial
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new
            {
                ChannelCode = "UPSERT_CH",
                InternalCategory = "Dogs",
                MarketplaceCategoryId = "OLD-ID"
            }).ToUrl("/api/category-mappings");
            s.StatusCodeShouldBe(201);
        });

        // Upsert with new values
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new
            {
                ChannelCode = "UPSERT_CH",
                InternalCategory = "Dogs",
                MarketplaceCategoryId = "NEW-ID",
                MarketplaceCategoryPath = "Pet > Dogs"
            }).ToUrl("/api/category-mappings");
            s.StatusCodeShouldBeOk();
        });

        var body = result.ReadAsJson<System.Text.Json.JsonElement>();
        body.GetProperty("marketplaceCategoryId").GetString().ShouldBe("NEW-ID");
        body.GetProperty("marketplaceCategoryPath").GetString().ShouldBe("Pet > Dogs");
    }

    [Fact]
    public async Task SetCategoryMapping_WithoutAuth_Returns401()
    {
        var client = _fixture.Host.Server.CreateClient();
        var response = await client.PostAsJsonAsync("/api/category-mappings", new
        {
            ChannelCode = "NO_AUTH",
            InternalCategory = "Dogs",
            MarketplaceCategoryId = "TEST-001"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // Get
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCategoryMapping_ExistingMapping_Returns200()
    {
        // Create
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new
            {
                ChannelCode = "GET_CH",
                InternalCategory = "Cats",
                MarketplaceCategoryId = "GET-CAT-001"
            }).ToUrl("/api/category-mappings");
            s.StatusCodeShouldBe(201);
        });

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/category-mappings/GET_CH/Cats");
            s.StatusCodeShouldBeOk();
        });

        var body = result.ReadAsJson<System.Text.Json.JsonElement>();
        body.GetProperty("id").GetString().ShouldBe("GET_CH:Cats");
        body.GetProperty("marketplaceCategoryId").GetString().ShouldBe("GET-CAT-001");
    }

    [Fact]
    public async Task GetCategoryMapping_MissingMapping_Returns404()
    {
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/category-mappings/NONEXISTENT/Unicorns");
            s.StatusCodeShouldBe(404);
        });
    }

    // -------------------------------------------------------------------------
    // List
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListCategoryMappings_ByChannelCode_ReturnsFiltered()
    {
        // Create mappings for two channels
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new
            {
                ChannelCode = "FILTER_A",
                InternalCategory = "Dogs",
                MarketplaceCategoryId = "A-DOGS-001"
            }).ToUrl("/api/category-mappings");
            s.StatusCodeShouldBe(201);
        });

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new
            {
                ChannelCode = "FILTER_A",
                InternalCategory = "Cats",
                MarketplaceCategoryId = "A-CATS-001"
            }).ToUrl("/api/category-mappings");
            s.StatusCodeShouldBe(201);
        });

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new
            {
                ChannelCode = "FILTER_B",
                InternalCategory = "Dogs",
                MarketplaceCategoryId = "B-DOGS-001"
            }).ToUrl("/api/category-mappings");
            s.StatusCodeShouldBe(201);
        });

        // Filter by FILTER_A
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/category-mappings?channelCode=FILTER_A");
            s.StatusCodeShouldBeOk();
        });

        var items = result.ReadAsJson<System.Text.Json.JsonElement[]>();
        items.ShouldNotBeNull();
        items.Length.ShouldBe(2);
        items.ShouldAllBe(item => item.GetProperty("channelCode").GetString() == "FILTER_A");
    }

    // -------------------------------------------------------------------------
    // Seed data
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SeedData_EighteenCategoryMappings_ExistOnStartup()
    {
        await using var session = _fixture.GetDocumentSession();

        var allMappings = await session.Query<CategoryMapping>().ToListAsync();

        // 6 categories × 3 channels = 18 mappings
        allMappings.Count.ShouldBeGreaterThanOrEqualTo(18);

        // Verify a sample mapping from each channel
        var amazonDogs = await session.LoadAsync<CategoryMapping>("AMAZON_US:Dogs");
        amazonDogs.ShouldNotBeNull();
        amazonDogs!.MarketplaceCategoryId.ShouldBe("AMZN-PET-DOGS-001");

        var walmartCats = await session.LoadAsync<CategoryMapping>("WALMART_US:Cats");
        walmartCats.ShouldNotBeNull();
        walmartCats!.MarketplaceCategoryId.ShouldBe("WMT-PET-CATS-001");

        var ebayBirds = await session.LoadAsync<CategoryMapping>("EBAY_US:Birds");
        ebayBirds.ShouldNotBeNull();
        ebayBirds!.MarketplaceCategoryId.ShouldBe("EBAY-PET-BIRDS-001");
    }
}
