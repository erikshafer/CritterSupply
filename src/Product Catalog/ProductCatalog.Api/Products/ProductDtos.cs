namespace ProductCatalog.Api.Products;

/// <summary>
/// DTO for product image data in API requests.
/// Converted to <see cref="ProductCatalog.Products.ProductImage"/> value object in handlers.
/// </summary>
public sealed record ProductImageDto(string Url, string AltText, int SortOrder = 0);

/// <summary>
/// DTO for product dimensions data in API requests.
/// Converted to <see cref="ProductCatalog.Products.ProductDimensions"/> value object in handlers.
/// </summary>
public sealed record ProductDimensionsDto(decimal Length, decimal Width, decimal Height, decimal Weight);
