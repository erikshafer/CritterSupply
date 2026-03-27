namespace ProductCatalog.Products;

/// <summary>
/// Event-sourced product aggregate. Uses Guid as stream ID (Marten requirement).
/// The existing Product record serves as the document-store model for migration reads.
/// Read queries use the <see cref="ProductCatalogView"/> projection instead.
/// </summary>
public sealed class CatalogProduct
{
    public Guid Id { get; set; }
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string? LongDescription { get; private set; }
    public string Category { get; private set; } = null!;
    public string? Subcategory { get; private set; }
    public string? Brand { get; private set; }
    public IReadOnlyList<ProductImage> Images { get; private set; } = [];
    public IReadOnlyList<string> Tags { get; private set; } = [];
    public ProductDimensions? Dimensions { get; private set; }
    public ProductStatus Status { get; private set; }
    public bool IsDeleted { get; private set; }
    public Guid? VendorTenantId { get; private set; }
    public string? AssignedBy { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public bool IsTerminal => Status == ProductStatus.Discontinued || IsDeleted;

    // Apply methods for event sourcing — Marten calls these to rehydrate state

    public void Apply(ProductMigrated e)
    {
        Sku = e.Sku;
        Name = e.Name;
        Description = e.Description;
        LongDescription = e.LongDescription;
        Category = e.Category;
        Subcategory = e.Subcategory;
        Brand = e.Brand;
        Images = e.Images;
        Tags = e.Tags;
        Dimensions = e.Dimensions;
        Status = e.Status;
        IsDeleted = e.IsDeleted;
        VendorTenantId = e.VendorTenantId;
        AssignedBy = e.AssignedBy;
        AssignedAt = e.AssignedAt;
        AddedAt = e.AddedAt;
    }

    public void Apply(ProductCreated e)
    {
        Sku = e.Sku;
        Name = e.Name;
        Description = e.Description;
        LongDescription = e.LongDescription;
        Category = e.Category;
        Subcategory = e.Subcategory;
        Brand = e.Brand;
        Images = e.Images ?? [];
        Tags = e.Tags ?? [];
        Dimensions = e.Dimensions;
        Status = ProductStatus.Active;
        IsDeleted = false;
        AddedAt = e.CreatedAt ?? DateTimeOffset.UtcNow;
    }

    public void Apply(ProductNameChanged e)
    {
        Name = e.NewName;
        UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductDescriptionChanged e)
    {
        Description = e.NewDescription;
        UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductCategoryChanged e)
    {
        Category = e.NewCategory;
        UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductImagesUpdated e)
    {
        Images = e.NewImages;
        UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductDimensionsChanged e)
    {
        Dimensions = e.NewDimensions;
        UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductStatusChanged e)
    {
        Status = e.NewStatus;
        UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductTagsUpdated e)
    {
        Tags = e.NewTags;
        UpdatedAt = e.ChangedAt;
    }

    public void Apply(ProductSoftDeleted e)
    {
        IsDeleted = true;
        UpdatedAt = e.DeletedAt;
    }

    public void Apply(ProductRestored e)
    {
        IsDeleted = false;
        UpdatedAt = e.RestoredAt;
    }

    public void Apply(ProductVendorAssigned e)
    {
        VendorTenantId = e.VendorTenantId;
        AssignedBy = e.AssignedBy;
        AssignedAt = e.AssignedAt;
        UpdatedAt = e.AssignedAt;
    }
}
