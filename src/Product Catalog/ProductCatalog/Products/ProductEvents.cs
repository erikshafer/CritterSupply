namespace ProductCatalog.Products;

/// <summary>
/// Emitted when an existing document-store product is migrated to event sourcing.
/// Captures the full product state at migration time.
/// </summary>
public sealed record ProductMigrated(
    Guid ProductId,
    string Sku,
    string Name,
    string Description,
    string? LongDescription,
    string Category,
    string? Subcategory,
    string? Brand,
    IReadOnlyList<ProductImage> Images,
    IReadOnlyList<string> Tags,
    ProductDimensions? Dimensions,
    ProductStatus Status,
    bool IsDeleted,
    Guid? VendorTenantId,
    string? AssignedBy,
    DateTimeOffset? AssignedAt,
    DateTimeOffset AddedAt,
    DateTimeOffset MigratedAt);

/// <summary>
/// Emitted when a brand-new product is created directly via event sourcing.
/// </summary>
public sealed record ProductCreated(
    Guid ProductId,
    string Sku,
    string Name,
    string Description,
    string Category,
    string? LongDescription = null,
    string? Subcategory = null,
    string? Brand = null,
    IReadOnlyList<ProductImage>? Images = null,
    IReadOnlyList<string>? Tags = null,
    ProductDimensions? Dimensions = null,
    DateTimeOffset? CreatedAt = null);

public sealed record ProductNameChanged(
    Guid ProductId,
    string PreviousName,
    string NewName,
    DateTimeOffset ChangedAt);

public sealed record ProductDescriptionChanged(
    Guid ProductId,
    string PreviousDescription,
    string NewDescription,
    DateTimeOffset ChangedAt);

public sealed record ProductCategoryChanged(
    Guid ProductId,
    string PreviousCategory,
    string NewCategory,
    DateTimeOffset ChangedAt);

public sealed record ProductImagesUpdated(
    Guid ProductId,
    IReadOnlyList<ProductImage> PreviousImages,
    IReadOnlyList<ProductImage> NewImages,
    DateTimeOffset ChangedAt);

public sealed record ProductDimensionsChanged(
    Guid ProductId,
    ProductDimensions? PreviousDimensions,
    ProductDimensions? NewDimensions,
    DateTimeOffset ChangedAt);

public sealed record ProductStatusChanged(
    Guid ProductId,
    ProductStatus PreviousStatus,
    ProductStatus NewStatus,
    string? Reason,
    DateTimeOffset ChangedAt);

public sealed record ProductTagsUpdated(
    Guid ProductId,
    IReadOnlyList<string> PreviousTags,
    IReadOnlyList<string> NewTags,
    DateTimeOffset ChangedAt);

public sealed record ProductSoftDeleted(
    Guid ProductId,
    DateTimeOffset DeletedAt);

public sealed record ProductRestored(
    Guid ProductId,
    DateTimeOffset RestoredAt);
