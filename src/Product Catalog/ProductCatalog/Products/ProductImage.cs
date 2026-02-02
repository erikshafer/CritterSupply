namespace ProductCatalog.Products;

/// <summary>
/// Product image value object.
/// Represents a single product image with URL, alt text, and display order.
/// </summary>
public sealed record ProductImage
{
    public string Url { get; init; } = null!;
    public string AltText { get; init; } = null!;
    public int SortOrder { get; init; }

    public ProductImage() { }

    public static ProductImage Create(string url, string altText, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Image URL cannot be empty", nameof(url));

        if (string.IsNullOrWhiteSpace(altText))
            throw new ArgumentException("Alt text cannot be empty", nameof(altText));

        if (sortOrder < 0)
            throw new ArgumentException("Sort order cannot be negative", nameof(sortOrder));

        return new ProductImage
        {
            Url = url.Trim(),
            AltText = altText.Trim(),
            SortOrder = sortOrder
        };
    }
}
