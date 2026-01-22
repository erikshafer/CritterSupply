namespace ProductCatalog.Products;

/// <summary>
/// Product aggregate root (document model).
/// Represents master product data in the catalog.
/// Uses Marten document store (NOT event sourced).
/// </summary>
public sealed record Product
{
    // Marten requires a string/Guid/int/long for Id
    // We expose Sku as the Id and derive the string value from it
    public string Id { get; init; } = null!;
    public Sku Sku { get; init; } = null!;
    public ProductName Name { get; init; } = null!;
    public string Description { get; init; } = null!;
    public string? LongDescription { get; init; }
    public CategoryName Category { get; init; } = null!;
    public string? Subcategory { get; init; }
    public string? Brand { get; init; }
    public IReadOnlyList<ProductImage> Images { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public ProductDimensions? Dimensions { get; init; }
    public ProductStatus Status { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset AddedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    private Product() { }

    public static Product Create(
        string sku,
        string name,
        string description,
        string category,
        IReadOnlyList<ProductImage>? images = null,
        string? longDescription = null,
        string? subcategory = null,
        string? brand = null,
        IReadOnlyList<string>? tags = null,
        ProductDimensions? dimensions = null)
    {
        var skuValue = Sku.From(sku);
        return new Product
        {
            Id = skuValue,  // Implicit conversion to string
            Sku = skuValue,
            Name = ProductName.From(name),
            Description = description,
            LongDescription = longDescription,
            Category = CategoryName.From(category),
            Subcategory = subcategory,
            Brand = brand,
            Images = images ?? [],
            Tags = tags ?? [],
            Dimensions = dimensions,
            Status = ProductStatus.Active,
            IsDeleted = false,
            AddedAt = DateTimeOffset.UtcNow
        };
    }

    public Product Update(
        string? name = null,
        string? description = null,
        string? longDescription = null,
        string? category = null,
        string? subcategory = null,
        string? brand = null,
        IReadOnlyList<ProductImage>? images = null,
        IReadOnlyList<string>? tags = null,
        ProductDimensions? dimensions = null)
    {
        return this with
        {
            Name = name is not null ? ProductName.From(name) : Name,
            Description = description ?? Description,
            LongDescription = longDescription ?? LongDescription,
            Category = category is not null ? CategoryName.From(category) : Category,
            Subcategory = subcategory ?? Subcategory,
            Brand = brand ?? Brand,
            Images = images ?? Images,
            Tags = tags ?? Tags,
            Dimensions = dimensions ?? Dimensions,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public Product ChangeStatus(ProductStatus newStatus)
    {
        return this with
        {
            Status = newStatus,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public Product SoftDelete()
    {
        return this with
        {
            IsDeleted = true,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public bool IsTerminal => Status == ProductStatus.Discontinued || IsDeleted;
}
