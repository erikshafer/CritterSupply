namespace Orders.Api.IntegrationTests;

/// <summary>
/// Collection definition that ensures all integration tests sharing the TestFixture
/// run sequentially to avoid race conditions with shared resources.
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<TestFixture>
{
    public const string Name = "orders-integration";
}
