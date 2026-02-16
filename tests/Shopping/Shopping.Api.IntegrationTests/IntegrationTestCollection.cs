namespace Shopping.Api.IntegrationTests;

/// <summary>
/// xUnit collection definition for integration tests.
/// Ensures tests run sequentially and share the same TestFixture instance.
/// </summary>
[CollectionDefinition(nameof(IntegrationTestCollection))]
public class IntegrationTestCollection : ICollectionFixture<TestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
