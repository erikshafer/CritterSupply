---
name: testcontainers-integration-tests
description: Write integration tests using TestContainers for .NET with xUnit. Covers infrastructure testing with real databases, message queues, and caches in Docker containers instead of mocks.
---

# Integration Testing with TestContainers

## When to Use This Skill

Use this skill when:
- Writing integration tests that need real infrastructure (databases, caches, message queues)
- Testing data access layers against actual databases
- Verifying message queue integrations
- Testing Redis caching behavior
- Avoiding mocks for infrastructure components
- Ensuring tests work against production-like environments
- Testing database migrations and schema changes

## Core Principles

1. **Real Infrastructure Over Mocks** - Use actual databases/services in containers, not mocks
2. **Test Isolation** - Each test gets fresh containers or fresh data
3. **Automatic Cleanup** - TestContainers handles container lifecycle and cleanup
4. **Fast Startup** - Reuse containers across tests in the same class when appropriate
5. **CI/CD Compatible** - Works seamlessly in Docker-enabled CI environments
6. **Port Randomization** - Containers use random ports to avoid conflicts

## Why TestContainers Over Mocks?

### ❌ Problems with Mocking Infrastructure

```csharp
// BAD: Mocking a database
public class OrderRepositoryTests
{
    private readonly Mock<IDbConnection> _mockDb = new();

    [Fact]
    public async Task GetOrder_ReturnsOrder()
    {
        // This doesn't test real SQL behavior, constraints, or performance
        _mockDb.Setup(db => db.QueryAsync<Order>(It.IsAny<string>()))
            .ReturnsAsync(new[] { new Order { Id = 1 } });

        var repo = new OrderRepository(_mockDb.Object);
        var order = await repo.GetOrderAsync(1);

        Assert.NotNull(order);
    }
}
```

Problems:
- Doesn't test actual SQL queries
- Misses database constraints, indexes, and performance
- Can give false confidence
- Doesn't catch SQL syntax errors or schema mismatches

### ✅ Better: TestContainers with Real Database

```csharp
// GOOD: Testing against a real database
public class OrderRepositoryTests : IAsyncLifetime
{
    public IAlbaHost? Host { get; private set; }

    private readonly PostgreSqlContainer _postgreSqlContainer =
        new PostgreSqlBuilder()
            .WithImage("postgres:latest")
            .WithDatabase("inventory_test_db")
            .WithName($"inventory-postgres-test-{Guid.NewGuid():N}")
            .WithCleanUp(true)
            .Build();

    // This is a one-time initialization of the system under test before the first usage
    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync()

        var postgresConn = _postgreSqlContainer.GetConnectionString();

        // Since we're using the JasperFx command line for processing and
        // want to use Alba for integration testing, this is enabled
        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(x =>
        {
            x.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(postgresConn);
                });

                // Disable external transports for Wolverine, such as RabbitMQ
                services.DisableAllExternalWolverineTransports();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (Host != null)
        {
            await Host.StopAsync();
            Host.Dispose();
        }
        
        await _postgreSqlContainer.DisposeAsync();
    }
    
    [Fact]
    public async Task AddInventorySucceeds()
    {
        var id = Guid.CreateVersion7();
        var command = new AddInventory(id, 10, "Initial stock");

        await _fixture.Host!.Scenario(x =>
        {
            x.Post.Url($"/api/inventory/{id}/add");
            x.Post.Json(command);
            x.StatusCodeShouldBeOk();
        });
    }
}
```

Benefits:
- Tests database behavior
- Catches constraint violations, index issues, and performance problems
- Verifies migrations work correctly
- Gives confidence in data persistence

## Required NuGet Packages

```xml
<ItemGroup>
  <PackageReference Include="Testcontainers" Version="*" />
  <PackageReference Include="xunit" Version="*" />
  <PackageReference Include="xunit.runner.visualstudio" Version="*" />
</ItemGroup>
```

## Best Practices

1. **Always Use IAsyncLifetime** - Proper async setup and teardown
2. **Wait for Port Availability** - Use `WaitStrategy` to ensure containers are ready
3. **Use Random Ports** - Let TestContainers assign ports automatically
4. **Clean Data Between Tests** - Either use fresh containers or truncate tables
5. **Reuse Containers When Possible** - Faster than creating new ones for each test
6. **Handle Cleanup** - Always dispose containers in `DisposeAsync`

## Performance Tips

1. **Reuse containers** - Share fixtures across tests in a collection
2. **Parallel execution** - TestContainers handles port conflicts automatically
3. **Use lightweight images** - Alpine versions are smaller and faster
4. **Cache images** - Docker will cache pulled images locally
5. **Limit container resources** - Set CPU/memory limits if needed:
