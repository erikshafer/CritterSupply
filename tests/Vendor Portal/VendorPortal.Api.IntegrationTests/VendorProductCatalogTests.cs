using Messages.Contracts.ProductCatalog;
using Shouldly;
using VendorPortal.VendorProductCatalog;

namespace VendorPortal.Api.IntegrationTests;

/// <summary>
/// Integration tests for the VendorProductCatalog feature:
/// verifies that VendorProductAssociated events are correctly handled and
/// persisted as VendorProductCatalogEntry documents.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class VendorProductCatalogTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public VendorProductCatalogTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task VendorProductAssociated_UpsertsCatalogEntry_OnNewAssignment()
    {
        // Arrange
        var vendorId = Guid.NewGuid();
        var @event = new VendorProductAssociated(
            Sku: "CAT-FOOD-001",
            VendorTenantId: vendorId,
            AssociatedBy: "admin@crittersupply.com",
            AssociatedAt: DateTimeOffset.UtcNow,
            PreviousVendorTenantId: null);

        // Act — invoke the handler directly via Wolverine
        await _fixture.ExecuteMessageAsync(@event);

        // Assert — verify the document was persisted
        using var session = _fixture.GetDocumentSession();
        var entry = await session.LoadAsync<VendorProductCatalogEntry>("CAT-FOOD-001");

        entry.ShouldNotBeNull();
        entry.Id.ShouldBe("CAT-FOOD-001");
        entry.Sku.ShouldBe("CAT-FOOD-001");
        entry.VendorTenantId.ShouldBe(vendorId);
        entry.AssociatedBy.ShouldBe("admin@crittersupply.com");
        entry.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task VendorProductAssociated_UpsertsCatalogEntry_OnReassignment()
    {
        // Arrange — seed an initial assignment
        var originalVendorId = Guid.NewGuid();
        var newVendorId = Guid.NewGuid();
        var sku = "DOG-TREAT-001";

        var initialEvent = new VendorProductAssociated(
            Sku: sku,
            VendorTenantId: originalVendorId,
            AssociatedBy: "admin@crittersupply.com",
            AssociatedAt: DateTimeOffset.UtcNow.AddHours(-1),
            PreviousVendorTenantId: null);

        await _fixture.ExecuteMessageAsync(initialEvent);

        // Verify original assignment exists
        using (var session = _fixture.GetDocumentSession())
        {
            var originalEntry = await session.LoadAsync<VendorProductCatalogEntry>(sku);
            originalEntry.ShouldNotBeNull();
            originalEntry.VendorTenantId.ShouldBe(originalVendorId);
        }

        // Act — reassign the SKU to a different vendor
        var reassignedAt = DateTimeOffset.UtcNow;
        var reassignmentEvent = new VendorProductAssociated(
            Sku: sku,
            VendorTenantId: newVendorId,
            AssociatedBy: "admin@crittersupply.com",
            AssociatedAt: reassignedAt,
            PreviousVendorTenantId: originalVendorId);

        await _fixture.ExecuteMessageAsync(reassignmentEvent);

        // Assert — the entry should now point to the new vendor (upsert)
        using var verifySession = _fixture.GetDocumentSession();
        var updatedEntry = await verifySession.LoadAsync<VendorProductCatalogEntry>(sku);

        updatedEntry.ShouldNotBeNull();
        updatedEntry.VendorTenantId.ShouldBe(newVendorId);
        updatedEntry.AssociatedAt.ShouldBe(reassignedAt);
        updatedEntry.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task VendorProductAssociated_HandlesMultipleSkus_Independently()
    {
        // Arrange
        var vendorAId = Guid.NewGuid();
        var vendorBId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var eventA = new VendorProductAssociated(
            Sku: "BIRD-SEED-001",
            VendorTenantId: vendorAId,
            AssociatedBy: "admin",
            AssociatedAt: now);
        var eventB = new VendorProductAssociated(
            Sku: "FISH-FOOD-001",
            VendorTenantId: vendorBId,
            AssociatedBy: "admin",
            AssociatedAt: now);

        // Act
        await _fixture.ExecuteMessageAsync(eventA);
        await _fixture.ExecuteMessageAsync(eventB);

        // Assert — both entries are distinct and correct
        using var session = _fixture.GetDocumentSession();
        var entryA = await session.LoadAsync<VendorProductCatalogEntry>("BIRD-SEED-001");
        var entryB = await session.LoadAsync<VendorProductCatalogEntry>("FISH-FOOD-001");

        entryA.ShouldNotBeNull();
        entryA.VendorTenantId.ShouldBe(vendorAId);

        entryB.ShouldNotBeNull();
        entryB.VendorTenantId.ShouldBe(vendorBId);
    }
}
