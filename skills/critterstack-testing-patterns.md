# Critter Stack Testing Patterns

Patterns for testing Wolverine and Marten applications in CritterSupply.

## Planned: End-to-End Testing

This document is intended for integration and unit level tests, not end-to-end (E2E) tests. The intent is that system-wide E2E testing will be appended here in its own section or be included in another document. The idea at the moment is to leverage a BDD-aligned framework like SpecFlow and write the specifications in the Gherkin language.

## Core Philosophy

1. **Prefer integration tests over unit tests** — Test complete vertical slices
2. **Use real infrastructure** — TestContainers for Postgres, RabbitMQ
3. **Pure functions are easy to unit test** — Thanks to A-Frame architecture
4. **BDD-style for integration tests** — Focus on behavior, not implementation

## Testing Tools

| Tool | Purpose |
|------|---------|
| **xUnit** | Test framework |
| **Shouldly** | Readable assertions |
| **Alba** | HTTP integration testing |
| **TestContainers** | Real Postgres/RabbitMQ in Docker |
| **NSubstitute** | Mocking (only when necessary) |

Simply put, xUnit is mature and proven as a test framework. Shouldly provides great error messages ontop of being declaritive. Alba is another open-source project from the JasperFx team, like Wolverine and Marten, enabling effortless usage and a declarative syntax for ASP.NET Core application testing. TestContainers allow us to use actual databases for testing our applications that we can spin-up and spin-down with ease. NSubstitute is the tool of choice if we absolutely need to mock something, as it provides a succinct syntax to focus on behavior instead of configuration.

## Integration Test Fixture

### Standard TestFixture Pattern (Marten)

All Marten-based BCs (event store or document store) use this standardized fixture pattern:

```csharp
using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace YourBC.Api.IntegrationTests;

/// <summary>
/// Test fixture providing PostgreSQL via TestContainers and Alba host for integration tests.
/// Uses collection fixture pattern to ensure sequential test execution and proper resource sharing.
/// </summary>
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

        // Necessary for WebApplicationFactory usage with Alba for integration testing
        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Configure Marten with the test container connection string directly
                services.ConfigureMarten(opts =>
                {
                    opts.Connection(_connectionString);
                });

                // Disable external Wolverine transports for testing
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

    /// <summary>
    /// Gets a Marten document session for direct database operations.
    /// Caller is responsible for disposing the session.
    /// </summary>
    public IDocumentSession GetDocumentSession()
    {
        return Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
    }

    /// <summary>
    /// Gets the document store for advanced operations like cleaning data.
    /// </summary>
    public IDocumentStore GetDocumentStore()
    {
        return Host.Services.GetRequiredService<IDocumentStore>();
    }

    /// <summary>
    /// Cleans all document data from the database. Use between tests that need isolation.
    /// </summary>
    public async Task CleanAllDocumentsAsync()
    {
        var store = GetDocumentStore();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
    }

    /// <summary>
    /// Executes a message through Wolverine and waits for all cascading messages to complete.
    /// This ensures all side effects are persisted before assertions.
    /// Messages with no routes (like integration messages to other contexts) are allowed.
    /// </summary>
    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host)
            .ExecuteAndWaitAsync((Func<IMessageContext, Task>)(async ctx =>
            {
                await ctx.InvokeAsync(message);
            }));
    }

    /// <summary>
    /// This method allows us to make HTTP calls into our system in memory with Alba while
    /// leveraging Wolverine's test support for message tracking to both record outgoing messages.
    /// This ensures that any cascaded work spawned by the initial command is completed before
    /// passing control back to the calling test.
    /// </summary>
    protected async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(Action<Scenario> configuration)
    {
        IScenarioResult result = null!;

        var tracked = await Host.ExecuteAndWaitAsync(async () =>
        {
            result = await Host.Scenario(configuration);
        });

        return (tracked, result);
    }
}
```

### EF Core TestFixture Pattern

For BCs using Entity Framework Core (like Customer Identity):

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
                // Remove the default DbContext registration
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

> **Reference:** [Alba Documentation](https://jasperfx.github.io/alba/)

## Integration Test Pattern

### Using Alba for HTTP Integration Tests

```csharp
[Collection(IntegrationTestCollection.Name)]
public class AddProductTests
{
    private readonly TestFixture _fixture;

    public AddProductTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanAddProduct_WithValidData_ReturnsCreated()
    {
        // Arrange
        var command = new AddProduct(
            "TEST-001",
            "Test Product",
            "Test Description",
            "Dogs",
            null, // Subcategory
            null, // Long description
            new List<ProductImageDto>
            {
                new("https://example.com/image.jpg", "Product image", 0)
            },
            new ProductDimensionsDto(10m, 10m, 5m, 2.5m)
        );

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/products");
            s.StatusCodeShouldBe(201);
            s.Header("Location").ShouldNotBeNullOrEmpty();
        });

        // Verify product was persisted
        using var session = _fixture.GetDocumentSession();
        var product = await session.LoadAsync<Product>("TEST-001");

        product.ShouldNotBeNull();
        product.Name.Value.ShouldBe("Test Product");
        product.Category.ShouldBe("Dogs");
        product.Images.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CanAddProduct_WithInvalidSku_Returns400()
    {
        var command = new AddProduct(
            "invalid sku!", // Invalid SKU format
            "Test Product",
            "Description",
            "Dogs",
            null, null, null, null
        );

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/products");
            s.StatusCodeShouldBe(400);
        });
    }
}
```

### Event-Sourced Aggregate Tests

```csharp
[Collection(IntegrationTestCollection.Name)]
public class CapturePaymentTests
{
    private readonly TestFixture _fixture;

    public CapturePaymentTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CapturePayment_WithAuthorizedPayment_ReturnsSuccess()
    {
        // Arrange — seed the event stream
        var paymentId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();

        using (var session = _fixture.GetDocumentSession())
        {
            var authorized = new PaymentAuthorized(paymentId, orderId, 100m, "auth_123", DateTimeOffset.UtcNow);
            session.Events.StartStream<Payment>(paymentId, authorized);
            await session.SaveChangesAsync();
        }

        // Act — invoke the endpoint
        var command = new CapturePayment(paymentId, 100m);

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl($"/api/payments/{paymentId}/capture");
            s.StatusCodeShouldBe(200);
        });

        // Assert — verify the aggregate state
        using (var session = _fixture.GetDocumentSession())
        {
            var payment = await session.Events.AggregateStreamAsync<Payment>(paymentId);

            payment.ShouldNotBeNull();
            payment.Status.ShouldBe(PaymentStatus.Captured);
            payment.CapturedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task CapturePayment_WithNonExistentPayment_Returns404()
    {
        var command = new CapturePayment(Guid.NewGuid(), 100m);

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl($"/api/payments/{command.PaymentId}/capture");
            s.StatusCodeShouldBe(404);
        });
    }
}
```

## TestFixture Helper Methods

The standardized TestFixture provides several helper methods for common test scenarios:

### GetDocumentSession() / GetDbContext()

Use for **direct database access** when you need to seed data or verify state:

```csharp
[Fact]
public async Task CanRetrieveProduct()
{
    // Seed data directly
    using (var session = _fixture.GetDocumentSession())
    {
        var product = Product.Create("TEST-001", "Test Product", "Description", "Dogs");
        session.Store(product);
        await session.SaveChangesAsync();
    }

    // Test retrieval via HTTP
    await _fixture.Host.Scenario(s =>
    {
        s.Get.Url("/api/products/TEST-001");
        s.StatusCodeShouldBe(200);
    });
}
```

### CleanAllDocumentsAsync() / CleanAllDataAsync()

Use for **test isolation** when tests need a clean slate:

```csharp
[Fact]
public async Task TestWithCleanState()
{
    // Clean all data before test
    await _fixture.CleanAllDocumentsAsync();

    // Now test with guaranteed empty database
    await _fixture.Host.Scenario(s =>
    {
        s.Get.Url("/api/products");
        s.StatusCodeShouldBe(200);
    });

    var result = await s.ReadAsJsonAsync<ProductListResponse>();
    result.Products.Count.ShouldBe(0); // No products exist
}
```

### ExecuteAndWaitAsync()

Use for **message-based testing** when you need to invoke Wolverine handlers directly:

```csharp
[Fact]
public async Task CanProcessMessageWithCascading()
{
    var command = new ReserveStock("SKU-001", "WH-01", 10);

    var tracked = await _fixture.ExecuteAndWaitAsync(command);

    // All cascading messages and side effects completed
    using var session = _fixture.GetDocumentSession();
    var inventory = await session.LoadAsync<ProductInventory>("SKU-001:WH-01");
    inventory.AvailableQuantity.ShouldBe(90); // Reduced by 10
}
```

### TrackedHttpCall()

Use for **HTTP calls with message tracking** when testing endpoints that publish integration messages:

```csharp
[Fact]
public async Task HttpEndpoint_PublishesIntegrationMessage()
{
    var command = new CompleteCheckout(checkoutId);

    var (tracked, result) = await _fixture.TrackedHttpCall(s =>
    {
        s.Post.Json(command).ToUrl($"/api/checkouts/{checkoutId}/complete");
        s.StatusCodeShouldBe(200);
    });

    // Verify integration message was sent (even though no route exists in test)
    tracked.Sent.MessagesOf<CheckoutCompleted>().Any().ShouldBeTrue();
}
```

## Unit Testing Pure Functions

Thanks to A-Frame architecture, handlers are pure functions that are easy to unit test:

```csharp
public class ProcessPaymentHandlerTests
{
    [Fact]
    public void Before_WithNullPayment_Returns404()
    {
        var command = new ProcessPayment(Guid.NewGuid(), 100m);

        var result = ProcessPaymentHandler.Before(command, payment: null);

        result.Status.ShouldBe(404);
        result.Detail.ShouldBe("Payment not found");
    }

    [Fact]
    public void Before_WithAlreadyProcessedPayment_Returns400()
    {
        var command = new ProcessPayment(Guid.NewGuid(), 100m);
        var payment = CreatePayment(PaymentStatus.Processed);

        var result = ProcessPaymentHandler.Before(command, payment);

        result.Status.ShouldBe(400);
    }

    [Fact]
    public void Handle_WithPendingPayment_ReturnsProcessedEvent()
    {
        var command = new ProcessPayment(Guid.NewGuid(), 100m);
        var payment = CreatePayment(PaymentStatus.Pending);

        var (events, messages) = ProcessPaymentHandler.Handle(command, payment);

        events.ShouldContain(e => e is PaymentProcessed);
    }

    private static Payment CreatePayment(PaymentStatus status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 100m, status, null, null);
}
```

## Testing Validators

```csharp
public class ProcessPaymentValidatorTests
{
    private readonly ProcessPayment.ProcessPaymentValidator _validator = new();

    [Fact]
    public void Validate_WithEmptyPaymentId_Fails()
    {
        var command = new ProcessPayment(Guid.Empty, 100m);

        var result = _validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "PaymentId");
    }

    [Fact]
    public void Validate_WithZeroAmount_Fails()
    {
        var command = new ProcessPayment(Guid.NewGuid(), 0m);

        var result = _validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "Amount");
    }

    [Fact]
    public void Validate_WithValidCommand_Passes()
    {
        var command = new ProcessPayment(Guid.NewGuid(), 100m);

        var result = _validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }
}
```

> **Reference:** [FluentValidation Testing](https://docs.fluentvalidation.net/en/latest/testing.html)

## Testing Event-Sourced Aggregates

```csharp
public class PaymentAggregateTests
{
    [Fact]
    public void Apply_PaymentProcessed_UpdatesStatus()
    {
        var payment = CreatePendingPayment();
        var @event = new PaymentProcessed(payment.Id, DateTimeOffset.UtcNow);

        var updated = payment.Apply(@event);

        updated.Status.ShouldBe(PaymentStatus.Processed);
        updated.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Apply_PaymentFailed_IncrementsRetryCount()
    {
        var payment = CreatePendingPayment();
        var @event = new PaymentFailed(payment.Id, "Insufficient funds", true, DateTimeOffset.UtcNow);

        var updated = payment.Apply(@event);

        updated.Status.ShouldBe(PaymentStatus.Failed);
        updated.RetryCount.ShouldBe(1);
        updated.FailureReason.ShouldBe("Insufficient funds");
    }

    private static Payment CreatePendingPayment() =>
        new(Guid.NewGuid(), Guid.NewGuid(), 100m, PaymentStatus.Pending, 0, null, null);
}
```

## Cross-Context Refactoring Checklist

After changes affecting multiple bounded contexts, **always run the full test suite**:

**When to run all tests:**
- Adding/removing project references between BCs
- Moving code between projects
- Updating namespaces across files
- Refactoring handlers or sagas
- Modifying shared infrastructure

**Test execution:**
```bash
# Build first (catches compile errors)
dotnet build

# Run all tests
dotnet test

# Or by category
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"
```

**Exit criteria:**
- Solution builds with 0 errors
- All unit tests pass
- All integration tests pass
- No unused project references remain

## Shouldly Assertions

```csharp
// Basic assertions
result.ShouldNotBeNull();
result.Status.ShouldBe(PaymentStatus.Processed);
result.Amount.ShouldBeGreaterThan(0);

// Collection assertions
events.ShouldNotBeEmpty();
events.ShouldContain(e => e is PaymentProcessed);
events.Count.ShouldBe(1);

// Exception assertions
Should.Throw<InvalidOperationException>(() => payment.Process());
```

> **Reference:** [Shouldly Documentation](https://docs.shouldly.org/)

## Test Organization

```
tests/
  Payment Processing/
    Payments.IntegrationTests/
      Features/
        ProcessPaymentTests.cs
        CapturePaymentTests.cs
      PaymentsTestFixture.cs
    Payments.UnitTests/
      Handlers/
        ProcessPaymentHandlerTests.cs
      Validators/
        ProcessPaymentValidatorTests.cs
      Domain/
        PaymentAggregateTests.cs
```

## Key Principles

1. **Standardized TestFixture across all BCs** — Use the same helper methods for consistency
2. **Integration tests cover vertical slices** — HTTP request to database verification
3. **Unit tests cover pure functions** — `Before()`, `Handle()`, `Apply()` methods
4. **Use stubs for external services** — Tests shouldn't depend on third-party APIs
5. **Real infrastructure via TestContainers** — Postgres, RabbitMQ in Docker
6. **Collection fixtures prevent DDL concurrency** — Sequential test execution avoids Marten schema conflicts
7. **Test code follows production standards** — Same C# conventions apply

## TestFixture Standardization Summary

**All Marten-based BCs have these 5 helper methods:**

| Method | Purpose | When to Use |
|--------|---------|-------------|
| `GetDocumentSession()` | Direct DB access | Seeding data, verifying state |
| `GetDocumentStore()` | Advanced operations | Getting store for cleanup |
| `CleanAllDocumentsAsync()` | Clear all data | Test isolation between runs |
| `ExecuteAndWaitAsync<T>()` | Message execution | Testing commands with cascading |
| `TrackedHttpCall()` | HTTP + tracking | Testing endpoints that publish messages |

**EF Core BCs have equivalent methods:**

| Method | Purpose |
|--------|---------|
| `GetDbContext()` | Direct DB access |
| `CleanAllDataAsync()` | Clear all data via `ExecuteDeleteAsync()` |

**Exception handling in DisposeAsync():**
- Catches `ObjectDisposedException` (already disposed)
- Catches `TaskCanceledException` (async cleanup canceled)
- Catches `AggregateException` with cancellation/disposal inner exceptions

This ensures clean test teardown without spurious errors in test output.
