namespace Returns.Api.IntegrationTests.CrossBcSmokeTests;

/// <summary>
/// xUnit collection definition to ensure all cross-BC smoke tests run sequentially
/// with a shared fixture. This prevents parallel test execution which would cause
/// RabbitMQ and database conflicts.
/// </summary>
[CollectionDefinition(nameof(CrossBcTestCollection))]
public class CrossBcTestCollection : ICollectionFixture<CrossBcTestFixture>
{
    // This class is never instantiated. Its purpose is to tell xUnit
    // to use the CrossBcTestFixture for all tests in this collection.
}
