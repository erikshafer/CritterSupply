using Marten;
using Marketplaces.Marketplaces;
using System.Net;
using System.Net.Http.Json;

namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Integration tests for Marketplace CRUD operations.
/// Verifies Register, Update, Deactivate, Get, List handlers
/// and seed data, authentication enforcement, and idempotency.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class MarketplaceCrudTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public MarketplaceCrudTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Clean documents between test classes to ensure isolation
        await _fixture.CleanAllDocumentsAsync();
    }

    // -------------------------------------------------------------------------
    // Registration
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegisterMarketplace_ValidRequest_Returns201()
    {
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new
            {
                ChannelCode = "TEST_MKT",
                DisplayName = "Test Marketplace",
                ApiCredentialVaultPath = "marketplace/test-mkt"
            }).ToUrl("/api/marketplaces");
            s.StatusCodeShouldBe(201);
        });

        var body = result.ReadAsJson<dynamic>();
        Assert.NotNull(body);
    }

    [Fact]
    public async Task RegisterMarketplace_DuplicateChannelCode_Returns200_WithExistingDocument()
    {
        // First registration — creates the document
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new
            {
                ChannelCode = "DUP_CHANNEL",
                DisplayName = "Original Name"
            }).ToUrl("/api/marketplaces");
            s.StatusCodeShouldBe(201);
        });

        // Second registration — idempotent, should return 200 with existing document
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new
            {
                ChannelCode = "DUP_CHANNEL",
                DisplayName = "Attempted Rename"
            }).ToUrl("/api/marketplaces");
            s.StatusCodeShouldBeOk();
        });

        var body = result.ReadAsJson<System.Text.Json.JsonElement>();
        body.GetProperty("displayName").GetString().ShouldBe("Original Name");
    }

    [Fact]
    public async Task RegisterMarketplace_WithoutAuth_Returns401()
    {
        // Use a raw HttpClient without the test auth header
        var client = _fixture.Host.Server.CreateClient();
        var response = await client.PostAsJsonAsync("/api/marketplaces", new
        {
            ChannelCode = "NO_AUTH",
            DisplayName = "Should Not Register"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // Get
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMarketplace_ExistingChannelCode_Returns200()
    {
        // Seed a marketplace
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new
            {
                ChannelCode = "GET_MKTPLACE",
                DisplayName = "Get Test Marketplace"
            }).ToUrl("/api/marketplaces");
            s.StatusCodeShouldBe(201);
        });

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/marketplaces/GET_MKTPLACE");
            s.StatusCodeShouldBeOk();
        });

        var body = result.ReadAsJson<System.Text.Json.JsonElement>();
        body.GetProperty("id").GetString().ShouldBe("GET_MKTPLACE");
        body.GetProperty("displayName").GetString().ShouldBe("Get Test Marketplace");
    }

    [Fact]
    public async Task GetMarketplace_NonExistentChannelCode_Returns404()
    {
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/marketplaces/DOES_NOT_EXIST");
            s.StatusCodeShouldBe(404);
        });
    }

    // -------------------------------------------------------------------------
    // List
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListMarketplaces_ReturnsAll()
    {
        // Seed two marketplaces
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { ChannelCode = "LIST_MKT1", DisplayName = "List Test 1" })
                .ToUrl("/api/marketplaces");
            s.StatusCodeShouldBe(201);
        });

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { ChannelCode = "LIST_MKT2", DisplayName = "List Test 2" })
                .ToUrl("/api/marketplaces");
            s.StatusCodeShouldBe(201);
        });

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/marketplaces");
            s.StatusCodeShouldBeOk();
        });

        var items = result.ReadAsJson<System.Text.Json.JsonElement[]>();
        items.ShouldNotBeNull();
        items.Length.ShouldBeGreaterThanOrEqualTo(2);
    }

    // -------------------------------------------------------------------------
    // Update
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateMarketplace_ValidRequest_Returns200()
    {
        // Create first
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { ChannelCode = "UPDATE_MKT", DisplayName = "Before Update" })
                .ToUrl("/api/marketplaces");
            s.StatusCodeShouldBe(201);
        });

        // Update it
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(new
            {
                ChannelCode = "UPDATE_MKT",
                DisplayName = "After Update",
                ApiCredentialVaultPath = "marketplace/updated"
            }).ToUrl("/api/marketplaces/UPDATE_MKT");
            s.StatusCodeShouldBeOk();
        });

        var body = result.ReadAsJson<System.Text.Json.JsonElement>();
        body.GetProperty("displayName").GetString().ShouldBe("After Update");
        body.GetProperty("apiCredentialVaultPath").GetString().ShouldBe("marketplace/updated");
    }

    // -------------------------------------------------------------------------
    // Deactivate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeactivateMarketplace_ActiveMarketplace_Returns200_WithIsActiveFalse()
    {
        // Create an active marketplace
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { ChannelCode = "DEACT_MKT", DisplayName = "Deactivate Test" })
                .ToUrl("/api/marketplaces");
            s.StatusCodeShouldBe(201);
        });

        // Deactivate it
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { }).ToUrl("/api/marketplaces/DEACT_MKT/deactivate");
            s.StatusCodeShouldBeOk();
        });

        var body = result.ReadAsJson<System.Text.Json.JsonElement>();
        body.GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task DeactivateMarketplace_AlreadyDeactivated_Returns200_Idempotent()
    {
        // Create and deactivate
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { ChannelCode = "IDEMP_DEACT", DisplayName = "Idempotent Deactivate" })
                .ToUrl("/api/marketplaces");
            s.StatusCodeShouldBe(201);
        });

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { }).ToUrl("/api/marketplaces/IDEMP_DEACT/deactivate");
            s.StatusCodeShouldBeOk();
        });

        // Deactivate again — should still return 200
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { }).ToUrl("/api/marketplaces/IDEMP_DEACT/deactivate");
            s.StatusCodeShouldBeOk();
        });

        var body = result.ReadAsJson<System.Text.Json.JsonElement>();
        body.GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Seed data
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SeedData_ThreeMarketplaces_ExistOnStartup()
    {
        // Seed data runs on startup in Development environment.
        // Query directly via Marten to verify the three canonical marketplaces exist.
        await using var session = _fixture.GetDocumentSession();

        var amazon = await session.LoadAsync<Marketplace>("AMAZON_US");
        var walmart = await session.LoadAsync<Marketplace>("WALMART_US");
        var ebay = await session.LoadAsync<Marketplace>("EBAY_US");

        amazon.ShouldNotBeNull();
        amazon!.DisplayName.ShouldBe("Amazon US");
        amazon.IsActive.ShouldBeTrue();
        amazon.IsOwnWebsite.ShouldBeFalse();

        walmart.ShouldNotBeNull();
        walmart!.DisplayName.ShouldBe("Walmart US");
        walmart.IsActive.ShouldBeTrue();

        ebay.ShouldNotBeNull();
        ebay!.DisplayName.ShouldBe("eBay US");
        ebay.IsActive.ShouldBeTrue();
    }
}
