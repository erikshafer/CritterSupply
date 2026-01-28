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

```csharp
public class PaymentsTestFixture : IAsyncLifetime
{
    public IAlbaHost Host { get; private set; } = null!;
    public IDocumentSession Session { get; private set; } = null!;

    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:18-alpine")
            .WithDatabase("payments_test_db")
            .WithName($"payments-postgres-test-{Guid.NewGuid():N}")
            .WithCleanUp(true)
            .Build();

        await _postgres.StartAsync();

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace connection string with TestContainers
                services.Configure<MartenOptions>(opts =>
                    opts.Connection(_postgres.GetConnectionString()));

                // Use stub for external services
                services.AddSingleton<IPaymentGateway, StubPaymentGateway>();
            });
        });

        Session = Host.Services.GetRequiredService<IDocumentSession>();
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
```

> **Reference:** [Alba Documentation](https://jasperfx.github.io/alba/)

## Integration Test Pattern

```csharp
public class ProcessPaymentTests : IClassFixture<PaymentsTestFixture>
{
    private readonly PaymentsTestFixture _fixture;

    public ProcessPaymentTests(PaymentsTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPayment_WithValidPayment_ReturnsSuccess()
    {
        // Arrange — seed the aggregate
        var paymentId = Guid.CreateVersion7();
        var initiated = new PaymentInitiated(paymentId, Guid.NewGuid(), 100m, DateTimeOffset.UtcNow);
        _fixture.Session.Events.StartStream<Payment>(paymentId, initiated);
        await _fixture.Session.SaveChangesAsync();

        // Act — invoke the endpoint
        var command = new ProcessPayment(paymentId, 100m);

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl($"/api/payments/{paymentId}/process");
            s.StatusCodeShouldBe(200);
        });

        // Assert — verify the aggregate state
        var payment = await _fixture.Session.Events.AggregateStreamAsync<Payment>(paymentId);

        payment.ShouldNotBeNull();
        payment.Status.ShouldBe(PaymentStatus.Processed);
    }

    [Fact]
    public async Task ProcessPayment_WithNonExistentPayment_Returns404()
    {
        var command = new ProcessPayment(Guid.NewGuid(), 100m);

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl($"/api/payments/{command.PaymentId}/process");
            s.StatusCodeShouldBe(404);
        });
    }
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

1. **Integration tests cover vertical slices** — HTTP request to database verification
2. **Unit tests cover pure functions** — `Before()`, `Handle()`, `Apply()` methods
3. **Use stubs for external services** — Tests shouldn't depend on third-party APIs
4. **Real infrastructure via TestContainers** — Postgres, RabbitMQ in Docker
5. **Test code follows production standards** — Same C# conventions apply
