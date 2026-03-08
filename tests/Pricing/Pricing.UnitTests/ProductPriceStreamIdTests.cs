using Pricing.Products;

namespace Pricing.UnitTests;

/// <summary>
/// Tests for ProductPrice.StreamId(sku) — UUID v5 determinism.
/// Verifies RFC 4122 compliance, case-insensitive determinism, and consistency.
/// See ADR 0016 for rationale (why UUID v5, not UUID v7 or MD5).
/// </summary>
public sealed class ProductPriceStreamIdTests
{
    [Fact]
    public void StreamId_WithSameSku_ReturnsSameGuid()
    {
        // Arrange
        var sku = "DOG-FOOD-5LB";

        // Act
        var id1 = ProductPrice.StreamId(sku);
        var id2 = ProductPrice.StreamId(sku);

        // Assert
        id1.ShouldBe(id2);
    }

    [Fact]
    public void StreamId_IsCaseInsensitive_ReturnsSameGuidForDifferentCasing()
    {
        // Arrange
        var sku1 = "dog-food-5lb";
        var sku2 = "DOG-FOOD-5LB";
        var sku3 = "Dog-Food-5Lb";

        // Act
        var id1 = ProductPrice.StreamId(sku1);
        var id2 = ProductPrice.StreamId(sku2);
        var id3 = ProductPrice.StreamId(sku3);

        // Assert
        id1.ShouldBe(id2);
        id2.ShouldBe(id3);
    }

    [Fact]
    public void StreamId_WithDifferentSkus_ReturnsDifferentGuids()
    {
        // Arrange
        var sku1 = "DOG-FOOD-5LB";
        var sku2 = "CAT-FOOD-3LB";

        // Act
        var id1 = ProductPrice.StreamId(sku1);
        var id2 = ProductPrice.StreamId(sku2);

        // Assert
        id1.ShouldNotBe(id2);
    }

    [Fact]
    public void StreamId_IsNotUuidV7_SameInputProducesSameOutput()
    {
        // Arrange
        var sku = "DOG-FOOD-5LB";

        // Act
        var ids = Enumerable.Range(0, 100)
            .Select(_ => ProductPrice.StreamId(sku))
            .ToList();

        // Assert
        ids.Distinct().Count().ShouldBe(1, "UUID v5 must be deterministic (not timestamp-random like v7)");
    }

    [Fact]
    public void StreamId_IsRfc4122Compliant_VersionBitsAre5()
    {
        // Arrange
        var sku = "DOG-FOOD-5LB";

        // Act
        var id = ProductPrice.StreamId(sku);
        var bytes = id.ToByteArray();

        // Assert - Version field (4 bits at offset 48-51) should be 0101 (5)
        var versionByte = bytes[6];
        var versionNibble = (versionByte & 0xF0) >> 4;
        versionNibble.ShouldBe(5, "UUID version must be 5 (SHA-1 hash, RFC 4122 §4.3)");
    }

    [Fact]
    public void StreamId_IsRfc4122Compliant_VariantBitsAre10()
    {
        // Arrange
        var sku = "DOG-FOOD-5LB";

        // Act
        var id = ProductPrice.StreamId(sku);
        var bytes = id.ToByteArray();

        // Assert - Variant field (2 bits at offset 64-65) should be 10 (RFC 4122)
        var variantByte = bytes[8];
        var variantBits = (variantByte & 0xC0) >> 6;
        variantBits.ShouldBe(2, "UUID variant must be 2 (binary 10, RFC 4122)");
    }

    [Fact]
    public void StreamId_UsesNamespacePrefixForIsolation()
    {
        // Arrange
        var sku = "DOG-FOOD-5LB";

        // Act
        var idWithPrefix = ProductPrice.StreamId(sku);

        // Compute a UUID v5 without "pricing:" prefix (to demonstrate namespace isolation)
        var namespaceBytes = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8").ToByteArray();
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(sku.ToUpperInvariant());
        var hash = System.Security.Cryptography.SHA1.HashData([.. namespaceBytes, .. nameBytes]);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        var idWithoutPrefix = new Guid(hash[..16]);

        // Assert
        idWithPrefix.ShouldNotBe(idWithoutPrefix, "pricing: namespace prefix provides collision isolation");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StreamId_WithInvalidSku_ThrowsArgumentException(string? invalidSku)
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => ProductPrice.StreamId(invalidSku!));
    }

    [Fact]
    public void StreamId_MatchesBetweenCreateAndDirectCall()
    {
        // Arrange
        var sku = "DOG-FOOD-5LB";

        // Act
        var idFromCreate = ProductPrice.Create(sku, DateTimeOffset.UtcNow).Id;
        var idFromStreamId = ProductPrice.StreamId(sku);

        // Assert
        idFromCreate.ShouldBe(idFromStreamId);
    }

    [Fact]
    public void StreamId_IsNotEmpty()
    {
        // Arrange
        var sku = "DOG-FOOD-5LB";

        // Act
        var id = ProductPrice.StreamId(sku);

        // Assert
        id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void StreamId_WithRealWorldSkus_ProducesUniqueIds()
    {
        // Arrange
        var skus = new[]
        {
            "DOG-FOOD-5LB",
            "CAT-FOOD-3LB",
            "BIRD-SEED-10LB",
            "FISH-TANK-20GAL",
            "HAMSTER-WHEEL-6IN",
            "REPTILE-HEAT-LAMP",
            "RABBIT-HUTCH-LG",
            "GUINEA-PIG-BEDDING"
        };

        // Act
        var ids = skus.Select(ProductPrice.StreamId).ToList();

        // Assert
        ids.Distinct().Count().ShouldBe(skus.Length, "Each SKU must produce a unique stream ID");
    }

    [Fact]
    public void StreamId_WithSpecialCharacters_HandlesCorrectly()
    {
        // Arrange
        var skus = new[]
        {
            "DOG-FOOD-5LB",
            "DOG_FOOD_5LB",
            "DOG.FOOD.5LB",
            "DOG FOOD 5LB"
        };

        // Act
        var ids = skus.Select(ProductPrice.StreamId).ToList();

        // Assert - Different special characters should produce different IDs
        ids.Distinct().Count().ShouldBe(skus.Length);
    }
}
