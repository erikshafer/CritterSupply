namespace ProductCatalog.Shared;

/// <summary>
/// Image URL validation interface.
/// Phase 1: Stub implementation (always returns true).
/// Future: Real validation (URL format, accessibility, CDN checks).
/// </summary>
public interface IImageValidator
{
    Task<bool> IsValidAsync(string url, CancellationToken ct = default);
}

/// <summary>
/// Stub implementation that accepts all image URLs.
/// </summary>
public sealed class StubImageValidator : IImageValidator
{
    public Task<bool> IsValidAsync(string url, CancellationToken ct = default)
    {
        // Phase 1: Accept all URLs
        // Future: Validate URL format, check if accessible, verify CDN path, etc.
        return Task.FromResult(true);
    }
}
