# Entity Framework Core + Wolverine Integration

Patterns for using Entity Framework Core with Wolverine in CritterSupply. Customer Identity BC is the reference implementation.

## When to Use EF Core vs Marten

**Use EF Core when:**
- Traditional relational model fits naturally (Customer → Addresses with foreign keys)
- Navigation properties simplify queries
- Foreign key constraints enforce referential integrity
- Schema evolution via migrations
- Team is more familiar with EF Core
- Current state is all that matters

**Use Marten when:**
- Event sourcing is beneficial (Orders, Payments, Inventory)
- Document model fits (flexible schema, JSON storage)
- No complex relational joins needed

## Package Dependencies

```xml
<ItemGroup>
    <PackageReference Include="WolverineFx.Http.FluentValidation" />
    <PackageReference Include="WolverineFx.RabbitMQ" />
    <PackageReference Include="WolverineFx.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
</ItemGroup>
```

> **Reference:** [Wolverine EF Core Integration](https://wolverinefx.net/guide/durability/efcore.html)

## Entity Model Design

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

    public static Customer Create(string email, string firstName, string lastName)
    {
        return new Customer
        {
            Id = Guid.CreateVersion7(),
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
    public bool IsVerified { get; private set; }

    // Navigation property (back to parent)
    public Customer Customer { get; private set; } = null!;

    private CustomerAddress() { }

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

    public void MarkAsVerified() => IsVerified = true;
}
```

> **Reference:** [EF Core Entity Types](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types)

## DbContext Configuration

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

            // One-to-many relationship
            entity.HasMany(c => c.Addresses)
                .WithOne(a => a.Customer)
                .HasForeignKey(a => a.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerAddress>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Nickname).IsRequired().HasMaxLength(50);

            // Unique constraint
            entity.HasIndex(a => new { a.CustomerId, a.Nickname }).IsUnique();
        });
    }
}
```

## Program.cs Configuration

```csharp
// Configure EF Core with Postgres
builder.Services.AddDbContext<CustomerIdentityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("CustomerIdentity")));

// Wolverine configuration
builder.Host.UseWolverine(opts =>
{
    opts.Policies.AutoApplyTransactions();  // Auto-saves changes
    opts.Discovery.IncludeAssembly(typeof(CustomerIdentityDbContext).Assembly);
});
```

## Wolverine Handler with EF Core

```csharp
public sealed record AddAddress(
    Guid CustomerId,
    AddressType Type,
    string Nickname,
    string AddressLine1,
    string City,
    string Postcode,
    string Country)
{
    public class AddAddressValidator : AbstractValidator<AddAddress>
    {
        public AddAddressValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.Nickname).NotEmpty().MaximumLength(50);
            RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(200);
        }
    }
}

public static class AddAddressHandler
{
    public static async Task<ProblemDetails> Before(
        AddAddress command,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        var customer = await dbContext.Customers
            .Include(c => c.Addresses)  // Eager load navigation property
            .FirstOrDefaultAsync(c => c.Id == command.CustomerId, ct);

        if (customer is null)
            return new ProblemDetails { Detail = "Customer not found", Status = 404 };

        if (customer.Addresses.Any(a => a.Nickname == command.Nickname))
            return new ProblemDetails { Detail = "Nickname already exists", Status = 409 };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/customers/{customerId}/addresses")]
    public static async Task<CreationResponse> Handle(
        AddAddress command,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        var customer = await dbContext.Customers
            .Include(c => c.Addresses)
            .FirstAsync(c => c.Id == command.CustomerId, ct);

        var address = customer.AddAddress(
            command.Type,
            command.Nickname,
            command.AddressLine1,
            command.City,
            command.Postcode,
            command.Country);

        await dbContext.SaveChangesAsync(ct);

        return new CreationResponse($"/api/customers/{command.CustomerId}/addresses/{address.Id}");
    }
}
```

## Query with Navigation Properties

```csharp
public static class GetCustomerAddressesHandler
{
    [WolverineGet("/api/customers/{customerId}/addresses")]
    public static async Task<List<AddressSummary>> Handle(
        Guid customerId,
        AddressType? type,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        var query = dbContext.Addresses
            .Where(a => a.CustomerId == customerId);

        if (type.HasValue)
            query = query.Where(a => a.Type == type.Value);

        return await query
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Nickname)
            .Select(a => new AddressSummary(
                a.Id,
                a.Nickname,
                $"{a.AddressLine1}, {a.City}, {a.Postcode}",
                a.IsDefault,
                a.IsVerified))
            .ToListAsync(ct);
    }
}
```

> **Reference:** [EF Core Querying](https://learn.microsoft.com/en-us/ef/core/querying/)

## Migrations

```bash
# Create initial migration
dotnet ef migrations add InitialCreate --project src/Customer\ Identity/Customers

# Apply migrations
dotnet ef database update --project src/Customer\ Identity/Customers

# Add new migration when schema changes
dotnet ef migrations add AddLastUsedAtColumn --project src/Customer\ Identity/Customers
```

> **Reference:** [EF Core Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)

## Testing with Alba + TestContainers

```csharp
public class CustomerIdentityTestFixture : IAsyncLifetime
{
    private IAlbaHost _host = null!;
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .Build();

        await _postgres.StartAsync();

        _host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CustomerIdentityDbContext>>();
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

## Key Takeaways

1. **DbContext Injection** — Wolverine injects `DbContext` into handlers like any other dependency
2. **Navigation Properties** — Use `Include()` for eager loading
3. **Change Tracking** — EF Core tracks entity changes automatically
4. **SaveChangesAsync** — Call explicitly (or use `AutoApplyTransactions`)
5. **Migrations** — Use EF Core migrations for schema evolution
6. **Foreign Keys** — Database-level referential integrity
