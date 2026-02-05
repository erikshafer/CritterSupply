---
name: testcontainers-integration-tests
description: Write integration tests using TestContainers for .NET with xUnit. Covers infrastructure testing with real databases, message queues, and caches in Docker containers instead of mocks.
---

# Integration Testing with TestContainers

> **Scope:** This skill covers TestContainers infrastructure setup (Postgres, RabbitMQ, etc.) — getting real databases running in Docker for tests. For testing patterns using Wolverine handlers, Marten aggregates, and Alba HTTP scenarios, see `critterstack-testing-patterns.md`.

## When to Use This Skill

Use this skill when:
- Writing integration tests that need real infrastructure (databases, caches, message queues)
- Testing data access layers against actual databases
- Verifying database migrations and schema changes
- Testing Marten event store or document store behavior
- Testing EF Core entities with navigation properties, constraints, indexes
- Avoiding mocks for infrastructure components
- Ensuring tests work against production-like environments

## Core Principles

1. **Real Infrastructure Over Mocks** — Use actual databases/services in containers, not mocks
2. **Test Isolation** — Each test gets fresh containers or fresh data
3. **Automatic Cleanup** — TestContainers handles container lifecycle and cleanup
4. **Fast Startup** — Reuse containers across tests in the same collection when appropriate
5. **CI/CD Compatible** — Works seamlessly in Docker-enabled CI environments
6. **Port Randomization** — Containers use random ports to avoid conflicts

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

**Problems:**
- Doesn't test actual SQL queries
- Misses database constraints, indexes, and performance characteristics
- Can give false confidence
- Doesn't catch SQL syntax errors or schema mismatches
- Doesn't verify migrations work correctly
- Doesn't test Marten projections, event streams, or document queries
- Doesn't test EF Core navigation properties, lazy loading, or change tracking

### ✅ Better: TestContainers with Real Database

```csharp
// GOOD: Testing against a real Postgres database with Marten
public class InventoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("inventory_test_db")
        .WithName($"inventory-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private string? _connectionString;
    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _connectionString = _postgres.GetConnectionString();

        // Necessary for WebApplicationFactory usage with Alba
        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_connectionString);
                });

                // Disable external transports for Wolverine (e.g., RabbitMQ)
                services.DisableAllExternalWolverineTransports();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (Host != null)
        {
            await Host.StopAsync();
            await Host.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task AddInventory_WithValidData_SucceedsAndPersists()
    {
        var id = Guid.CreateVersion7();
        var command = new AddInventory(id, 10, "Initial stock");

        await Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl($"/api/inventory/{id}/add");
            s.StatusCodeShouldBe(200);
        });

        // Verify against real database
        using var session = Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
        var inventory = await session.LoadAsync<ProductInventory>(id);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(10);
    }
}
```

**Benefits:**
- Tests real database behavior (constraints, indexes, transactions)
- Catches SQL syntax errors and schema mismatches
- Verifies migrations work correctly
- Tests Marten event streams, projections, and document queries
- Tests EF Core navigation properties, constraints, and cascading deletes
- Gives confidence in data persistence and retrieval
- Detects performance issues (missing indexes, N+1 queries)

## Required NuGet Packages

```xml
<ItemGroup>
  <PackageReference Include="Testcontainers" Version="*" />
  <PackageReference Include="Testcontainers.PostgreSql" Version="*" />
  <PackageReference Include="xunit" Version="*" />
  <PackageReference Include="xunit.runner.visualstudio" Version="*" />
  <PackageReference Include="Alba" Version="*" />
  <PackageReference Include="Shouldly" Version="*" />
</ItemGroup>
```

## TestContainers Setup Patterns

### Pattern 1: Marten-Based BC (Event Store or Document Store)

```csharp
using Alba;
using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;

namespace YourBC.Api.IntegrationTests;

public class TestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("yourbc_test_db")
        .WithName($"yourbc-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private string? _connectionString;

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _connectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_connectionString);
                });

                services.DisableAllExternalWolverineTransports();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (Host != null)
        {
            try
            {
                await Host.StopAsync();
                await Host.DisposeAsync();
            }
            catch (ObjectDisposedException)
            {
                // Ignore if already disposed during async shutdown
            }
            catch (TaskCanceledException)
            {
                // Ignore if tasks were canceled during async shutdown
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException))
            {
                // Ignore cancellation/disposal exceptions during shutdown
            }
        }

        await _postgres.DisposeAsync();
    }

    public IDocumentSession GetDocumentSession()
    {
        return Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
    }

    public IDocumentStore GetDocumentStore()
    {
        return Host.Services.GetRequiredService<IDocumentStore>();
    }

    public async Task CleanAllDocumentsAsync()
    {
        var store = GetDocumentStore();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
    }
}
```

**Key Points:**
- Uses `PostgreSqlBuilder("postgres:18-alpine")` for lightweight, fast container startup
- Generates unique container name with `Guid.NewGuid()` to avoid conflicts
- Overrides `ConfigureMarten()` to inject test connection string
- Disables external Wolverine transports (RabbitMQ) for isolated tests
- Provides helper methods (`GetDocumentSession()`, `CleanAllDocumentsAsync()`) for test convenience

### Pattern 2: EF Core-Based BC

```csharp
using Alba;
using JasperFx.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Wolverine;

namespace YourBC.Api.IntegrationTests;

public class TestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("yourbc_test_db")
        .WithName($"yourbc-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private string? _connectionString;

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _connectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove default DbContext registration
                services.RemoveAll<DbContextOptions<YourDbContext>>();
                services.RemoveAll<YourDbContext>();

                // Register DbContext with test connection string
                services.AddDbContext<YourDbContext>(options =>
                    options.UseNpgsql(_connectionString));

                services.DisableAllExternalWolverineTransports();
            });
        });

        // Apply migrations
        using var scope = Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YourDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (Host != null)
        {
            try
            {
                await Host.StopAsync();
                await Host.DisposeAsync();
            }
            catch (ObjectDisposedException)
            {
                // Ignore if already disposed during async shutdown
            }
            catch (TaskCanceledException)
            {
                // Ignore if tasks were canceled during async shutdown
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException))
            {
                // Ignore cancellation/disposal exceptions during shutdown
            }
        }

        await _postgres.DisposeAsync();
    }

    public YourDbContext GetDbContext()
    {
        var scope = Host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<YourDbContext>();
    }

    public async Task CleanAllDataAsync()
    {
        using var scope = Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YourDbContext>();

        // Use EF Core's ExecuteDeleteAsync for bulk delete operations
        await dbContext.YourEntitySet.ExecuteDeleteAsync();
    }
}
```

**Key Points:**
- Uses `RemoveAll<T>()` to replace production DbContext registration with test registration
- Calls `Database.MigrateAsync()` to apply migrations after container startup
- Provides `GetDbContext()` for direct database access in tests
- Uses `ExecuteDeleteAsync()` for efficient bulk delete operations between tests

### Collection Fixtures for Sequential Test Execution

**IMPORTANT:** Use xUnit collection fixtures to ensure sequential test execution and avoid Marten DDL concurrency issues:

```csharp
// IntegrationTestCollection.cs
namespace YourBC.Api.IntegrationTests;

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<TestFixture>
{
    public const string Name = "Integration Tests";
}

// In test classes
[Collection(IntegrationTestCollection.Name)]
public class YourFeatureTests
{
    private readonly TestFixture _fixture;

    public YourFeatureTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    // Tests...
}
```

**Why Collection Fixtures?**
- Prevents parallel test execution (which causes Marten DDL concurrency errors)
- Shares a single TestFixture instance across all tests in the collection
- Ensures sequential test execution within the collection
- Faster than creating a new container for each test

## Best Practices

### 1. Always Use IAsyncLifetime

TestContainers requires async initialization for container startup:

```csharp
// GOOD
public class TestFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Additional setup...
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}

// BAD - Don't use constructor/Dispose()
public class TestFixture : IDisposable
{
    public TestFixture()
    {
        _postgres.StartAsync().Wait(); // Deadlock risk!
    }

    public void Dispose()
    {
        _postgres.DisposeAsync().Wait(); // Deadlock risk!
    }
}
```

### 2. Use Unique Container Names

Prevents conflicts when running tests in parallel across different test classes:

```csharp
// GOOD
new PostgreSqlBuilder()
    .WithName($"yourbc-postgres-test-{Guid.NewGuid():N}")
    .Build();

// BAD - Can cause conflicts
new PostgreSqlBuilder()
    .WithName("yourbc-postgres-test")
    .Build();
```

### 3. Clean Data Between Tests (Not Containers)

Reusing containers is faster than recreating them:

```csharp
[Fact]
public async Task Test1()
{
    // Clean data before test
    await _fixture.CleanAllDocumentsAsync();

    // Test logic...
}

[Fact]
public async Task Test2()
{
    // Clean data before test
    await _fixture.CleanAllDocumentsAsync();

    // Test logic...
}
```

### 4. Use Lightweight Images

Alpine-based images are smaller and start faster:

```csharp
// GOOD - Alpine image (40MB)
new PostgreSqlBuilder("postgres:18-alpine")

// OK - Standard image (130MB)
new PostgreSqlBuilder("postgres:18")
```

### 5. Handle Cleanup Exceptions

TestContainers can throw disposal exceptions during async shutdown:

```csharp
public async Task DisposeAsync()
{
    if (Host != null)
    {
        try
        {
            await Host.StopAsync();
            await Host.DisposeAsync();
        }
        catch (ObjectDisposedException)
        {
            // Ignore - already disposed
        }
        catch (TaskCanceledException)
        {
            // Ignore - async cleanup canceled
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
            e is OperationCanceledException or ObjectDisposedException))
        {
            // Ignore - composite disposal errors
        }
    }

    await _postgres.DisposeAsync();
}
```

### 6. Disable External Transports

Tests should not depend on external message brokers:

```csharp
Host = await AlbaHost.For<Program>(builder =>
{
    builder.ConfigureServices(services =>
    {
        // Disables RabbitMQ, Azure Service Bus, etc.
        services.DisableAllExternalWolverineTransports();
    });
});
```

## Performance Tips

### 1. Reuse Containers Across Tests

Use collection fixtures to share a single container across all tests in a class:

```csharp
[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<TestFixture>
{
    public const string Name = "Integration Tests";
}

[Collection(IntegrationTestCollection.Name)]
public class MyTests
{
    private readonly TestFixture _fixture;

    public MyTests(TestFixture fixture)
    {
        _fixture = fixture; // Shared across all tests
    }
}
```

**Result:** Container starts once, tests clean data between runs.

### 2. Cache Docker Images

Docker caches pulled images locally, so subsequent test runs are faster:

```bash
# First run: Downloads image (~40MB for Alpine)
dotnet test

# Subsequent runs: Uses cached image (instant)
dotnet test
```

### 3. Use Parallel Test Execution (Between Collections)

xUnit runs test collections in parallel by default:

```csharp
// Collection A (runs in parallel with B and C)
[Collection("Collection A")]
public class TestsA { }

// Collection B (runs in parallel with A and C)
[Collection("Collection B")]
public class TestsB { }

// Collection C (runs in parallel with A and B)
[Collection("Collection C")]
public class TestsC { }
```

TestContainers handles port randomization automatically, so parallel execution is safe.

### 4. Limit Container Resources (Optional)

For resource-constrained CI environments:

```csharp
new PostgreSqlBuilder()
    .WithResourceReusable(true) // Reuse across test sessions
    .Build();
```

## Testing Patterns

### Marten Event Store Tests

```csharp
[Fact]
public async Task EventStream_CanAppendAndRetrieve()
{
    var orderId = Guid.CreateVersion7();

    // Arrange — Start an event stream
    using (var session = _fixture.GetDocumentSession())
    {
        session.Events.StartStream<Order>(orderId, new OrderPlaced(orderId, 100m));
        await session.SaveChangesAsync();
    }

    // Act — Append another event
    using (var session = _fixture.GetDocumentSession())
    {
        session.Events.Append(orderId, new OrderShipped(orderId, DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();
    }

    // Assert — Verify aggregate state
    using (var session = _fixture.GetDocumentSession())
    {
        var order = await session.Events.AggregateStreamAsync<Order>(orderId);

        order.ShouldNotBeNull();
        order.Status.ShouldBe(OrderStatus.Shipped);
    }
}
```

### Marten Document Store Tests

```csharp
[Fact]
public async Task Document_CanStoreAndRetrieve()
{
    var product = Product.Create("SKU-001", "Test Product", "Description", "Dogs");

    // Arrange — Store document
    using (var session = _fixture.GetDocumentSession())
    {
        session.Store(product);
        await session.SaveChangesAsync();
    }

    // Act & Assert — Retrieve document
    using (var session = _fixture.GetDocumentSession())
    {
        var retrieved = await session.LoadAsync<Product>("SKU-001");

        retrieved.ShouldNotBeNull();
        retrieved.Name.Value.ShouldBe("Test Product");
    }
}
```

### EF Core Entity Tests

```csharp
[Fact]
public async Task Customer_WithAddresses_NavigationPropertyLoads()
{
    var customerId = Guid.CreateVersion7();

    // Arrange — Seed customer with address
    using (var dbContext = _fixture.GetDbContext())
    {
        var customer = new Customer { Id = customerId, Email = "test@example.com" };
        var address = new Address { CustomerId = customerId, Street = "123 Main St" };

        dbContext.Customers.Add(customer);
        dbContext.Addresses.Add(address);
        await dbContext.SaveChangesAsync();
    }

    // Act & Assert — Verify navigation property
    using (var dbContext = _fixture.GetDbContext())
    {
        var customer = await dbContext.Customers
            .Include(c => c.Addresses)
            .FirstAsync(c => c.Id == customerId);

        customer.Addresses.Count.ShouldBe(1);
        customer.Addresses.First().Street.ShouldBe("123 Main St");
    }
}
```

## Common Pitfalls

### ❌ Forgetting to Start Container

```csharp
// BAD - Container not started
public async Task InitializeAsync()
{
    _connectionString = _postgres.GetConnectionString(); // WRONG!

    Host = await AlbaHost.For<Program>(/*...*/);
}
```

**Fix:** Always call `await _postgres.StartAsync()` first.

### ❌ Hardcoding Connection Strings

```csharp
// BAD - Hardcoded port
services.ConfigureMarten(opts =>
{
    opts.Connection("Host=localhost;Port=5432;Database=test");
});
```

**Fix:** Use TestContainers' generated connection string (includes random port).

### ❌ Not Cleaning Data Between Tests

```csharp
// BAD - Data leaks between tests
[Fact]
public async Task Test1()
{
    await _fixture.Host.Scenario(s => s.Post.Json(product).ToUrl("/api/products"));
}

[Fact]
public async Task Test2()
{
    // Test assumes empty database, but product from Test1 exists!
    await _fixture.Host.Scenario(s => s.Get.Url("/api/products"));
}
```

**Fix:** Call `await _fixture.CleanAllDocumentsAsync()` at the start of each test.

### ❌ Blocking on Async Methods

```csharp
// BAD - Deadlock risk
public TestFixture()
{
    _postgres.StartAsync().Wait();
}
```

**Fix:** Use `IAsyncLifetime` with proper async/await.

## CI/CD Considerations

### GitHub Actions

```yaml
name: Integration Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v3

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'

      - name: Start Docker
        run: docker info

      - name: Run Integration Tests
        run: dotnet test --filter "Category=Integration"
```

**Key Points:**
- GitHub Actions runners have Docker pre-installed
- TestContainers automatically detects Docker and uses it
- No additional configuration needed

### Docker-in-Docker (DinD)

Some CI environments require Docker-in-Docker setup. Consult your CI provider's documentation.

## Summary

| Aspect | Recommendation |
|--------|----------------|
| **Fixture Pattern** | Use `IAsyncLifetime` for async container startup |
| **Container Reuse** | Share via collection fixtures for speed |
| **Image Choice** | Use Alpine images (`postgres:18-alpine`) |
| **Data Cleanup** | Clean data between tests, not containers |
| **Exception Handling** | Catch disposal exceptions in `DisposeAsync()` |
| **External Services** | Disable Wolverine transports in tests |
| **Parallel Execution** | Use collection fixtures to control parallelism |
| **Helper Methods** | Provide `GetDocumentSession()`, `CleanAllDocumentsAsync()` for convenience |

## References

- **TestContainers for .NET:** [https://dotnet.testcontainers.org/](https://dotnet.testcontainers.org/)
- **Alba:** [https://jasperfx.github.io/alba/](https://jasperfx.github.io/alba/)
- **xUnit Collection Fixtures:** [https://xunit.net/docs/shared-context](https://xunit.net/docs/shared-context)
- **Marten:** [https://martendb.io/](https://martendb.io/)
