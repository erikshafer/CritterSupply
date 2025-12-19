namespace Fulfillment.Api.IntegrationTests;

/// <summary>
/// xUnit collection definition for integration tests.
/// Ensures all tests in this collection share the same TestFixture instance
/// and run sequentially to avoid database conflicts.
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<TestFixture>
{
    public const string Name = "Fulfillment Integration Tests";
}
