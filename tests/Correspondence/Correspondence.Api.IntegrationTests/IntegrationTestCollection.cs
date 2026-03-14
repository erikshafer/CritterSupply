namespace Correspondence.Api.IntegrationTests;

/// <summary>
/// Marks integration test classes to run sequentially using the shared TestFixture.
/// Prevents race conditions with shared Docker containers.
/// </summary>
[CollectionDefinition("Integration")]
public sealed class IntegrationTestCollection : ICollectionFixture<TestFixture>;
