namespace Payments.Api.IntegrationTests;

/// <summary>
/// Collection definition for integration tests that share the TestFixture.
/// Tests in this collection run sequentially to avoid database conflicts.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<TestFixture>
{
}
