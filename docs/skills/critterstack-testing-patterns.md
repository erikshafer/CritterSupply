# Critter Stack Testing Patterns

> **Scope:** This skill covers testing patterns for Wolverine handlers, Marten aggregates, and Alba HTTP scenarios. For TestContainers infrastructure setup and container lifecycle management, see `testcontainers-integration-tests.md`.

Patterns for testing Wolverine and Marten applications in CritterSupply.

## Table of Contents

1. [Core Philosophy](#core-philosophy)
2. [Testing Tools](#testing-tools)
3. [Integration Test Fixture](#integration-test-fixture)
4. [Test Isolation Checklist](#test-isolation-checklist)
5. [Event Sourcing Race Conditions](#event-sourcing-race-conditions)
6. [Integration Test Pattern](#integration-test-pattern)
7. [TestFixture Helper Methods](#testfixture-helper-methods)
8. [Unit Testing Pure Functions](#unit-testing-pure-functions)
9. [Testing Validators](#testing-validators)
10. [Cross-Context Refactoring](#cross-context-refactoring)
11. [Shouldly Assertions](#shouldly-assertions)
12. [Test Organization](#test-organization)
13. [Testing Async Patterns](#testing-async-patterns)
14. [Multi-BC BFF Testing](#multi-bc-bff-testing)
15. [Key Principles](#key-principles)

---

## Core Philosophy

1. **Prefer integration tests over unit tests** — Test complete vertical slices
2. **Use real infrastructure** — TestContainers for Postgres, RabbitMQ
3. **Pure functions are easy to unit test** — Thanks to A-Frame architecture
4. **BDD-style for integration tests** — Focus on behavior, not implementation

## Testing Tools

| Tool               | Purpose                          |
|--------------------|----------------------------------|
| **xUnit**          | Test framework                   |
| **Shouldly**       | Readable assertions              |
| **Alba**           | HTTP integration testing         |
| **TestContainers** | Real Postgres/RabbitMQ in Docker |
| **NSubstitute**    | Mocking (only when necessary)    |

## Integration Test Fixture

### Standard TestFixture Pattern (Marten)

All Marten-based BCs use this standardized fixture:

```csharp
using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

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
                services.ConfigureMarten(opts => opts.Connection(_connectionString));
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
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException)) { }
        }

        await _postgres.DisposeAsync();
    }

    public IDocumentSession GetDocumentSession() =>
        Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();

    public IDocumentStore GetDocumentStore() =>
        Host.Services.GetRequiredService<IDocumentStore>();

    public async Task CleanAllDocumentsAsync()
    {
        var store = GetDocumentStore();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
    }

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

### Test Authentication with Stable User IDs

**⚠️ CRITICAL:** Multi-request authorization tests need **stable user IDs** across requests.

**Problem:** Random user IDs break multi-request tests:

```csharp
// ❌ BAD: Random Guid for `sub` claim on EVERY request
new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())

// Result: Request 1 creates resource with UserId = "abc..."
//         Request 2 tries to edit with UserId = "xyz..." → 403 Forbidden
```

**Solution:** Use stable user ID via ITestAuthContext:

```csharp
public interface ITestAuthContext
{
    Guid UserId { get; }
}

public class TestAuthContext : ITestAuthContext
{
    public static readonly Guid TestAdminUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid UserId => TestAdminUserId;
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ITestAuthContext _authContext;

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITestAuthContext authContext)
        : base(options, logger, encoder)
    {
        _authContext = authContext;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // ⭐ M36.1 Addition: Check for Authorization header presence
        if (!Context.Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _authContext.UserId.ToString()),
            new Claim(ClaimTypes.Name, "Test Admin"),
            new Claim(ClaimTypes.Role, "Admin")
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

**⚠️ CRITICAL (M36.1):** `TestAuthHandler` must check for the `Authorization` header and return `AuthenticateResult.NoResult()` if absent. Without this check, `[Authorize]` endpoints always succeed in tests — even without credentials — masking production authentication failures. This bug existed silently from M36.0 through M36.1 Session 7.

**TestFixture Registration:**

```csharp
services.AddSingleton<ITestAuthContext, TestAuthContext>();
services.AddAuthentication("Test")
    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
```

**⭐ M36.1 Addition:** After creating the `AlbaHost`, call `AddDefaultAuthHeader()` to inject the default `Authorization: Bearer test-token` header into all Alba scenarios:

```csharp
Host = await AlbaHost.For<Program>(builder => { /* ... */ });
Host.AddDefaultAuthHeader(); // Required — ensures all scenarios are authenticated by default
```

**DO NOT** skip `AddDefaultAuthHeader()`. Without it, all HTTP requests via Alba will fail with 401 on `[Authorize]` endpoints because `TestAuthHandler` now correctly rejects requests without the header.

### Authorization Bypass for Policy-Based Endpoints

**CRITICAL:** Integration test fixtures must bypass **all** authorization policies. Missing policies cause 403 Forbidden.

**Single Policy Bypass:**

```csharp
services.AddAuthorization(opts =>
{
    opts.AddPolicy("PricingManager", policy =>
        policy.RequireAssertion(_ => true));  // Always succeeds
});
```

**Multi-Policy Bypass (M32.1):**

```csharp
// BCs with multiple policies need ALL bypassed
services.AddAuthorization(opts =>
{
    opts.AddPolicy("CustomerService", policy => policy.RequireAssertion(_ => true));
    opts.AddPolicy("FinanceClerk", policy => policy.RequireAssertion(_ => true));
    opts.AddPolicy("BackofficeUser", policy => policy.RequireAssertion(_ => true));
});
```

**Decision Matrix:**

| BC Scenario | Auth Bypass Pattern |
|-------------|---------------------|
| No authorization | No bypass needed |
| Single policy | Add one `RequireAssertion(_ => true)` |
| Multiple policies | Bypass **every** policy |
| Role-based (no policies) | Use `TestAuthHandler` with roles |

**How to Discover All Policies:**

```bash
grep -r "Authorize(Policy" src/YourBC/YourBC.Api/
```

### EF Core TestFixture Pattern

For BCs using Entity Framework Core:

```csharp
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
                services.RemoveAll<DbContextOptions<YourDbContext>>();
                services.RemoveAll<YourDbContext>();

                services.AddDbContext<YourDbContext>(options =>
                    options.UseNpgsql(_connectionString));

                services.DisableAllExternalWolverineTransports();
            });
        });

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
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException)) { }
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
        await dbContext.YourEntitySet.ExecuteDeleteAsync();
    }
}
```

## Test Isolation Checklist

### ✅ TestFixture Setup
- [ ] TestFixture does NOT seed data in `InitializeAsync()`
- [ ] TestFixture provides `CleanAllDocumentsAsync()` or `CleanAllDataAsync()`
- [ ] TestFixture provides `GetDocumentSession()` or `GetDbContext()`
- [ ] Container has unique name: `$"{bc}-postgres-test-{Guid.NewGuid():N}"`
- [ ] Collection fixture defined for sequential execution

### ✅ Test Class Setup
- [ ] Test class implements `IAsyncLifetime`
- [ ] `InitializeAsync()` calls `_fixture.CleanAllDocumentsAsync()`
- [ ] `DisposeAsync()` returns `Task.CompletedTask`
- [ ] Test class has `[Collection(IntegrationTestCollection.Name)]`

### ✅ Test Method Setup
- [ ] Each test seeds its own data inline
- [ ] Tests do NOT rely on data from other tests
- [ ] Tests can run in any order

### ✅ Warning Signs
- ❌ Tests fail when run in isolation but pass together
- ❌ Tests pass in one order, fail in another
- ❌ Test depends on data created by fixture
- ❌ Data leakage between tests
- ❌ Seed data tests fail non-deterministically (see Seed Data Isolation below)

### Seed Data Isolation ⭐ *M36.1 Addition*

**Problem:** Test classes that call `CleanAllDocumentsAsync()` in `DisposeAsync()` can wipe seed data before seed data verification tests run. xUnit does not guarantee test class execution order.

**Solution:** Create a dedicated `SeedDataTests` class that reseeds in `InitializeAsync()` and does NOT clean in `DisposeAsync()`:

```csharp
[Collection(IntegrationTestCollection.Name)]
public class SeedDataTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public SeedDataTests(TestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ReseedAsync(); // ✅ Reseed before verification
    public Task DisposeAsync() => Task.CompletedTask; // ✅ Do NOT clean — leave seed data intact

    [Fact]
    public async Task Should_have_seeded_marketplaces() { /* verify seed data */ }
}
```

**TestFixture must expose `ReseedAsync()`:**

```csharp
public async Task ReseedAsync()
{
    await using var session = Store.LightweightSession();
    await YourSeedData.SeedAsync(Host.Services); // Reuse the same seed method as app startup
}
```

**DO NOT** verify seed data in a test class that also calls `CleanAllDocumentsAsync()` in `DisposeAsync()`. The cleanup will race with other test classes.

### Collection Fixtures for Sequential Execution

Use xUnit collection fixtures to ensure sequential test execution:

```csharp
[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<TestFixture>
{
    public const string Name = "Integration Tests";
}

[Collection(IntegrationTestCollection.Name)]
public class YourFeatureTests
{
    private readonly TestFixture _fixture;

    public YourFeatureTests(TestFixture fixture)
    {
        _fixture = fixture;
    }
}
```

## Event Sourcing Race Conditions

### The Problem: HTTP-Based Testing

When testing event-sourced aggregates via HTTP endpoints, a race condition occurs:

1. Command handler returns domain events
2. Wolverine's transaction middleware commits **asynchronously**
3. HTTP 200 response sent **BEFORE** transaction commits
4. Subsequent GET request reads stale aggregate state
5. Test fails with unexpected status or stale data

**Example:**

```csharp
// ❌ BAD: HTTP POST → immediate GET (race condition)
await _fixture.Host.Scenario(s =>
{
    s.Post.Json(new ApproveReturn(returnId))
        .ToUrl($"/api/returns/{returnId}/approve");
    s.StatusCodeShouldBe(200); // ✅ HTTP response sent
});

// RACE: Transaction may not be committed yet!
await _fixture.Host.Scenario(s =>
{
    s.Get.Url($"/api/returns/{returnId}");
    s.StatusCodeShouldBe(200); // ❌ May fail with stale data
});
```

### The Solution: Direct Command Invocation

Invoke commands **directly through Wolverine's message bus** and query **event store directly**:

```csharp
// ✅ GOOD: Direct invocation (no race condition)
var command = new ApproveReturn(returnId);
await _fixture.ExecuteAndWaitAsync(command);

// Query event store directly
await using var session = _fixture.GetDocumentSession();
var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);

aggregate.ShouldNotBeNull();
aggregate.Status.ShouldBe(ReturnStatus.Approved);
aggregate.ApprovedAt.ShouldNotBeNull();

// Verify HTTP GET still works (optional)
var getResult = await _fixture.Host.Scenario(s =>
{
    s.Get.Url($"/api/returns/{returnId}");
    s.StatusCodeShouldBe(200);
});
```

### Why This Pattern Works

1. **ExecuteAndWaitAsync guarantees completion** — All side effects complete before returning
2. **Query event store directly** — Event-sourced aggregates are source of truth
3. **Separate concerns** — Business logic tested separately from HTTP layer
4. **Aligns with eventual consistency** — Respects that projections are eventually consistent

### When to Use Each Pattern

| Pattern | Use Case |
|---------|----------|
| **Direct Command Invocation** | State-changing operations on event-sourced aggregates |
| **HTTP POST Tests** | Testing HTTP contract (status codes, validation errors) |
| **HTTP GET Tests** | Testing query endpoints and projections |

### Key Takeaway

For event-sourced aggregates:
- ✅ **DO** use direct command invocation (`ExecuteAndWaitAsync`)
- ✅ **DO** query event store directly (`session.Events.AggregateStreamAsync`)
- ✅ **DO** verify HTTP GET endpoints work after state changes
- ❌ **DON'T** rely on HTTP POST → immediate GET pattern
- ❌ **DON'T** add delays to work around race conditions

---

## Integration Test Pattern

### Using Alba for HTTP Integration Tests

```csharp
[Collection(IntegrationTestCollection.Name)]
public class AddProductTests
{
    private readonly TestFixture _fixture;

    public AddProductTests(TestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CanAddProduct_WithValidData_ReturnsCreated()
    {
        var command = new AddProduct("TEST-001", "Test Product", "Description", "Dogs");

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/products");
            s.StatusCodeShouldBe(201);
            s.Header("Location").ShouldNotBeNullOrEmpty();
        });

        using var session = _fixture.GetDocumentSession();
        var product = await session.LoadAsync<Product>("TEST-001");

        product.ShouldNotBeNull();
        product.Name.Value.ShouldBe("Test Product");
    }

    [Fact]
    public async Task CanAddProduct_WithInvalidSku_Returns400()
    {
        var command = new AddProduct("invalid sku!", "Test Product", "Description", "Dogs");

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

    public CapturePaymentTests(TestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CapturePayment_WithAuthorizedPayment_ReturnsSuccess()
    {
        // Arrange — seed event stream
        var paymentId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();

        using (var session = _fixture.GetDocumentSession())
        {
            var authorized = new PaymentAuthorized(paymentId, orderId, 100m, "auth_123", DateTimeOffset.UtcNow);
            session.Events.StartStream<Payment>(paymentId, authorized);
            await session.SaveChangesAsync();
        }

        // Act
        var command = new CapturePayment(paymentId, 100m);

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl($"/api/payments/{paymentId}/capture");
            s.StatusCodeShouldBe(200);
        });

        // Assert — verify aggregate state
        using (var session = _fixture.GetDocumentSession())
        {
            var payment = await session.Events.AggregateStreamAsync<Payment>(paymentId);

            payment.ShouldNotBeNull();
            payment.Status.ShouldBe(PaymentStatus.Captured);
            payment.CapturedAt.ShouldNotBeNull();
        }
    }
}
```

### `ProblemDetails` Assertion in Non-HTTP Handlers ⭐ *M39.0 Addition*

When `Before()` returns `ProblemDetails` in a non-HTTP message handler context, Wolverine stops the pipeline **without throwing an exception**. Tests must verify that state is unchanged, not that an exception was thrown.

**Context matters for assertions:**

| Handler Context | `ProblemDetails` Behavior | Test Assertion |
|-----------------|---------------------------|----------------|
| HTTP endpoint | Returns 400-family status code | Assert on `StatusCodeShouldBe(400)` |
| Message handler (RabbitMQ, local queue) | Stops pipeline silently — no exception | Assert aggregate state is unchanged |

```csharp
// ❌ WRONG — message handler context, no exception thrown
await Should.ThrowAsync<InvalidOperationException>(async () =>
    await _fixture.ExecuteAndWaitAsync(new RedeemCoupon(expiredCode, promotionId)));

// ✅ CORRECT — verify the aggregate was not modified
await _fixture.ExecuteAndWaitAsync(new RedeemCoupon(expiredCode, promotionId));
using var session = _fixture.GetDocumentSession();
var coupon = await session.Events.AggregateStreamAsync<Coupon>(streamId);
coupon!.Status.ShouldBe(CouponStatus.Expired); // unchanged — Before() rejected the command
```

See also `docs/skills/marten-event-sourcing.md` Anti-Pattern #10 for the Marten-side documentation.

## TestFixture Helper Methods

### GetDocumentSession() / GetDbContext()

Use for **direct database access** when seeding data or verifying state:

```csharp
[Fact]
public async Task CanRetrieveProduct()
{
    using (var session = _fixture.GetDocumentSession())
    {
        var product = Product.Create("TEST-001", "Test Product", "Description", "Dogs");
        session.Store(product);
        await session.SaveChangesAsync();
    }

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
    await _fixture.CleanAllDocumentsAsync();

    await _fixture.Host.Scenario(s =>
    {
        s.Get.Url("/api/products");
        s.StatusCodeShouldBe(200);
    });

    var result = await s.ReadAsJsonAsync<ProductListResponse>();
    result.Products.Count.ShouldBe(0);
}
```

### ExecuteAndWaitAsync()

Use for **message-based testing** when invoking Wolverine handlers directly:

```csharp
[Fact]
public async Task CanProcessMessageWithCascading()
{
    var command = new ReserveStock("SKU-001", "WH-01", 10);

    var tracked = await _fixture.ExecuteAndWaitAsync(command);

    using var session = _fixture.GetDocumentSession();
    var inventory = await session.LoadAsync<ProductInventory>("SKU-001:WH-01");
    inventory.AvailableQuantity.ShouldBe(90);
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

    tracked.Sent.MessagesOf<CheckoutCompleted>().Any().ShouldBeTrue();
}
```

## Unit Testing Pure Functions

### ⚠️ Auto-Transactions Do Not Apply to Direct Handler Calls ⭐ *M33.0 Addition*

Wolverine's `AutoApplyTransactions()` policy only fires for HTTP endpoints and Wolverine message handlers — **not** when you call a handler method directly in a test.

If a test calls a handler class directly (bypassing the Wolverine pipeline), you must explicitly call `await session.SaveChangesAsync()` after the handler returns. Otherwise changes are silently discarded.

```csharp
// ❌ WRONG — Handler called directly, auto-transaction does not fire
var handler = new AcknowledgeAlertHandler();
await handler.Handle(new AcknowledgeAlert(alertId, operatorId), session);
// Changes NOT committed — silently lost

// ✅ CORRECT — Explicit commit after direct handler invocation
await handler.Handle(new AcknowledgeAlert(alertId, operatorId), session);
await session.SaveChangesAsync();  // Required for direct calls

// ✅ PREFERRED — Use Wolverine pipeline (auto-transaction fires)
await _fixture.ExecuteAndWaitAsync(new AcknowledgeAlert(alertId, operatorId));
```

**Evidence:** M33.0 Session 13 found 3 integration tests failing after removing a manual `SaveChangesAsync()` from the `AcknowledgeAlert` handler (correct production fix — Wolverine handles the commit). The tests broke because they called the handler directly.

**Rule:** Prefer `ExecuteAndWaitAsync()` or `TrackedHttpCall()` in integration tests. Reserve direct handler invocation for unit tests of pure-function handlers that produce no persistence side effects.

---

Thanks to A-Frame architecture, handlers are pure functions:

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
    public void Validate_WithValidCommand_Passes()
    {
        var command = new ProcessPayment(Guid.NewGuid(), 100m);

        var result = _validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }
}
```

## Cross-Context Refactoring

After changes affecting multiple bounded contexts, **always run the full test suite**:

```bash
dotnet build
dotnet test
```

**When to run all tests:**
- Adding/removing project references between BCs
- Moving code between projects
- Updating namespaces across files
- Refactoring handlers or sagas
- Modifying shared infrastructure

**Exit criteria:**
- Solution builds with 0 errors
- All unit tests pass
- All integration tests pass

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

## Test Organization

```
tests/
  Payments/
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
```

## Testing Async Patterns

### Fan-Out Pattern Timing

**Problem:** Fan-out patterns (one command creates N child commands) need sufficient delays for async processing.

**Pattern:**

```csharp
// Handler creates multiple child commands
public static async Task<OutgoingMessages> Handle(
    GenerateCouponBatch cmd,
    IDocumentSession session)
{
    var outgoing = new OutgoingMessages();

    for (int i = 1; i <= cmd.Count; i++)
    {
        var couponCode = $"{cmd.Prefix.ToUpperInvariant()}-{i:D4}";
        outgoing.Add(new IssueCoupon(couponCode, cmd.PromotionId));
    }

    return outgoing;
}
```

**Testing with Generous Delay:**

```csharp
[Fact]
public async Task GenerateCouponBatch_Creates100Coupons()
{
    var promotionId = await CreatePromotion();
    var batchCmd = new GenerateCouponBatch(promotionId, "SAVE20", 100);

    await _fixture.ExecuteAndWaitAsync(batchCmd);

    // ✅ Wait 1000ms for async processing
    // GenerateCouponBatch creates N IssueCoupon commands
    // Each command creates aggregate + updates projections
    await Task.Delay(1000);

    await using var session = _fixture.GetDocumentSession();
    for (int i = 1; i <= 100; i++)
    {
        var code = $"SAVE20-{i:D4}";
        var coupon = await session.LoadAsync<CouponLookupView>(code);
        coupon.ShouldNotBeNull();
    }
}
```

**Guidelines:**

| Pattern | Recommended Delay | Why |
|---------|------------------|-----|
| Single command | 0ms (use `ExecuteAndWaitAsync`) | Wolverine waits for completion |
| Fan-out (N < 10) | 500ms | Small batch |
| Fan-out (N ≥ 10) | 1000ms | Larger batch, multiple DB ops |

### DCB Concurrency Testing ⭐ *M40.0 Addition*

DCB (Dynamic Consistency Boundary) concurrency tests prove that cross-stream optimistic concurrency actually rejects conflicting writes. Unlike standard optimistic concurrency tests (which check single-stream version conflicts), DCB concurrency tests verify that the **tag-based boundary** detects interleaved writes across multiple streams.

**Test structure:**

1. **Seed:** Issue a coupon and create an active promotion (two separate streams)
2. **First command:** Redeem the coupon — succeeds, appends `CouponRedeemed`
3. **Second command:** Attempt to redeem the same coupon — `Before()` rejects because boundary state shows `CouponStatus.Redeemed`
4. **Assert:** Verify the coupon is redeemed exactly once; the second redemption's effect is absent

```csharp
[Fact]
public async Task RedeemCoupon_ConcurrentRedemption_SecondIsRejectedByDcb()
{
    // Arrange — coupon and promotion exist
    var promotionId = await CreateActivePromotion();
    var couponCode = await IssueCoupon(promotionId);

    // Act — first redemption succeeds
    await _fixture.ExecuteAndWaitAsync(
        new RedeemCoupon(couponCode, promotionId, Guid.CreateVersion7(), Guid.CreateVersion7()));

    // Act — second redemption rejected by boundary state
    await _fixture.ExecuteAndWaitAsync(
        new RedeemCoupon(couponCode, promotionId, Guid.CreateVersion7(), Guid.CreateVersion7()));

    // Assert — coupon redeemed exactly once
    using var session = _fixture.GetDocumentSession();
    var coupon = await session.Events.AggregateStreamAsync<Coupon>(
        Coupon.StreamId(couponCode));
    coupon!.Status.ShouldBe(CouponStatus.Redeemed);
}
```

**What makes this different from standard concurrency tests:** Standard tests verify that two concurrent writes to the *same stream* trigger a version conflict. DCB tests verify that the *boundary state* (projected from tagged events across multiple streams) correctly reflects the first write before the second write is attempted, even though the writes target different streams.

**Reference:** `tests/Promotions/Promotions.IntegrationTests/CouponRedemptionTests.cs`

---

## Multi-BC BFF Testing

**From:** Backoffice BC (M32.0 Session 10)

Backend-for-Frontend (BFF) bounded contexts orchestrate multiple domain BCs via HTTP clients. Testing requires **stub clients** to isolate tests.

### Stub Client Pattern

```csharp
public class StubOrdersClient : IOrdersClient
{
    // Separate storage for list vs detail views
    private readonly Dictionary<Guid, OrderSummaryDto> _orders = new();
    private readonly Dictionary<Guid, OrderDetailDto> _orderDetails = new();

    public void AddOrder(OrderSummaryDto order) =>
        _orders[order.OrderId] = order;

    public void AddOrderDetail(OrderDetailDto detail) =>
        _orderDetails[detail.OrderId] = detail;

    public Task<IReadOnlyList<OrderSummaryDto>> GetOrders(Guid customerId)
    {
        var orders = _orders.Values
            .Where(o => o.CustomerId == customerId)
            .ToList();
        return Task.FromResult<IReadOnlyList<OrderSummaryDto>>(orders);
    }

    public Task<OrderDetailDto?> GetOrderDetail(Guid orderId) =>
        Task.FromResult(_orderDetails.GetValueOrDefault(orderId));
}
```

### BFF TestFixture with Stub Clients

```csharp
public class BackofficeTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = /* ... */;
    public IAlbaHost Host { get; private set; } = null!;

    // Stub clients (public for test setup)
    public StubCustomerIdentityClient CustomerIdentityClient { get; private set; } = null!;
    public StubOrdersClient OrdersClient { get; private set; } = null!;
    public StubReturnsClient ReturnsClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        CustomerIdentityClient = new StubCustomerIdentityClient();
        OrdersClient = new StubOrdersClient();
        ReturnsClient = new StubReturnsClient();

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts => opts.Connection(_connectionString));
                services.DisableAllExternalWolverineTransports();

                // Register stub clients
                services.AddScoped<ICustomerIdentityClient>(_ => CustomerIdentityClient);
                services.AddScoped<IOrdersClient>(_ => OrdersClient);
                services.AddScoped<IReturnsClient>(_ => ReturnsClient);
            });
        });
    }
}
```

### End-to-End Workflow Tests

```csharp
[Fact]
public async Task CustomerServiceWorkflow_SearchToReturnApproval_CompletesSuccessfully()
{
    var customerId = Guid.NewGuid();
    var orderId = Guid.NewGuid();
    var returnId = Guid.NewGuid();

    // Seed Customer Identity BC
    _fixture.CustomerIdentityClient.AddCustomer(new CustomerDto(
        customerId, "customer@example.com", "John Doe", []
    ));

    // Seed Orders BC (list + detail!)
    _fixture.OrdersClient.AddOrder(new OrderSummaryDto(
        orderId, customerId, 99.99m, "Delivered", DateTimeOffset.UtcNow.AddDays(-5)
    ));

    _fixture.OrdersClient.AddOrderDetail(new OrderDetailDto(
        orderId,
        customerId,
        "Delivered",
        99.99m,
        [new OrderLineItemDto("SKU123", "Dog Food", 2, 24.99m)],
        DateTimeOffset.UtcNow.AddDays(-5)
    ));

    // Seed Returns BC
    _fixture.ReturnsClient.AddReturn(new ReturnDetailDto(
        returnId, orderId, customerId, "Pending", [], DateTimeOffset.UtcNow.AddDays(-1)
    ));

    // Test complete CS workflow
    var customer = await _fixture.CustomerIdentityClient.GetCustomerByEmail("customer@example.com");
    customer.ShouldNotBeNull();

    var orders = await _fixture.OrdersClient.GetOrders(customerId);
    orders.Count.ShouldBe(1);

    var orderDetail = await _fixture.OrdersClient.GetOrderDetail(orderId);
    orderDetail.ShouldNotBeNull();

    var returnDetail = await _fixture.ReturnsClient.GetReturn(returnId);
    returnDetail.ShouldNotBeNull();

    await _fixture.Host.Scenario(s =>
    {
        s.Post.Json(new { }).ToUrl($"/api/backoffice/returns/{returnId}/approve");
        s.StatusCodeShouldBe(200);
    });
}
```

### Common Pitfall: Forgetting AddOrderDetail()

**Problem:** Only added summary, not detail:

```csharp
// ❌ BAD: Only added summary
_fixture.OrdersClient.AddOrder(new OrderSummaryDto(orderId, ...));

// GetOrderDetail returns null!
var detail = await _fixture.OrdersClient.GetOrderDetail(orderId);
```

**Fix:** Call both `AddOrder()` and `AddOrderDetail()`:

```csharp
// ✅ GOOD: Add both summary and detail
_fixture.OrdersClient.AddOrder(new OrderSummaryDto(orderId, ...));
_fixture.OrdersClient.AddOrderDetail(new OrderDetailDto(orderId, ...));
```

**Lesson:** Stub clients must mirror real BC API structure (separate list vs detail endpoints).

### When to Use Multi-BC Composition Tests

✅ **Use when:**
- BFF composes data from 2+ domain BCs
- Testing end-to-end CS/admin workflows
- Verifying HTTP client orchestration

❌ **Don't use when:**
- Testing single-BC logic (use domain BC's own tests)
- Simple HTTP proxying (unnecessary complexity)

---

## Key Principles

1. **Standardized TestFixture across all BCs** — Use same helper methods
2. **Integration tests cover vertical slices** — HTTP request to database verification
3. **Unit tests cover pure functions** — `Before()`, `Handle()`, `Apply()` methods
4. **Use stubs for external services** — Tests shouldn't depend on third-party APIs
5. **Real infrastructure via TestContainers** — Postgres, RabbitMQ in Docker
6. **Collection fixtures prevent DDL concurrency** — Sequential test execution
7. **Test code follows production standards** — Same C# conventions apply

### TestFixture Helper Methods Summary

**All Marten-based BCs have these 5 methods:**

| Method | Purpose | When to Use |
|--------|---------|-------------|
| `GetDocumentSession()` | Direct DB access | Seeding data, verifying state |
| `GetDocumentStore()` | Advanced operations | Getting store for cleanup |
| `CleanAllDocumentsAsync()` | Clear all data | Test isolation |
| `ExecuteAndWaitAsync<T>()` | Message execution | Testing commands with cascading |
| `TrackedHttpCall()` | HTTP + tracking | Testing endpoints that publish messages |

**EF Core BCs have equivalent methods:**
- `GetDbContext()` — Direct DB access
- `CleanAllDataAsync()` — Clear all data via `ExecuteDeleteAsync()`

**Exception handling in DisposeAsync():**
- Catches `ObjectDisposedException` (already disposed)
- Catches `TaskCanceledException` (async cleanup canceled)
- Catches `AggregateException` with cancellation/disposal inner exceptions

This ensures clean test teardown without spurious errors.
