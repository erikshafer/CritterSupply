namespace ProductCatalog.IntegrationTests;

/// <summary>
/// xUnit collection definition for integration tests.
/// Ensures tests run sequentially and share the same ProductCatalogFixture instance.
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<ProductCatalogFixture>
{
    public const string Name = "product-catalog-integration";
}
