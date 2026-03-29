using Marten;
using Messages.Contracts.ProductCatalog;
using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Shouldly;
using Wolverine.Tracking;

#pragma warning disable CS0618 // ProductUpdated is intentionally tested as deprecated

namespace ProductCatalog.Api.IntegrationTests;

/// <summary>
/// Tests that verify granular integration messages are emitted by Product Catalog event-sourced handlers.
/// Each test confirms:
///   1. The correct domain event is appended to the Marten event stream
///   2. The corresponding granular integration message is queued for publishing
///   3. ProductUpdated is NOT published (deprecated)
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EventSourcingBehaviorTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public EventSourcingBehaviorTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<Guid> SeedProductAsync(
        string sku,
        string name = "Test Product",
        string description = "A test product description",
        string category = "Dogs",
        string? brand = null)
    {
        var productId = Guid.NewGuid();
        using var session = _fixture.GetDocumentSession();
        session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
            ProductId: productId,
            Sku: sku,
            Name: name,
            Description: description,
            Category: category,
            Brand: brand,
            CreatedAt: DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();
        return productId;
    }

    // ── ChangeProductName Tests ───────────────────────────────────────────

    [Fact]
    public async Task ChangeProductName_EmitsProductNameChangedDomainEvent_And_ProductContentUpdatedIntegration()
    {
        // Arrange
        var productId = await SeedProductAsync("EVT-NAME-001", name: "Original Name");
        var command = new ChangeProductName("EVT-NAME-001", "Updated Name");

        // Act — track outgoing messages
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/EVT-NAME-001/name");
            s.StatusCodeShouldBe(204);
        });

        // Assert — domain event appended
        using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(productId);
        events.ShouldContain(e => e.EventType == typeof(ProductNameChanged));

        // Assert — granular integration message sent
        tracked.Sent.SingleMessage<ProductContentUpdated>().ShouldNotBeNull();
        var integrationMsg = tracked.Sent.SingleMessage<ProductContentUpdated>();
        integrationMsg.Sku.ShouldBe("EVT-NAME-001");
        integrationMsg.Name.ShouldBe("Updated Name");

        // Assert — deprecated ProductUpdated NOT sent
        tracked.Sent.MessagesOf<ProductUpdated>().ShouldBeEmpty();
    }

    // ── ChangeProductDescription Tests ────────────────────────────────────

    [Fact]
    public async Task ChangeProductDescription_EmitsProductDescriptionChangedDomainEvent_And_ProductContentUpdatedIntegration()
    {
        // Arrange
        var productId = await SeedProductAsync("EVT-DESC-001");
        var command = new ChangeProductDescription("EVT-DESC-001", "Brand new description");

        // Act
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/EVT-DESC-001/description");
            s.StatusCodeShouldBe(204);
        });

        // Assert — domain event
        using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(productId);
        events.ShouldContain(e => e.EventType == typeof(ProductDescriptionChanged));

        // Assert — integration message
        var integrationMsg = tracked.Sent.SingleMessage<ProductContentUpdated>();
        integrationMsg.ShouldNotBeNull();
        integrationMsg.Sku.ShouldBe("EVT-DESC-001");
        integrationMsg.Description.ShouldBe("Brand new description");

        // Assert — no deprecated ProductUpdated
        tracked.Sent.MessagesOf<ProductUpdated>().ShouldBeEmpty();
    }

    // ── ChangeProductCategory Tests ───────────────────────────────────────

    [Fact]
    public async Task ChangeProductCategory_EmitsProductCategoryChangedDomainEvent_And_Integration()
    {
        // Arrange
        var productId = await SeedProductAsync("EVT-CAT-001", category: "Dogs");
        var command = new ChangeProductCategory("EVT-CAT-001", "Cats");

        // Act
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/EVT-CAT-001/category");
            s.StatusCodeShouldBe(204);
        });

        // Assert — domain event
        using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(productId);
        events.ShouldContain(e => e.EventType == typeof(ProductCatalog.Products.ProductCategoryChanged));

        // Assert — integration message
        var integrationMsg = tracked.Sent.SingleMessage<Messages.Contracts.ProductCatalog.ProductCategoryChanged>();
        integrationMsg.ShouldNotBeNull();
        integrationMsg.Sku.ShouldBe("EVT-CAT-001");
        integrationMsg.PreviousCategory.ShouldBe("Dogs");
        integrationMsg.NewCategory.ShouldBe("Cats");

        // Assert — no deprecated ProductUpdated
        tracked.Sent.MessagesOf<ProductUpdated>().ShouldBeEmpty();
    }

    // ── ChangeProductStatus → Discontinued with IsRecall Tests ────────────

    [Fact]
    public async Task ChangeProductStatus_ToDiscontinuedWithIsRecall_EmitsProductDiscontinuedWithRecallFlag()
    {
        // Arrange
        await SeedProductAsync("EVT-RECALL-001");
        var command = new ChangeProductStatusCommand("EVT-RECALL-001", ProductStatus.Discontinued, Reason: "Safety recall", IsRecall: true);

        // Act
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/EVT-RECALL-001/status");
            s.StatusCodeShouldBe(204);
        });

        // Assert — ProductDiscontinued with IsRecall=true
        // ProductDiscontinued routes to two exchanges (standard + product-recall),
        // so we use MessagesOf to handle the dual-routing
        var discontinuedMsgs = tracked.Sent.MessagesOf<ProductDiscontinued>().ToList();
        discontinuedMsgs.ShouldNotBeEmpty();
        var discontinuedMsg = discontinuedMsgs.First();
        discontinuedMsg.Sku.ShouldBe("EVT-RECALL-001");
        discontinuedMsg.IsRecall.ShouldBeTrue();
        discontinuedMsg.Reason.ShouldBe("Safety recall");

        // Assert — also emits ProductStatusChanged integration
        var statusMsg = tracked.Sent.SingleMessage<Messages.Contracts.ProductCatalog.ProductStatusChanged>();
        statusMsg.ShouldNotBeNull();
        statusMsg.NewStatus.ShouldBe("Discontinued");
    }

    [Fact]
    public async Task ChangeProductStatus_ToDiscontinuedWithoutRecall_EmitsStatusChangedAndDiscontinuedWithRecallFalse()
    {
        // Arrange
        await SeedProductAsync("EVT-DISC-001");
        var command = new ChangeProductStatusCommand("EVT-DISC-001", ProductStatus.Discontinued);

        // Act
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/EVT-DISC-001/status");
            s.StatusCodeShouldBe(204);
        });

        // Assert — ProductStatusChanged sent
        var statusMsg = tracked.Sent.SingleMessage<Messages.Contracts.ProductCatalog.ProductStatusChanged>();
        statusMsg.ShouldNotBeNull();
        statusMsg.Sku.ShouldBe("EVT-DISC-001");
        statusMsg.NewStatus.ShouldBe("Discontinued");

        // Assert — ProductDiscontinued also sent but IsRecall=false
        // ProductDiscontinued routes to two exchanges, so use MessagesOf
        var discontinuedMsgs = tracked.Sent.MessagesOf<ProductDiscontinued>().ToList();
        discontinuedMsgs.ShouldNotBeEmpty();
        discontinuedMsgs.First().IsRecall.ShouldBeFalse();
    }

    // ── ChangeProductStatus to non-Discontinued ───────────────────────────

    [Fact]
    public async Task ChangeProductStatus_ToComingSoon_EmitsStatusChangedOnly_NoProductDiscontinued()
    {
        // Arrange
        await SeedProductAsync("EVT-STATUS-001");
        var command = new ChangeProductStatusCommand("EVT-STATUS-001", ProductStatus.ComingSoon);

        // Act
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/EVT-STATUS-001/status");
            s.StatusCodeShouldBe(204);
        });

        // Assert — ProductStatusChanged sent
        var statusMsg = tracked.Sent.SingleMessage<Messages.Contracts.ProductCatalog.ProductStatusChanged>();
        statusMsg.ShouldNotBeNull();
        statusMsg.NewStatus.ShouldBe("ComingSoon");

        // Assert — ProductDiscontinued NOT sent (status is not Discontinued)
        tracked.Sent.MessagesOf<ProductDiscontinued>().ShouldBeEmpty();
    }

    // ── CreateProduct Tests ───────────────────────────────────────────────

    [Fact]
    public async Task CreateProduct_EmitsEnrichedProductAdded()
    {
        // Arrange
        var command = new CreateProduct(
            Sku: "EVT-NEW-001",
            Name: "New Event Test Product",
            Description: "Testing enriched ProductAdded emission",
            Category: "Dogs",
            Brand: "TestBrand",
            Dimensions: new ProductDimensionsDto(10, 8, 6, 2));

        // Act
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(command).ToUrl("/api/products");
            s.StatusCodeShouldBe(201);
        });

        // Assert — enriched ProductAdded integration message
        var addedMsg = tracked.Sent.SingleMessage<ProductAdded>();
        addedMsg.ShouldNotBeNull();
        addedMsg.Sku.ShouldBe("EVT-NEW-001");
        addedMsg.Name.ShouldBe("New Event Test Product");
        addedMsg.Category.ShouldBe("Dogs");
        addedMsg.Status.ShouldBe("Active");
        addedMsg.Brand.ShouldBe("TestBrand");
        addedMsg.HasDimensions.ShouldBe(true);
    }

    [Fact]
    public async Task CreateProduct_WithoutDimensions_HasDimensionsFalse()
    {
        // Arrange
        var command = new CreateProduct(
            Sku: "EVT-NEW-002",
            Name: "Simple Product",
            Description: "Testing HasDimensions=false",
            Category: "Cats");

        // Act
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(command).ToUrl("/api/products");
            s.StatusCodeShouldBe(201);
        });

        // Assert
        var addedMsg = tracked.Sent.SingleMessage<ProductAdded>();
        addedMsg.ShouldNotBeNull();
        addedMsg.HasDimensions.ShouldBe(false);
        addedMsg.Brand.ShouldBeNull();
    }

    // ── SoftDeleteProduct Tests ───────────────────────────────────────────

    [Fact]
    public async Task SoftDeleteProduct_EmitsProductDeleted()
    {
        // Arrange
        await SeedProductAsync("EVT-DEL-001");

        // Act
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Delete.Url("/api/products/EVT-DEL-001");
            s.StatusCodeShouldBe(204);
        });

        // Assert — ProductDeleted integration message
        var deletedMsg = tracked.Sent.SingleMessage<ProductDeleted>();
        deletedMsg.ShouldNotBeNull();
        deletedMsg.Sku.ShouldBe("EVT-DEL-001");
    }

    // ── RestoreProduct Tests ──────────────────────────────────────────────

    [Fact]
    public async Task RestoreProduct_EmitsProductRestored()
    {
        // Arrange — seed and soft-delete
        var productId = await SeedProductAsync("EVT-REST-001");
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(productId, new ProductSoftDeleted(productId, DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        // Act
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Url("/api/products/EVT-REST-001/restore");
            s.StatusCodeShouldBe(204);
        });

        // Assert — ProductRestored integration message
        var restoredMsg = tracked.Sent.SingleMessage<Messages.Contracts.ProductCatalog.ProductRestored>();
        restoredMsg.ShouldNotBeNull();
        restoredMsg.Sku.ShouldBe("EVT-REST-001");
    }
}
