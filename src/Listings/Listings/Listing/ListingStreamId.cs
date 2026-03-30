using System.Security.Cryptography;
using System.Text;

namespace Listings.Listing;

/// <summary>
/// Generates deterministic UUID v5 stream IDs for listings.
/// Uses the URL namespace UUID from RFC 4122 with a "listing:" prefix
/// to produce repeatable stream IDs from {sku}:{channelCode}.
/// Per ADR 0042's catalog: pattern — Listings BC uses listing: namespace.
/// </summary>
public static class ListingStreamId
{
    /// <summary>
    /// RFC 4122 URL namespace UUID.
    /// </summary>
    private static readonly Guid UrlNamespace = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>
    /// Computes a deterministic UUID v5 stream ID for a listing.
    /// </summary>
    /// <param name="sku">The product SKU.</param>
    /// <param name="channelCode">The marketplace channel code.</param>
    /// <returns>A deterministic Guid that is always the same for the same SKU+channel combination.</returns>
    public static Guid Compute(string sku, string channelCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelCode);

        return CreateUuidV5(UrlNamespace, $"listing:{sku}:{channelCode}");
    }

    private static Guid CreateUuidV5(Guid namespaceId, string name)
    {
        byte[] namespaceBytes = namespaceId.ToByteArray();
        // Convert to network byte order (big-endian)
        SwapBytes(namespaceBytes, 0, 3);
        SwapBytes(namespaceBytes, 1, 2);
        SwapBytes(namespaceBytes, 4, 5);
        SwapBytes(namespaceBytes, 6, 7);

        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] data = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(data, 0);
        nameBytes.CopyTo(data, namespaceBytes.Length);

        byte[] hash = SHA1.HashData(data);
        byte[] result = new byte[16];
        Array.Copy(hash, 0, result, 0, 16);

        // Set version (5) and variant (RFC 4122)
        result[6] = (byte)((result[6] & 0x0F) | 0x50);
        result[8] = (byte)((result[8] & 0x3F) | 0x80);

        // Convert back from little-endian for Guid constructor
        SwapBytes(result, 0, 3);
        SwapBytes(result, 1, 2);
        SwapBytes(result, 4, 5);
        SwapBytes(result, 6, 7);

        return new Guid(result);
    }

    private static void SwapBytes(byte[] arr, int a, int b)
    {
        (arr[a], arr[b]) = (arr[b], arr[a]);
    }
}
