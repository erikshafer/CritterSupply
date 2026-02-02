namespace ProductCatalog.Api.IntegrationTests;

/// <summary>
/// xUnit collection definition for integration tests.
/// Ensures tests run sequentially and share the same ProductCatalogFixture instance.
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<TestFixture>
{
    public const string Name = "product-catalog-integration";
}
