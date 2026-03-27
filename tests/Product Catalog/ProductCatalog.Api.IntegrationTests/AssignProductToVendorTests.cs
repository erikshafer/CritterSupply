using Marten;
using Messages.Contracts.ProductCatalog;
using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Shouldly;
using Wolverine.Tracking;

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
        var productId = Guid.NewGuid();
        session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
            ProductId: productId,
            Sku: sku,
            Name: "Test Product",
            Description: "A test product description",
            Category: "Dogs",
            CreatedAt: DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();

        if (status != ProductStatus.Active)
        {
            session.Events.Append(productId, new ProductStatusChanged(
                ProductId: productId,
                PreviousStatus: ProductStatus.Active,
                NewStatus: status,
                Reason: null,
                ChangedAt: DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }
    }

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
    public async Task GetVendorAssignment_Returns200_WhenProductHasNoAssignment()
    {
        await SeedProductAsync("UNASSIGNED-001");

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/admin/products/UNASSIGNED-001/vendor-assignment");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<VendorAssignmentResponse>();
        response.ShouldNotBeNull();
        response.Sku.ShouldBe("UNASSIGNED-001");
        response.IsAssigned.ShouldBeFalse();
        response.VendorTenantId.ShouldBeNull();
        response.AssignedBy.ShouldBeNull();
        response.AssignedAt.ShouldBeNull();
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
        response.IsAssigned.ShouldBeTrue();
        response.VendorTenantId.ShouldBe(vendorId);
        response.AssignedBy.ShouldBe("system");
        response.AssignedAt!.Value.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
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
        response.IsAssigned.ShouldBeTrue();
        response.VendorTenantId.ShouldBe(vendorId);
        response.PreviousVendorTenantId.ShouldBeNull(); // first-time assignment

        // Verify the assignment is persisted via the projection
        using var session = _fixture.GetDocumentSession();
        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == "ASSIGN-SUCCESS-001")
            .FirstOrDefaultAsync();
        view!.VendorTenantId.ShouldBe(vendorId);
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

        // Second assignment with same vendor — must return 200 (idempotent)
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new AssignProductToVendor(vendorId))
                .ToUrl("/api/admin/products/IDEMPOTENT-001/vendor-assignment");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<VendorAssignmentResponse>();
        response.ShouldNotBeNull();
        response.IsAssigned.ShouldBeTrue();
        response.VendorTenantId.ShouldBe(vendorId);

        // Verify projection still shows original vendor
        using var session = _fixture.GetDocumentSession();
        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == "IDEMPOTENT-001")
            .FirstOrDefaultAsync();
        view!.VendorTenantId.ShouldBe(vendorId);
    }

    [Fact]
    public async Task AssignProduct_PersistsAssignmentViaProjection()
    {
        await SeedProductAsync("PERSIST-001");
        var vendorId = Guid.NewGuid();

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new AssignProductToVendor(vendorId))
                .ToUrl("/api/admin/products/PERSIST-001/vendor-assignment");
            s.StatusCodeShouldBe(200);
        });

        // Verify assignment was stored on the ProductCatalogView projection
        using var session = _fixture.GetDocumentSession();
        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == "PERSIST-001")
            .FirstOrDefaultAsync();
        view.ShouldNotBeNull();
        view.VendorTenantId.ShouldBe(vendorId);
        view.AssignedBy.ShouldBe("system");
        view.AssignedAt.ShouldNotBeNull();
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

    [Fact]
    public async Task BulkAssign_SetsPreviousVendorTenantId_OnReassignment()
    {
        await SeedProductAsync("REASSIGN-BULK-001");

        var vendorA = Guid.NewGuid();
        var vendorB = Guid.NewGuid();

        // First assign to Vendor A
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new AssignProductToVendor(vendorA))
                .ToUrl("/api/admin/products/REASSIGN-BULK-001/vendor-assignment");
            s.StatusCodeShouldBe(200);
        });

        // Bulk reassign from Vendor A → Vendor B with a note
        var command = new BulkAssignProductsToVendor(
        [
            new BulkAssignmentItem("REASSIGN-BULK-001", vendorB, "Supplier transition")
        ]);

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/admin/products/vendor-assignments/bulk");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<BulkAssignmentResult>();
        response.ShouldNotBeNull();
        response.TotalSucceeded.ShouldBe(1);
        response.Succeeded[0].PreviousVendorTenantId.ShouldBe(vendorA);
        response.Succeeded[0].VendorTenantId.ShouldBe(vendorB);
    }

    [Fact]
    public async Task AssignProduct_IncludesProductName_InResponse()
    {
        await SeedProductAsync("NAMED-001");
        var vendorId = Guid.NewGuid();

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new AssignProductToVendor(vendorId))
                .ToUrl("/api/admin/products/NAMED-001/vendor-assignment");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<VendorAssignmentResponse>();
        response.ShouldNotBeNull();
        response.ProductName.ShouldNotBeNullOrWhiteSpace();
        response.IsAssigned.ShouldBeTrue();
    }

    [Fact]
    public async Task AssignProduct_PropagatesReassignmentNote_InResponse()
    {
        await SeedProductAsync("NOTE-001");
        var vendorId = Guid.NewGuid();
        const string note = "Contract signed 2026-03-10";

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new AssignProductToVendor(vendorId, note))
                .ToUrl("/api/admin/products/NOTE-001/vendor-assignment");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<VendorAssignmentResponse>();
        response.ShouldNotBeNull();
        response.ReassignmentNote.ShouldBe(note);
    }
}
