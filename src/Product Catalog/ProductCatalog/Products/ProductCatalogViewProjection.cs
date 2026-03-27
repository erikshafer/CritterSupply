using Marten.Events.Aggregation;

namespace ProductCatalog.Products;

/// <summary>
/// Read model for the event-sourced product catalog.
/// Populated by <see cref="ProductCatalogViewProjection"/> from the CatalogProduct event stream.
/// </summary>
public sealed class ProductCatalogView
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? LongDescription { get; set; }
    public string Category { get; set; } = null!;
    public string? Subcategory { get; set; }
    public string? Brand { get; set; }
    public IReadOnlyList<ProductImage> Images { get; set; } = [];
    public IReadOnlyList<string> Tags { get; set; } = [];
    public ProductDimensions? Dimensions { get; set; }
    public ProductStatus Status { get; set; }
    public bool IsDeleted { get; set; }
    public Guid? VendorTenantId { get; set; }
    public string? AssignedBy { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// SingleStreamProjection that produces a <see cref="ProductCatalogView"/> (read model) from the
/// event-sourced CatalogProduct aggregate stream. Registered as an inline projection so the
/// read model is updated within the same transaction as the event append.
/// </summary>
public sealed class ProductCatalogViewProjection : SingleStreamProjection<ProductCatalogView, Guid>
{
    public ProductCatalogView Create(ProductMigrated e) => new()
    {
        Sku = e.Sku,
        Name = e.Name,
        Description = e.Description,
        LongDescription = e.LongDescription,
        Category = e.Category,
        Subcategory = e.Subcategory,
        Brand = e.Brand,
        Images = e.Images,
        Tags = e.Tags,
        Dimensions = e.Dimensions,
        Status = e.Status,
        IsDeleted = e.IsDeleted,
        VendorTenantId = e.VendorTenantId,
        AssignedBy = e.AssignedBy,
        AssignedAt = e.AssignedAt,
        AddedAt = e.AddedAt
    };

    public ProductCatalogView Create(ProductCreated e) => new()
    {
        Sku = e.Sku,
        Name = e.Name,
        Description = e.Description,
        LongDescription = e.LongDescription,
        Category = e.Category,
        Subcategory = e.Subcategory,
        Brand = e.Brand,
        Images = e.Images ?? [],
        Tags = e.Tags ?? [],
        Dimensions = e.Dimensions,
        Status = ProductStatus.Active,
        IsDeleted = false,
        AddedAt = e.CreatedAt ?? DateTimeOffset.UtcNow
    };

    public void Apply(ProductNameChanged e, ProductCatalogView view)
    {
        view.Name = e.NewName;
        view.UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductDescriptionChanged e, ProductCatalogView view)
    {
        view.Description = e.NewDescription;
        view.UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductCategoryChanged e, ProductCatalogView view)
    {
        view.Category = e.NewCategory;
        view.UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductImagesUpdated e, ProductCatalogView view)
    {
        view.Images = e.NewImages;
        view.UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductDimensionsChanged e, ProductCatalogView view)
    {
        view.Dimensions = e.NewDimensions;
        view.UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductStatusChanged e, ProductCatalogView view)
    {
        view.Status = e.NewStatus;
        view.UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductTagsUpdated e, ProductCatalogView view)
    {
        view.Tags = e.NewTags;
        view.UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductSoftDeleted e, ProductCatalogView view)
    {
        view.IsDeleted = true;
        view.UpdatedAt = e.DeletedAt;
    }

    public void Apply(ProductRestored e, ProductCatalogView view)
    {
        view.IsDeleted = false;
        view.UpdatedAt = e.RestoredAt;
    }
}
