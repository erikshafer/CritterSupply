namespace ProductCatalog.Products;

/// <summary>
/// Product dimensions value object.
/// Physical measurements used for shipping calculations by Fulfillment BC.
/// </summary>
public sealed record ProductDimensions
{
    public decimal Length { get; init; }  // Inches
    public decimal Width { get; init; }   // Inches
    public decimal Height { get; init; }  // Inches
    public decimal Weight { get; init; }  // Pounds

    public ProductDimensions() { }

    public static ProductDimensions Create(decimal length, decimal width, decimal height, decimal weight)
    {
        if (length <= 0)
            throw new ArgumentException("Length must be greater than zero", nameof(length));

        if (width <= 0)
            throw new ArgumentException("Width must be greater than zero", nameof(width));

        if (height <= 0)
            throw new ArgumentException("Height must be greater than zero", nameof(height));

        if (weight <= 0)
            throw new ArgumentException("Weight must be greater than zero", nameof(weight));

        return new ProductDimensions
        {
            Length = length,
            Width = width,
            Height = height,
            Weight = weight
        };
    }
}
