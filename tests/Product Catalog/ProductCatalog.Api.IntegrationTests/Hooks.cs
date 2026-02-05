using Reqnroll;

namespace ProductCatalog.Api.IntegrationTests;

[Binding]
public sealed class Hooks
{
    private static TestFixture? _testFixture;

    [BeforeTestRun]
    public static async Task BeforeTestRun()
    {
        _testFixture = new TestFixture();
        await _testFixture.InitializeAsync();
    }

    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        if (_testFixture != null)
        {
            await _testFixture.DisposeAsync();
        }
    }

    [BeforeScenario]
    public void BeforeScenario()
    {
        if (_testFixture == null)
        {
            throw new InvalidOperationException("TestFixture not initialized");
        }
    }

    public static TestFixture GetTestFixture()
    {
        if (_testFixture == null)
        {
            throw new InvalidOperationException("TestFixture not initialized. Ensure [BeforeTestRun] hook has executed.");
        }

        return _testFixture;
    }
}
