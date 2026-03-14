# Entity Framework Core + Wolverine Integration

Comprehensive patterns for using Entity Framework Core with Wolverine in CritterSupply. Customer Identity BC and Vendor Identity BC are the reference implementations.

---

## Table of Contents

1. [When to Use EF Core vs Marten](#when-to-use-ef-core-vs-marten)
2. [Philosophy: Storage Side Effects & Pure Functions](#philosophy-storage-side-effects--pure-functions)
3. [Setup and Configuration](#setup-and-configuration)
4. [Entity Model Design](#entity-model-design)
5. [Storage Operations and Side Effects](#storage-operations-and-side-effects)
6. [Transactional Inbox and Outbox](#transactional-inbox-and-outbox)
7. [Wolverine Handler Patterns](#wolverine-handler-patterns)
8. [Multi-Tenancy with EF Core](#multi-tenancy-with-ef-core)
9. [Saga Storage with EF Core](#saga-storage-with-ef-core)
10. [Publishing Domain Events](#publishing-domain-events)
11. [Database Migrations](#database-migrations)
12. [Testing with EF Core + Wolverine](#testing-with-ef-core--wolverine)
13. [Lessons Learned from CritterSupply](#lessons-learned-from-crittersupply)
14. [Common Pitfalls & Solutions](#common-pitfalls--solutions)
15. [Appendix: References & Further Reading](#appendix-references--further-reading)

---

## When to Use EF Core vs Marten

**Use EF Core when:**
- Traditional relational model fits naturally (Customer → Addresses with foreign keys)
- Navigation properties simplify queries (`customer.Addresses.Where(...)`)
- Foreign key constraints enforce referential integrity at the database level
- Schema evolution via migrations is preferred
- Team is more familiar with EF Core
- **Current state is all that matters** (no need for event history)
- Complex joins or aggregations across multiple tables

**Use Marten when:**
- Event sourcing is beneficial (Orders, Payments, Inventory) — audit trail is valuable
- Document model fits (flexible schema, JSON storage)
- No complex relational joins needed
- Aggregate boundaries are clear and self-contained
- You want projections (inline, async, live aggregations)

**Decision Rule:** If your BC needs event history or operates on aggregates with complex state transitions, use Marten. If it's primarily CRUD with relational data, use EF Core.

**Reference:** See [ADR 0002: EF Core for Customer Identity](../../decisions/0002-ef-core-for-customer-identity.md) for the full decision rationale.

---

## Philosophy: Storage Side Effects & Pure Functions

Wolverine's storage operations API is designed around a core principle from functional programming: **separate "deciding what to do" from "doing it."**

**The Problem:**
```csharp
// ❌ Handler mixes business logic with persistence infrastructure
public static async Task Handle(
    CreateItem command,
    MyDbContext dbContext,
    CancellationToken ct)
{
    // Business logic mixed with EF Core tracking, SaveChanges, etc.
    var item = new Item { Id = command.Id, Name = command.Name };
    dbContext.Items.Add(item);
    await dbContext.SaveChangesAsync(ct);
}
```

This is hard to unit test — you need to mock `DbContext`, set up tracking behavior, etc.

**The Solution: Storage Side Effects**
```csharp
// ✅ Pure function that returns a side effect
public static Insert<Item> Handle(CreateItem command)
{
    // Pure business logic — no infrastructure dependencies
    return Storage.Insert(new Item
    {
        Id = command.Id,
        Name = command.Name
    });
}
```

**Benefits:**
1. **Easy to unit test** — no `DbContext` mocking, just assert the return value
2. **Easy to reason about** — function signature tells you what it does
3. **Wolverine handles the "doing"** — transaction, SaveChangesAsync, outbox messaging
4. **Composable** — can return multiple storage actions via `UnitOfWork<T>`

This pattern applies to Marten, EF Core, and RavenDb integrations in Wolverine.

**Key Insight:** When you return `IStorageAction<T>` or `UnitOfWork<T>` from a handler, Wolverine automatically applies transactional middleware — even without `[Transactional]` or `AutoApplyTransactions()`. This is required because Wolverine needs to call `SaveChangesAsync()` to persist the operation within the same transaction as the outbox/inbox.

---

## Setup and Configuration

### Package Dependencies

```xml
<ItemGroup>
    <PackageReference Include="WolverineFx.EntityFrameworkCore" />
    <PackageReference Include="WolverineFx.Http.FluentValidation" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
</ItemGroup>
```

### Program.cs Configuration

```csharp
var builder = WebApplication.CreateBuilder(args);

// Configure EF Core with PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("postgres")
    ?? throw new Exception("PostgreSQL connection string not found");

builder.Services.AddDbContext<CustomerIdentityDbContext>(options =>
    options.UseNpgsql(connectionString));

// Wolverine configuration
builder.Host.UseWolverine(opts =>
{
    // Handler discovery
    opts.Discovery.IncludeAssembly(typeof(CustomerIdentityDbContext).Assembly);

    // Automatic transactions for handlers
    opts.Policies.AutoApplyTransactions();

    // Durable local queues for async processing
    opts.Policies.UseDurableLocalQueues();

    // Outbox pattern for reliable messaging
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    // FluentValidation integration
    opts.UseFluentValidation();
});

// Apply migrations on startup (development only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CustomerIdentityDbContext>();
    await dbContext.Database.MigrateAsync();
}
```

**Key Configuration Points:**
- `AutoApplyTransactions()` — Wolverine calls `SaveChangesAsync()` automatically after handlers complete
- `UseDurableOutboxOnAllSendingEndpoints()` — Messages published during handler execution are stored in the outbox table
- Handler assembly must be explicitly included via `opts.Discovery.IncludeAssembly()`

---

## Entity Model Design

### Entity with Navigation Properties

```csharp
public sealed class Customer
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    // Navigation property (one-to-many)
    public ICollection<CustomerAddress> Addresses { get; private set; } = new List<CustomerAddress>();

    private Customer() { }  // Required by EF Core

    public static Customer Create(Guid id, string email, string firstName, string lastName)
    {
        return new Customer
        {
            Id = id,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public CustomerAddress AddAddress(AddressType type, string nickname, /* ... */)
    {
        var address = CustomerAddress.Create(Id, type, nickname, /* ... */);
        Addresses.Add(address);
        return address;
    }
}

public sealed class CustomerAddress
{
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }  // Foreign key
    public AddressType Type { get; private set; }
    public string Nickname { get; private set; } = string.Empty;
    public string AddressLine1 { get; private set; } = string.Empty;
    public bool IsDefault { get; private set; }
    public bool IsVerified { get; private set; }

    // Navigation property (back to parent)
    public Customer Customer { get; private set; } = null!;

    private CustomerAddress() { }  // Required by EF Core

    internal static CustomerAddress Create(Guid customerId, AddressType type, string nickname, /* ... */)
    {
        return new CustomerAddress
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            Type = type,
            Nickname = nickname,
            // ...
        };
    }

    public void SetAsDefault(bool isDefault = true) => IsDefault = isDefault;
}
```

**Key Patterns:**
- Private setters + static factory methods (`Create()`) enforce immutability
- Private parameterless constructor required by EF Core
- Navigation properties managed by EF Core (`Customer.Addresses`, `CustomerAddress.Customer`)
- Foreign key property (`CustomerId`) explicit for clarity

### DbContext Configuration

```csharp
public class CustomerIdentityDbContext : DbContext
{
    public CustomerIdentityDbContext(DbContextOptions<CustomerIdentityDbContext> options)
        : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> Addresses => Set<CustomerAddress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Email).IsRequired().HasMaxLength(256);
            entity.HasIndex(c => c.Email).IsUnique();

            // One-to-many relationship with cascade delete
            entity.HasMany(c => c.Addresses)
                .WithOne(a => a.Customer)
                .HasForeignKey(a => a.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerAddress>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Nickname).IsRequired().HasMaxLength(50);

            // Unique constraint on nickname per customer
            entity.HasIndex(a => new { a.CustomerId, a.Nickname }).IsUnique();
        });
    }
}
```

**Key Configuration:**
- Explicit foreign key relationships with `HasForeignKey()`
- Cascade delete behavior for child entities
- Unique constraints for business rules (email, nickname per customer)
- Max length constraints match database constraints

---

## Storage Operations and Side Effects

Wolverine's storage operations API allows handlers and HTTP endpoints to return side effect objects that describe persistence operations. Wolverine then applies these operations to your EF Core `DbContext` within a transaction.

### The Storage API

```csharp
using Wolverine.Persistence;

// Available operations:
Storage.Insert<T>(entity)    // Add entity (for EF Core, calls Add())
Storage.Update<T>(entity)    // Update existing entity
Storage.Store<T>(entity)     // "Upsert" — for EF Core, this is Update()
Storage.Delete<T>(entity)    // Remove entity
Storage.Nothing<T>()         // Do nothing (useful for conditional logic)
```

**⚠️ Important:** `Storage.Store()` is NOT an upsert in EF Core (unlike Marten/RavenDb). It translates to `Update()`. Use `Storage.Insert()` for new entities.

### Return Type: IStorageAction<T>

```csharp
public record CreateItem(Guid Id, string Name);

public static class CreateItemHandler
{
    // Return IStorageAction<T> for conditional logic
    public static IStorageAction<Item> Handle(
        CreateItem command,
        IProfanityDetector detector)
    {
        // Business logic decides what to do
        if (detector.HasProfanity(command.Name))
        {
            return Storage.Nothing<Item>();  // Do nothing
        }

        return Storage.Insert(new Item
        {
            Id = command.Id,
            Name = command.Name
        });
    }
}
```

**When Wolverine sees this handler, it generates code like:**
```csharp
var action = CreateItemHandler.Handle(command, detector);
if (action is Insert<Item> insert)
{
    dbContext.Items.Add(insert.Entity);
    await dbContext.SaveChangesAsync(ct);
}
```

### Return Type: Specific Storage Actions

```csharp
// You can also return concrete types directly
public static Insert<Todo> Handle(CreateTodo command)
{
    return Storage.Insert(new Todo { Id = command.Id, Name = command.Name });
}

public static Update<Todo> Handle(RenameTodo command, [Entity] Todo todo)
{
    todo.Name = command.Name;
    return Storage.Update(todo);
}

public static Delete<Todo> Handle(DeleteTodo command, [Entity] Todo todo)
{
    return Storage.Delete(todo);
}
```

### Loading Entities with [Entity] Attribute

The `[Entity]` attribute tells Wolverine to load an entity by its identity before invoking the handler.

```csharp
public record RenameTodo(Guid Id, string Name);

public static Update<Todo> Handle(
    RenameTodo command,

    // Wolverine loads this entity from DbContext by matching
    // command.Id (convention) to todo.Id
    [Entity] Todo todo)
{
    todo.Name = command.Name;
    return Storage.Update(todo);
}
```

**Convention:** By default, Wolverine looks for a member named `Id` or `{EntityName}Id` on the command. You can customize:

```csharp
// Use explicit member name
public static Update<Todo> Handle(
    RenameTodo command,
    [Entity("TodoIdentity")] Todo todo)  // Looks for command.TodoIdentity
{
    todo.Name = command.Name;
    return Storage.Update(todo);
}

// Optional loading — returns null if not found
public static IStorageAction<Todo> Handle(
    MaybeCompleteTodo command,
    [Entity(Required = false)] Todo? todo)
{
    if (todo == null) return Storage.Nothing<Todo>();

    todo.IsComplete = true;
    return Storage.Update(todo);
}
```

**How it works:** Wolverine calls `dbContext.Set<Todo>().FindAsync(command.Id)` before invoking your handler. The loaded entity is tracked by EF Core's change tracker.

### Returning Multiple Storage Actions: UnitOfWork<T>

When you need to insert, update, or delete multiple entities, use `UnitOfWork<T>`:

```csharp
public record StoreMany(string[] Adds);

public static UnitOfWork<Todo> Handle(StoreMany command)
{
    var uow = new UnitOfWork<Todo>();

    foreach (var add in command.Adds)
    {
        uow.Insert(new Todo { Id = add });
    }

    return uow;
}
```

`UnitOfWork<T>` is essentially `List<IStorageAction<T>>`. Wolverine applies all actions in order within a single transaction.

### Null Returns

Returning `null` from a storage action handler is treated as "do nothing":

```csharp
public static Insert<Todo>? Handle(ReturnNullInsert command)
{
    // Returning null is equivalent to Storage.Nothing<Todo>()
    return null;
}
```

### ⚠️ Automatic Transactional Middleware

**CRITICAL:** When a handler returns `IStorageAction<T>` or `UnitOfWork<T>`, Wolverine automatically applies transactional middleware **even without `[Transactional]` or `AutoApplyTransactions()`**. This is required because Wolverine must call `SaveChangesAsync()` to persist the operation within the same transaction as outbox/inbox message processing.

**Implication:** You don't need to explicitly configure transactions for storage action handlers — they're transactional by default.

---

## Transactional Inbox and Outbox

Wolverine's inbox and outbox provide at-least-once delivery guarantees for messages. EF Core integrates fully with this mechanism.

### How It Works

1. **Inbox:** Incoming messages are written to the `wolverine_incoming_messages` table before processing
2. **Handler execution:** Your handler runs within a transaction that spans the inbox + your DbContext
3. **Outbox:** Messages published during handler execution are written to `wolverine_outgoing_messages`
4. **Commit:** If the handler succeeds, the transaction commits (inbox message marked processed, outbox messages queued for delivery, your DbContext changes saved)
5. **Rollback:** If the handler fails, everything rolls back (inbox message reprocessed, outbox messages discarded, your DbContext changes discarded)

### Configuration

```csharp
builder.Host.UseWolverine(opts =>
{
    // Enable automatic transactions
    opts.Policies.AutoApplyTransactions();

    // Outbox for all sent messages
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    // Durable local queues
    opts.Policies.UseDurableLocalQueues();
});
```

### Publishing Messages from Handlers

```csharp
public static async Task<(Customer, CustomerCreated)> Handle(
    CreateCustomer command,
    CustomerIdentityDbContext dbContext,
    CancellationToken ct)
{
    var customer = Customer.Create(
        Guid.CreateVersion7(),
        command.Email,
        command.FirstName,
        command.LastName);

    dbContext.Customers.Add(customer);
    await dbContext.SaveChangesAsync(ct);

    // This message is written to the outbox table
    var customerCreated = new CustomerCreated(customer.Id, customer.Email);

    // Return tuple — Wolverine publishes CustomerCreated via outbox
    return (customer, customerCreated);
}
```

**Transaction Boundary:** The `SaveChangesAsync()`, inbox update, and outbox write all happen in one transaction. If any part fails, everything rolls back.

---

## Wolverine Handler Patterns

### Compound Handler with Before() Validation

```csharp
public sealed record AddAddress(
    Guid CustomerId,
    AddressType Type,
    string Nickname,
    string AddressLine1,
    /* ... */);

public static class AddAddressHandler
{
    // Before() runs first — validation logic
    public static async Task<ProblemDetails> Before(
        AddAddress command,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        // Check if customer exists
        var customerExists = await dbContext.Customers
            .AsNoTracking()
            .AnyAsync(c => c.Id == command.CustomerId, ct);

        if (!customerExists)
            return new ProblemDetails
            {
                Detail = "Customer not found",
                Status = 404
            };

        // Check nickname uniqueness
        var nicknameExists = await dbContext.Addresses
            .AsNoTracking()
            .AnyAsync(a => a.CustomerId == command.CustomerId
                        && a.Nickname == command.Nickname, ct);

        if (nicknameExists)
            return new ProblemDetails
            {
                Detail = $"Address with nickname '{command.Nickname}' already exists",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/customers/{customerId}/addresses")]
    public static async Task<CreationResponse<Guid>> Handle(
        AddAddress command,
        CustomerIdentityDbContext dbContext,
        IAddressVerificationService verificationService,
        CancellationToken ct)
    {
        // Verify address via external service
        var verificationResult = await verificationService.VerifyAsync(/* ... */);

        // Create address entity
        var address = CustomerAddress.Create(
            command.CustomerId,
            command.Type,
            command.Nickname,
            /* ... */,
            isVerified: verificationResult.Status is VerificationStatus.Verified);

        // Handle default address logic
        if (command.IsDefault)
        {
            var existingDefaults = await dbContext.Addresses
                .Where(a => a.CustomerId == command.CustomerId && a.IsDefault)
                .ToListAsync(ct);

            foreach (var existingDefault in existingDefaults)
            {
                existingDefault.SetAsDefault(false);
            }

            address.SetAsDefault();
        }

        dbContext.Addresses.Add(address);
        await dbContext.SaveChangesAsync(ct);

        return new CreationResponse<Guid>(
            $"/api/customers/{command.CustomerId}/addresses/{address.Id}",
            address.Id);
    }
}
```

**Compound Handler Lifecycle:**
1. `Before()` — Validation (can return `ProblemDetails` to short-circuit)
2. `Validate()` — FluentValidation (if configured)
3. `Load()` — Load aggregates (if using `[ReadAggregate]` / `[WriteAggregate]`)
4. `Handle()` — Main business logic

### Query Handler with AsNoTracking()

```csharp
[WolverineGet("/api/customers/{customerId}/addresses")]
public static async Task<List<AddressSummary>> GetCustomerAddresses(
    Guid customerId,
    AddressType? type,
    CustomerIdentityDbContext dbContext,
    CancellationToken ct)
{
    var query = dbContext.Addresses
        .AsNoTracking()  // Read-only — no change tracking overhead
        .Where(a => a.CustomerId == customerId);

    if (type.HasValue)
        query = query.Where(a => a.Type == type.Value);

    return await query
        .OrderByDescending(a => a.IsDefault)
        .ThenBy(a => a.Nickname)
        .Select(a => new AddressSummary(
            a.Id,
            a.Nickname,
            $"{a.AddressLine1}, {a.City}, {a.PostalCode}",
            a.IsDefault,
            a.IsVerified))
        .ToListAsync(ct);
}
```

**AsNoTracking() Benefits:**
- Reduces memory overhead (no change tracking)
- Faster queries (EF Core skips snapshot generation)
- Prevents accidental mutations of query results
- Use for read-only queries that won't be modified

### HTTP Endpoint with Route Parameter Binding

```csharp
// ✅ Correct: Route parameter binds directly to method parameter
[WolverineGet("/api/customers/{customerId}")]
public static async Task<IResult> GetCustomer(
    Guid customerId,  // Binds from route parameter
    CustomerIdentityDbContext dbContext,
    CancellationToken ct)
{
    var customer = await dbContext.Customers
        .AsNoTracking()
        .Where(c => c.Id == customerId)
        .Select(c => new CustomerResponse(
            c.Id,
            c.Email,
            c.FirstName,
            c.LastName,
            c.CreatedAt))
        .FirstOrDefaultAsync(ct);

    return customer is null ? Results.NotFound() : Results.Ok(customer);
}
```

**Rule:** When using `{parameterName}` in route templates, method parameters should match the route parameter type directly. Query objects are for POST/PUT bodies, not GET routes with path parameters.

---

## Multi-Tenancy with EF Core

Wolverine provides first-class support for multi-tenant EF Core applications where different tenants use different databases.

### Setup with PostgreSQL (Wolverine-Managed)

```csharp
var builder = Host.CreateApplicationBuilder();
var configuration = builder.Configuration;

builder.UseWolverine(opts =>
{
    // Main database for messaging persistence
    opts.PersistMessagesWithPostgresql(configuration.GetConnectionString("main"))

        // Register static tenants at bootstrapping
        .RegisterStaticTenants(tenants =>
        {
            tenants.Register("tenant1", configuration.GetConnectionString("tenant1"));
            tenants.Register("tenant2", configuration.GetConnectionString("tenant2"));
            tenants.Register("tenant3", configuration.GetConnectionString("tenant3"));
        });

    // Register DbContext with multi-tenancy
    opts.Services.AddDbContextWithWolverineManagedMultiTenancy<ItemsDbContext>(
        (builder, connectionString, _) =>
        {
            builder.UseNpgsql(connectionString.Value,
                b => b.MigrationsAssembly("MultiTenantedEfCoreWithPostgreSQL"));
        },
        AutoCreate.CreateOrUpdate);
});
```

### Setup with SQL Server (Wolverine-Managed)

```csharp
builder.UseWolverine(opts =>
{
    opts.PersistMessagesWithSqlServer(configuration.GetConnectionString("main"))
        .RegisterStaticTenants(tenants =>
        {
            tenants.Register("tenant1", configuration.GetConnectionString("tenant1"));
            tenants.Register("tenant2", configuration.GetConnectionString("tenant2"));
        });

    opts.Services.AddDbContextWithWolverineManagedMultiTenancy<OrdersDbContext>(
        (builder, connectionString, _) =>
        {
            builder.UseSqlServer(connectionString.Value,
                b => b.MigrationsAssembly("MultiTenantedEfCoreWithSqlServer"));
        },
        AutoCreate.CreateOrUpdate);
});
```

**Key Points:**
- Wolverine manages separate inbox/outbox for each tenant database
- Transactional middleware is multi-tenant aware
- Storage actions (`[Entity]`, `Storage.Insert()`, etc.) respect multi-tenancy
- Tenant ID detection for HTTP uses [Wolverine's multi-tenancy support](https://wolverine.netlify.app/guide/http/multi-tenancy.html)

### Combining with Marten-Managed Multi-Tenancy

If you're already using Marten with multi-tenancy, EF Core can ride on Marten's tenant configuration:

```csharp
opts.Services.AddMarten(m =>
{
    m.MultiTenantedDatabases(x =>
    {
        x.AddSingleTenantDatabase(tenant1ConnectionString, "red");
        x.AddSingleTenantDatabase(tenant2ConnectionString, "blue");
        x.AddSingleTenantDatabase(tenant3ConnectionString, "green");
    });
}).IntegrateWithWolverine(x =>
{
    x.MainDatabaseConnectionString = Servers.PostgresConnectionString;
});

// EF Core uses Marten's tenant configuration
opts.Services.AddDbContextWithWolverineManagedMultiTenancyByDbDataSource<ItemsDbContext>(
    (builder, dataSource, _) =>
    {
        builder.UseNpgsql(dataSource,
            b => b.MigrationsAssembly("MultiTenantedEfCoreWithPostgreSQL"));
    },
    AutoCreate.CreateOrUpdate);
```

**Use Case:** You're using Marten for event sourcing, EF Core for flat table projections. Both share the same tenant databases.

### Publishing from Outside Handlers: IDbContextOutboxFactory

When you need to publish messages from code that's not a Wolverine handler or HTTP endpoint (e.g., background jobs, legacy code), use `IDbContextOutboxFactory`:

```csharp
public class MyLegacyService
{
    private readonly IDbContextOutboxFactory _factory;

    public MyLegacyService(IDbContextOutboxFactory factory)
    {
        _factory = factory;
    }

    public async Task ProcessItemAsync(
        CreateItem command,
        TenantId tenantId,
        CancellationToken ct)
    {
        // Get an EF Core DbContext wrapped in Wolverine's outbox
        var outbox = await _factory.CreateForTenantAsync<ItemsDbContext>(
            tenantId.Value, ct);

        var item = new Item { Name = command.Name, Id = CombGuidIdGeneration.NewGuid() };
        outbox.DbContext.Items.Add(item);

        // Message doesn't get sent until transaction succeeds
        await outbox.PublishAsync(new ItemCreated { Id = item.Id });

        // Save and commit — then flush outgoing messages
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
    }
}
```

**Key Points:**
- `IDbContextOutboxFactory` provides transactional outbox outside of Wolverine's middleware
- Works with both multi-tenant and single-tenant configurations
- Useful for transforming existing ASP.NET Core apps to use Wolverine messaging incrementally

---

## Saga Storage with EF Core

Wolverine sagas can use EF Core for persistence. Configure saga document storage in `DbContext`:

```csharp
public class OrderSagaDbContext : DbContext
{
    public DbSet<OrderSaga> OrderSagas => Set<OrderSaga>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderSaga>(entity =>
        {
            entity.HasKey(s => s.Id);

            // Enable optimistic concurrency via numeric revision
            entity.Property(s => s.Version)
                .IsConcurrencyToken()
                .ValueGeneratedOnAddOrUpdate();
        });
    }
}
```

**Saga Configuration:**
```csharp
builder.Host.UseWolverine(opts =>
{
    // Sagas using EF Core must configure numeric revisions
    opts.Discovery.IncludeAssembly(typeof(OrderSaga).Assembly);
    opts.Policies.AutoApplyTransactions();
});
```

**Saga Implementation:**
```csharp
public class OrderSaga : Saga
{
    public Guid Id { get; set; }
    public int Version { get; set; }  // Optimistic concurrency
    public string Status { get; set; } = "Pending";

    public void Handle(OrderPlaced message)
    {
        Id = message.OrderId;
        Status = "Processing";
    }

    public PaymentRequested Handle(PaymentAuthorized message)
    {
        Status = "Paid";
        return new PaymentRequested(Id, message.Amount);
    }

    public void Handle(OrderCompleted message)
    {
        Status = "Complete";
        MarkCompleted();  // Required to clean up saga
    }
}
```

**Reference:** See [docs/skills/wolverine-sagas.md](./wolverine-sagas.md) for comprehensive saga patterns.

---

## Publishing Domain Events

Domain events can be published from EF Core entities through Wolverine. Two patterns:

### Pattern 1: Return Events from Handlers

```csharp
public static (Customer, CustomerCreated) Handle(
    CreateCustomer command,
    CustomerIdentityDbContext dbContext)
{
    var customer = Customer.Create(
        Guid.CreateVersion7(),
        command.Email,
        command.FirstName,
        command.LastName);

    dbContext.Customers.Add(customer);

    var customerCreated = new CustomerCreated(customer.Id, customer.Email);

    // Tuple return — Wolverine publishes CustomerCreated
    return (customer, customerCreated);
}
```

### Pattern 2: Collect Events on Entities

```csharp
public sealed class Order
{
    private readonly List<object> _domainEvents = new();
    public IReadOnlyList<object> DomainEvents => _domainEvents;

    public void Submit()
    {
        Status = OrderStatus.Submitted;
        _domainEvents.Add(new OrderSubmitted(Id, CustomerId));
    }
}

// Handler publishes collected events
public static object[] Handle(SubmitOrder command, [Entity] Order order)
{
    order.Submit();
    return order.DomainEvents.ToArray();
}
```

**Key Points:**
- Events are written to the outbox table (transactional)
- Events are published after `SaveChangesAsync()` succeeds
- If transaction rolls back, events are discarded

---

## Database Migrations

EF Core migrations handle schema evolution.

### Creating Migrations

```bash
# Create initial migration
dotnet ef migrations add InitialCreate \
    --project "src/Customer Identity/Customers"

# Apply migrations
dotnet ef database update \
    --project "src/Customer Identity/Customers"

# Add new migration when schema changes
dotnet ef migrations add AddLastUsedAtColumn \
    --project "src/Customer Identity/Customers"
```

### Applying Migrations on Startup (Development)

```csharp
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CustomerIdentityDbContext>();
    await dbContext.Database.MigrateAsync();
}
```

**⚠️ Production:** Use a dedicated migration tool (e.g., `dotnet ef database update` in CI/CD) rather than applying migrations on app startup. This prevents race conditions when scaling horizontally.

### Migration Files

Each migration generates three files:
1. `YYYYMMDDHHMMSS_MigrationName.cs` — Migration logic (Up/Down methods)
2. `YYYYMMDDHHMMSS_MigrationName.Designer.cs` — Metadata (auto-generated, don't edit)
3. `DbContextModelSnapshot.cs` — Current model state (auto-generated, don't edit)

**Multiple Migrations:** You can stack migrations (e.g., `InitialCreate`, `AddTerminationReason`). EF Core applies them in order.

---

## Testing with EF Core + Wolverine

### Integration Test Fixture with TestContainers

```csharp
public class CustomerIdentityTestFixture : IAsyncLifetime
{
    private IAlbaHost _host = null!;
    private PostgreSqlContainer _postgres = null!;

    public IAlbaHost Host => _host;

    public async Task InitializeAsync()
    {
        // Start PostgreSQL container
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .Build();

        await _postgres.StartAsync();

        // Create Alba host with test-specific DbContext
        _host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove production DbContext registration
                services.RemoveAll<DbContextOptions<CustomerIdentityDbContext>>();

                // Register test DbContext pointing to container
                services.AddDbContext<CustomerIdentityDbContext>(options =>
                    options.UseNpgsql(_postgres.GetConnectionString()));
            });
        });

        // Run migrations
        using var scope = _host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CustomerIdentityDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
```

### Integration Test Example

```csharp
public class AddAddressTests : IClassFixture<CustomerIdentityTestFixture>
{
    private readonly CustomerIdentityTestFixture _fixture;

    public AddAddressTests(CustomerIdentityTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task adding_address_returns_201_and_location_header()
    {
        // Arrange: Create customer first
        var createCustomer = new CreateCustomer(
            "alice@example.com",
            "Alice",
            "Smith");

        var createResult = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(createCustomer).ToUrl("/api/customers");
            s.StatusCodeShouldBe(201);
        });

        var customerId = createResult.ReadAsJson<CreateCustomerResponse>()!.CustomerId;

        // Act: Add address
        var addAddress = new AddAddress(
            customerId,
            AddressType.Shipping,
            "Home",
            "123 Main St",
            null,
            "Springfield",
            "IL",
            "62701",
            "US",
            IsDefault: true);

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(addAddress).ToUrl($"/api/customers/{customerId}/addresses");
            s.StatusCodeShouldBe(201);
            s.Header("Location").ShouldNotBeNull();
        });

        // Assert: Verify address was created
        var location = result.Context.Response.Headers.Location!.ToString();

        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url(location);
            s.StatusCodeShouldBe(200);
        });
    }
}
```

### Unit Testing Storage Side Effects

```csharp
public class CreateItemHandlerTests
{
    [Fact]
    public void handle_returns_insert_action_when_name_is_valid()
    {
        // Arrange
        var command = new CreateItem(Guid.NewGuid(), "Valid Item Name");
        var detector = Substitute.For<IProfanityDetector>();
        detector.HasProfanity("Valid Item Name").Returns(false);

        // Act
        var result = CreateItemHandler.Handle(command, detector);

        // Assert
        result.ShouldBeOfType<Insert<Item>>();
        var insert = (Insert<Item>)result;
        insert.Entity.Id.ShouldBe(command.Id);
        insert.Entity.Name.ShouldBe(command.Name);
    }

    [Fact]
    public void handle_returns_nothing_when_name_has_profanity()
    {
        // Arrange
        var command = new CreateItem(Guid.NewGuid(), "Profane Name");
        var detector = Substitute.For<IProfanityDetector>();
        detector.HasProfanity("Profane Name").Returns(true);

        // Act
        var result = CreateItemHandler.Handle(command, detector);

        // Assert
        result.ShouldBeOfType<Nothing<Item>>();
    }
}
```

**Key Testing Patterns:**
- Use Alba for HTTP integration tests
- Use TestContainers for real PostgreSQL databases (not mocks)
- Storage side effects are trivial to unit test (no `DbContext` mocking needed)
- Separate unit tests (pure logic) from integration tests (full stack)

**Reference:** See [docs/skills/testcontainers-integration-tests.md](./testcontainers-integration-tests.md) for comprehensive testing patterns.

---

## Lessons Learned from CritterSupply

These lessons come from actual bugs, code reviews, and architectural adjustments during CritterSupply development (Cycles 13, 22, 26, 27).

### L1: Enum Arrays Cannot Be Parameterized in LINQ

**Problem:** Npgsql (PostgreSQL provider for EF Core) cannot serialize C# enum arrays as LINQ query parameters.

```csharp
// ❌ Does NOT work — runtime exception
var activeStatuses = new[] { Status.Draft, Status.Submitted, Status.NeedsMoreInfo };
var query = dbContext.ChangeRequests
    .Where(r => activeStatuses.Contains(r.Status));
```

**Error:**
```
System.InvalidCastException: Writing values of 'Status[]' is not supported
for parameters having NpgsqlDbType '-2147483639'.
```

**Fix:** Use explicit OR conditions in LINQ; use arrays only for in-memory checks:

```csharp
// ✅ Explicit OR conditions for LINQ queries
var query = dbContext.ChangeRequests
    .Where(r => r.Status == Status.Draft ||
                r.Status == Status.Submitted ||
                r.Status == Status.NeedsMoreInfo);

// ✅ Pattern expression for in-memory checks (O(1), no allocation)
public bool IsActive => Status is Draft or Submitted or NeedsMoreInfo;

// ✅ Static array for documentation only
public static readonly Status[] ActiveStatuses =
    [Draft, Submitted, NeedsMoreInfo];
```

**Source:** Cycle 22 retrospective, L1

---

### L2: Entity State vs Command Value Bug

**Problem:** After mutating entity state, handlers that re-read from entity state for outgoing messages can send stale data.

```csharp
// ❌ Bug: BuildMessage() reads request.AdditionalNotes (stale)
public static object Handle(
    ProvideAdditionalInfo command,
    [Entity] ChangeRequest request)
{
    request.InfoResponses.Add(new VendorInfoResponse(command.Response, DateTime.UtcNow));

    // BUG: BuildCatalogMessage() reads request.AdditionalNotes
    // But request.AdditionalNotes is the OLD draft notes, not command.Response!
    return BuildCatalogMessage(request);
}

private static CatalogUpdateMessage BuildCatalogMessage(ChangeRequest request)
{
    return new CatalogUpdateMessage(
        request.Sku,
        request.AdditionalNotes);  // WRONG — this is the old notes!
}
```

**Fix:** Pass transient command values explicitly to helper methods:

```csharp
// ✅ Pass command.Response explicitly
public static object Handle(
    ProvideAdditionalInfo command,
    [Entity] ChangeRequest request)
{
    request.InfoResponses.Add(new VendorInfoResponse(command.Response, DateTime.UtcNow));

    // Pass transient value explicitly
    return BuildCatalogMessage(request, command.Response);
}

private static CatalogUpdateMessage BuildCatalogMessage(
    ChangeRequest request,
    string vendorResponse)  // Explicit parameter — correct
{
    return new CatalogUpdateMessage(request.Sku, vendorResponse);
}
```

**Rule:** After mutating entity state, don't re-read from entity for outgoing messages. Pass transient command values explicitly.

**Source:** Cycle 22 retrospective, L2

---

### L3: AsNoTracking() for Read-Only Queries

**Pattern:** Use `.AsNoTracking()` for queries that won't be modified.

```csharp
// ✅ AsNoTracking() for snapshot queries
[WolverineGet("/api/customers/addresses/{addressId:guid}")]
public static async Task<AddressSnapshot?> GetAddressSnapshot(
    Guid addressId,
    CustomerIdentityDbContext dbContext,
    CancellationToken ct)
{
    var address = await dbContext.Addresses
        .AsNoTracking()  // Critical: read-only, no tracking overhead
        .FirstOrDefaultAsync(a => a.Id == addressId, ct);

    if (address is null) return null;

    return new AddressSnapshot(
        address.AddressLine1,
        address.City,
        address.PostalCode);
}
```

**Benefits:**
- Reduces memory overhead (no change tracking snapshots)
- Faster queries (EF Core skips snapshot generation)
- Prevents accidental mutations of detached entities
- Ideal for DTOs and snapshots consumed by other BCs

**Source:** Customer Identity workflows documentation

---

### L4: Race Conditions in HTTP-Based Integration Tests

**Problem:** HTTP POST → immediate GET verification fails due to eventual consistency.

```csharp
// ❌ Race condition — SaveChangesAsync() is async after HTTP response
await _fixture.Host.Scenario(s =>
{
    s.Post.Json(command).ToUrl("/api/returns");
    s.StatusCodeShouldBe(202);
});

// This can fail — aggregate may not be persisted yet
var returnEntity = await LoadReturnFromEventStore(command.ReturnId);
returnEntity.Status.ShouldBe(ReturnStatus.Submitted);
```

**Root Cause:** Wolverine's `AutoApplyTransactions()` commits asynchronously after the HTTP response is sent. The test races against the commit.

**Fix:** Use direct Wolverine command invocation (`ExecuteAndWaitAsync()`) instead of HTTP:

```csharp
// ✅ ExecuteAndWaitAsync() waits for transaction to commit
await _fixture.Host.ExecuteAndWaitAsync(command);

// Now the aggregate is guaranteed to be persisted
var returnEntity = await LoadReturnFromEventStore(command.ReturnId);
returnEntity.Status.ShouldBe(ReturnStatus.Submitted);
```

**Alternative:** Query the event store directly (for event-sourced aggregates):

```csharp
// ✅ Query event store for aggregate state
using var session = _fixture.Store.LightweightSession();
var returnEntity = await session.Events.AggregateStreamAsync<Return>(command.ReturnId);
returnEntity.Status.ShouldBe(ReturnStatus.Submitted);
```

**Rule:** Test HTTP endpoints separately. Use direct command invocation for business logic tests.

**Source:** Cycle 26 retrospective, L5 (applies to EF Core as well as Marten)

---

### L5: Feature-Based Organization for EF Core BCs

**Pattern:** Organize EF Core BCs by feature, not by technical layer.

```
// ❌ Traditional layered structure (hard to navigate)
src/Vendor Identity/VendorIdentity/
├── Commands/
│   ├── CreateVendorTenant.cs
│   ├── InviteVendorUser.cs
├── Data/
│   └── VendorIdentityDbContext.cs
└── Entities/
    ├── VendorTenant.cs
    └── VendorUser.cs

// ✅ Feature-based structure (colocated files)
src/Vendor Identity/VendorIdentity/
├── TenantManagement/
│   ├── CreateVendorTenant.cs              # Command
│   ├── CreateVendorTenantValidator.cs     # Validator
│   ├── CreateVendorTenantHandler.cs       # Handler
│   └── GetVendorTenant.cs                 # Query
├── UserInvitations/
│   ├── InviteVendorUser.cs
│   ├── InviteVendorUserHandler.cs
└── Identity/
    ├── VendorIdentityDbContext.cs         # Shared infrastructure
    ├── VendorTenant.cs                    # Entity
    └── VendorUser.cs                      # Entity
```

**Benefits:**
- All files for a feature (command, validator, handler) are colocated
- Consistent with Marten-based BCs (vertical slice architecture)
- Easier navigation (IDE file trees group related files)
- Better for AI-assisted development

**Guidelines:**
- Feature folders contain commands, queries, handlers, validators
- `Identity/` folder contains shared infrastructure (DbContext, entities, enums)
- No circular dependencies between feature folders

**Reference:** [ADR 0023: Feature-Based Organization for EF Core BCs](../../decisions/0023-feature-based-organization-for-ef-core-bcs.md)

---

### L6: Foreign Key Constraints Catch Integration Bugs Early

**Pattern:** Use real foreign key constraints in the database to catch integration bugs early.

```csharp
// ❌ Stub/placeholder data bypasses referential integrity
var customerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
var cart = await shoppingClient.InitializeCart(customerId);  // Fails silently!

// ✅ Create customer first via Customer Identity BC
var createCustomerResponse = await customerIdentityClient.CreateCustomer(
    new CreateCustomerRequest("alice@example.com", "Alice", "Smith"));
var customerId = createCustomerResponse.CustomerId;

// Now foreign key constraint is satisfied
var cart = await shoppingClient.InitializeCart(customerId);
```

**Benefits:**
- Foreign key constraints enforce referential integrity at the database level
- Integration bugs surface immediately (not in production)
- Database enforces invariants (can't orphan child records)
- Fail-fast principle (catch bugs early in development)

**Rule:** Remove all stub/placeholder data early in integration testing. Let foreign key constraints validate integrations.

---

## Common Pitfalls & Solutions

### Pitfall 1: Route Parameter Binding with Query Objects

**Problem:**
```csharp
// ❌ Incorrect: Wolverine tries to resolve GetCustomer from DI container
[WolverineGet("/api/customers/{customerId}")]
public static async Task<IResult> Handle(
    GetCustomer query,  // Can't be injected!
    CustomerIdentityDbContext dbContext,
    CancellationToken ct)
{
    // ...
}
```

**Error:**
```
JasperFx.CodeGeneration.UnResolvableVariableException:
JasperFx was unable to resolve a variable of type GetCustomer
```

**Solution:**
```csharp
// ✅ Correct: Route parameter binds directly to method parameter
[WolverineGet("/api/customers/{customerId}")]
public static async Task<IResult> Handle(
    Guid customerId,  // Binds from route parameter
    CustomerIdentityDbContext dbContext,
    CancellationToken ct)
{
    var customer = await dbContext.Customers
        .AsNoTracking()
        .Where(c => c.Id == customerId)
        .Select(c => new CustomerResponse(...))
        .FirstOrDefaultAsync(ct);

    return customer is null ? Results.NotFound() : Results.Ok(customer);
}
```

**Rule:** When using `{parameterName}` in route templates, method parameters should match the route parameter type directly. Query objects are for POST/PUT bodies.

---

### Pitfall 2: Storage.Store() Is Not an Upsert in EF Core

**Problem:** `Storage.Store()` works as an upsert in Marten and RavenDb, but not in EF Core.

```csharp
// ❌ Throws exception if entity doesn't exist
public static Store<Item> Handle(UpdateItem command)
{
    return Storage.Store(new Item { Id = command.Id, Name = command.Name });
}
```

**Error:**
```
InvalidOperationException: The instance of entity type 'Item' cannot be tracked
because another instance with the same key value for {'Id'} is already being tracked.
```

**Solution:** Use `Storage.Insert()` for new entities, `Storage.Update()` for existing entities:

```csharp
// ✅ Use Insert for new entities
public static Insert<Item> Handle(CreateItem command)
{
    return Storage.Insert(new Item { Id = command.Id, Name = command.Name });
}

// ✅ Use Update for existing entities (with [Entity] to load)
public static Update<Item> Handle(UpdateItem command, [Entity] Item item)
{
    item.Name = command.Name;
    return Storage.Update(item);
}
```

**Rule:** `Storage.Store()` in EF Core translates to `Update()`, not upsert. Use `Insert()` for new entities.

---

### Pitfall 3: Forgetting to Include Handler Assembly in Discovery

**Problem:** Handlers defined in a separate assembly are not discovered by Wolverine.

```csharp
// ❌ Wolverine doesn't discover handlers in Customers assembly
builder.Host.UseWolverine(opts =>
{
    opts.Policies.AutoApplyTransactions();
});
```

**Error:** HTTP endpoints return 404, handlers don't execute.

**Solution:**
```csharp
// ✅ Explicitly include handler assembly
builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(CustomerAddress).Assembly);
    opts.Policies.AutoApplyTransactions();
});
```

**Rule:** Always explicitly include assemblies containing handlers via `opts.Discovery.IncludeAssembly()`.

---

### Pitfall 4: Not Calling MarkCompleted() in Sagas

**Problem:** Sagas that don't call `MarkCompleted()` remain in the database forever (orphaned sagas).

```csharp
// ❌ Saga never completes
public class OrderSaga : Saga
{
    public void Handle(OrderCompleted message)
    {
        Status = "Complete";
        // BUG: Forgot to call MarkCompleted()!
    }
}
```

**Solution:**
```csharp
// ✅ Always call MarkCompleted() in terminal states
public class OrderSaga : Saga
{
    public void Handle(OrderCompleted message)
    {
        Status = "Complete";
        MarkCompleted();  // Required!
    }
}
```

**Rule:** Every terminal code path in a saga must call `MarkCompleted()` to clean up the saga document.

**Reference:** See [docs/skills/wolverine-sagas.md](./wolverine-sagas.md) for comprehensive saga patterns.

---

### Pitfall 5: Not Using Optimistic Concurrency for Sagas

**Problem:** Without optimistic concurrency, concurrent saga updates can overwrite each other.

**Solution:** Configure numeric revisions for saga entities:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<OrderSaga>(entity =>
    {
        entity.Property(s => s.Version)
            .IsConcurrencyToken()
            .ValueGeneratedOnAddOrUpdate();
    });
}
```

**Rule:** Always use optimistic concurrency (row version or timestamp) for saga persistence with EF Core.

---

## Appendix: References & Further Reading

### Official Wolverine Documentation

- [Wolverine EF Core Integration](https://wolverine.netlify.app/guide/durability/efcore.html)
- [Storage Operations with EF Core](https://wolverine.netlify.app/guide/durability/efcore/operations.html)
- [Multi-Tenancy with EF Core](https://wolverine.netlify.app/guide/durability/efcore/multi-tenancy.html)
- [Storage Side Effects](https://wolverine.netlify.app/guide/handlers/side-effects.html#storage-side-effects)
- [Transactional Inbox/Outbox](https://wolverine.netlify.app/guide/durability/)

### Official EF Core Documentation

- [EF Core Documentation](https://learn.microsoft.com/en-us/ef/core/)
- [EF Core Entity Types](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types)
- [EF Core Querying](https://learn.microsoft.com/en-us/ef/core/querying/)
- [EF Core Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [EF Core Multi-Tenancy](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)

### CritterSupply Reference Documentation

**ADRs:**
- [ADR 0002: EF Core for Customer Identity](../../decisions/0002-ef-core-for-customer-identity.md)
- [ADR 0023: Feature-Based Organization for EF Core BCs](../../decisions/0023-feature-based-organization-for-ef-core-bcs.md)

**Workflow Docs:**
- [Customer Identity Workflows](../../workflows/customer-identity-workflows.md)
- [Vendor Identity Workflows](../../workflows/vendor-identity-workflows.md)

**Retrospectives:**
- [Cycle 22 Retrospective](../../planning/cycles/cycle-22-retrospective.md) — Vendor Portal (lessons L1-L12)
- [Cycle 26 Retrospective](../../planning/cycles/cycle-26-returns-bc-phase-2-retrospective.md) — Returns BC (lesson L5)

**Related Skills:**
- [wolverine-message-handlers.md](./wolverine-message-handlers.md) — Compound handlers, return patterns
- [wolverine-sagas.md](./wolverine-sagas.md) — Saga patterns, numeric revisions, MarkCompleted()
- [testcontainers-integration-tests.md](./testcontainers-integration-tests.md) — TestContainers patterns for Postgres
- [vertical-slice-organization.md](./vertical-slice-organization.md) — Feature-based folder structure

### CritterSupply Implementation Examples

**Customer Identity BC (EF Core + PostgreSQL):**
- `src/Customer Identity/Customers/AddressBook/` — Entity model, handlers, queries
- `src/Customer Identity/Customers/AddressBook/CustomerIdentityDbContext.cs` — DbContext configuration
- `src/Customer Identity/CustomerIdentity.Api/Program.cs` — Wolverine + EF Core setup
- `tests/Customer Identity/Customers.IntegrationTests/` — Alba + TestContainers integration tests

**Vendor Identity BC (EF Core + PostgreSQL + Multi-Tenant):**
- `src/Vendor Identity/VendorIdentity/TenantManagement/` — Feature folder example
- `src/Vendor Identity/VendorIdentity/Identity/VendorIdentityDbContext.cs` — DbContext with multi-tenancy
- `src/Vendor Identity/VendorIdentity.Api/Program.cs` — JWT auth + EF Core setup

---

**Document Version:** 2.0
**Last Updated:** 2026-03-14
**Authors:** Principal Architect, Product Owner
**Status:** ✅ Complete — Ready for use by human and AI developers
