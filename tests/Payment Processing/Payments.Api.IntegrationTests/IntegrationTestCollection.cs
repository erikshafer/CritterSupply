namespace Payments.Api.IntegrationTests;

/// <summary>
/// Collection definition for integration tests that share the TestFixture.
/// Tests in this collection run sequentially to avoid database conflicts.
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<TestFixture>
{
    public const string Name = "payments-integration";
}
