using Marten;
using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Shouldly;

namespace ProductCatalog.Api.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class AssignProductToVendorTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public AssignProductToVendorTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task SeedProductAsync(string sku, ProductStatus status = ProductStatus.Active)
    {
        using var session = _fixture.GetDocumentSession();
        var product = Product.Create(sku, "Test Product", "A test product description", "Dogs");
        if (status != ProductStatus.Active)
            product = product.ChangeStatus(status);
        session.Store(product);
        await session.SaveChangesAsync();
    }

    private static AddProduct MakeAddProductCommand(string sku) =>
        new(sku, "Test Product", "A test product description", "Dogs");

    // ── GET vendor assignment ──────────────────────────────────────────────

    [Fact]
    public async Task GetVendorAssignment_Returns404_WhenProductDoesNotExist()
    {
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/admin/products/NONEXISTENT-001/vendor-assignment");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task GetVendorAssignment_Returns404_WhenProductHasNoAssignment()
    {
        await SeedProductAsync("UNASSIGNED-001");

        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/admin/products/UNASSIGNED-001/vendor-assignment");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task GetVendorAssignment_Returns200_AfterAssignment()
    {
        await SeedProductAsync("ASSIGNED-001");

        var vendorId = Guid.NewGuid();

        // Assign the product
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new AssignProductToVendor(vendorId))
                .ToUrl("/api/admin/products/ASSIGNED-001/vendor-assignment");
            s.StatusCodeShouldBe(200);
        });

        // Now GET should return the assignment
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/admin/products/ASSIGNED-001/vendor-assignment");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<VendorAssignmentResponse>();
        response.ShouldNotBeNull();
        response.Sku.ShouldBe("ASSIGNED-001");
        response.VendorTenantId.ShouldBe(vendorId);
        response.AssignedBy.ShouldBe("system");
        response.AssignedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    // ── POST (single assignment) ───────────────────────────────────────────

    [Fact]
    public async Task AssignProduct_Returns404_WhenProductDoesNotExist()
    {
        var command = new AssignProductToVendor(Guid.NewGuid());

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/admin/products/NO-EXIST-001/vendor-assignment");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task AssignProduct_Returns400_WhenProductIsDiscontinued()
    {
        await SeedProductAsync("DISC-001", ProductStatus.Discontinued);

        var command = new AssignProductToVendor(Guid.NewGuid());

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/admin/products/DISC-001/vendor-assignment");
            s.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task AssignProduct_Returns400_WhenVendorTenantIdIsEmpty()
    {
        await SeedProductAsync("VALID-SKU-001");

        var command = new AssignProductToVendor(Guid.Empty);

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/admin/products/VALID-SKU-001/vendor-assignment");
            s.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task AssignProduct_Returns200_OnSuccessfulAssignment()
    {
        await SeedProductAsync("ASSIGN-SUCCESS-001");
        var vendorId = Guid.NewGuid();

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new AssignProductToVendor(vendorId))
                .ToUrl("/api/admin/products/ASSIGN-SUCCESS-001/vendor-assignment");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<VendorAssignmentResponse>();
        response.ShouldNotBeNull();
        response.Sku.ShouldBe("ASSIGN-SUCCESS-001");
        response.VendorTenantId.ShouldBe(vendorId);
    }

    [Fact]
    public async Task AssignProduct_IsIdempotent_WhenSameVendorAssignedAgain()
    {
        await SeedProductAsync("IDEMPOTENT-001");
        var vendorId = Guid.NewGuid();

        // First assignment
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new AssignProductToVendor(vendorId))
                .ToUrl("/api/admin/products/IDEMPOTENT-001/vendor-assignment");
            s.StatusCodeShouldBe(200);
        });

        // Second assignment with same vendor — should still return 200
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new AssignProductToVendor(vendorId))
                .ToUrl("/api/admin/products/IDEMPOTENT-001/vendor-assignment");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<VendorAssignmentResponse>();
        response.ShouldNotBeNull();
        response.VendorTenantId.ShouldBe(vendorId);
    }

    [Fact]
    public async Task AssignProduct_PersistsAssignmentOnProductDocument()
    {
        await SeedProductAsync("PERSIST-001");
        var vendorId = Guid.NewGuid();

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new AssignProductToVendor(vendorId))
                .ToUrl("/api/admin/products/PERSIST-001/vendor-assignment");
            s.StatusCodeShouldBe(200);
        });

        // Verify assignment was stored on the Product document
        using var session = _fixture.GetDocumentSession();
        var product = await session.LoadAsync<Product>("PERSIST-001");
        product.ShouldNotBeNull();
        product.VendorTenantId.ShouldBe(vendorId);
        product.AssignedBy.ShouldBe("system");
        product.AssignedAt.ShouldNotBeNull();
    }

    // ── Bulk assignment ────────────────────────────────────────────────────

    [Fact]
    public async Task BulkAssign_Returns400_WhenListIsEmpty()
    {
        var command = new BulkAssignProductsToVendor([]);

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/admin/products/vendor-assignments/bulk");
            s.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task BulkAssign_Returns400_WhenExceeds100Items()
    {
        var items = Enumerable.Range(1, 101)
            .Select(i => new BulkAssignmentItem($"SKU-{i:D3}", Guid.NewGuid()))
            .ToList()
            .AsReadOnly();

        var command = new BulkAssignProductsToVendor(items);

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/admin/products/vendor-assignments/bulk");
            s.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task BulkAssign_Returns200_WhenAllItemsSucceed()
    {
        await SeedProductAsync("BULK-001");
        await SeedProductAsync("BULK-002");

        var vendorId = Guid.NewGuid();
        var command = new BulkAssignProductsToVendor(
        [
            new BulkAssignmentItem("BULK-001", vendorId),
            new BulkAssignmentItem("BULK-002", vendorId)
        ]);

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/admin/products/vendor-assignments/bulk");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<BulkAssignmentResult>();
        response.ShouldNotBeNull();
        response.TotalRequested.ShouldBe(2);
        response.TotalSucceeded.ShouldBe(2);
        response.TotalFailed.ShouldBe(0);
        response.Succeeded.Count.ShouldBe(2);
        response.Failed.ShouldBeEmpty();
    }

    [Fact]
    public async Task BulkAssign_Returns207_WhenSomeItemsFail()
    {
        await SeedProductAsync("BULK-GOOD-001");
        // BULK-MISSING-001 does not exist

        var vendorId = Guid.NewGuid();
        var command = new BulkAssignProductsToVendor(
        [
            new BulkAssignmentItem("BULK-GOOD-001", vendorId),
            new BulkAssignmentItem("BULK-MISSING-001", vendorId)
        ]);

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/admin/products/vendor-assignments/bulk");
            s.StatusCodeShouldBe(207);
        });

        var response = await result.ReadAsJsonAsync<BulkAssignmentResult>();
        response.ShouldNotBeNull();
        response.TotalRequested.ShouldBe(2);
        response.TotalSucceeded.ShouldBe(1);
        response.TotalFailed.ShouldBe(1);
        response.Succeeded.ShouldHaveSingleItem();
        response.Succeeded[0].Sku.ShouldBe("BULK-GOOD-001");
        response.Failed.ShouldHaveSingleItem();
        response.Failed[0].Sku.ShouldBe("BULK-MISSING-001");
        response.Failed[0].ReasonCode.ShouldBe("ProductNotFound");
    }

    [Fact]
    public async Task BulkAssign_FailsDiscontinuedProducts_With207()
    {
        await SeedProductAsync("BULK-DISC-001", ProductStatus.Discontinued);
        await SeedProductAsync("BULK-ACTIVE-001");

        var vendorId = Guid.NewGuid();
        var command = new BulkAssignProductsToVendor(
        [
            new BulkAssignmentItem("BULK-DISC-001", vendorId),
            new BulkAssignmentItem("BULK-ACTIVE-001", vendorId)
        ]);

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/admin/products/vendor-assignments/bulk");
            s.StatusCodeShouldBe(207);
        });

        var response = await result.ReadAsJsonAsync<BulkAssignmentResult>();
        response.ShouldNotBeNull();
        response.Failed[0].ReasonCode.ShouldBe("ProductDiscontinued");
    }
}
