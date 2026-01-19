# Development Guidelines for CritterSupply with Claude

This repository contains production-ready skills designed specifically for use with Claude, Claude Agent, Claude Code, and other uses of "Claude" that aim to assist with software development.

These skills range from specific modern C# and .NET practices to idioms established in frameworks and libraries such as Wolverine and Marten to build robust event-driven systems.

> **Universal Applicability**: While explained using the C# programming language and .NET platform, these patterns apply to any object-oriented programming language (Java TypeScript, Python, etc.), as well as borrow concepts from functional programming languages (F#, Clojure, Elixir, etc.). Concepts, ideas, strategies, and tactics are influenced by pragmatic use of Domain-Driven Design (DDD) and Command Query Responsibility Segregation (CQRS), which are language-agnostic.

## Repository Purpose

This repository demonstrates how to build robust, production-ready, event-driven systems using a realistic e-commerce domain.

It also serves as a reference architecture for idiomatically leveraging the "Critter Stack"—[Wolverine](https://github.com/JasperFx/wolverine) and [Marten](https://github.com/JasperFx/marten)—to supercharge your .NET development. These tools just get out of your way so you can focus on the actual business problems at hand.

### Ecommerce

CritterSupply is a fictional pet supply retailer—the name a playful nod to the Critter Stack powering it, with the tagline "Stocked for every season."

E-commerce was chosen as the domain partly from the maintainer's industry experience, but more importantly because it's a domain most developers intuitively understand. Everyone has placed an order online. That familiarity lets us focus on *how* the system is built rather than getting bogged down explaining *what* it does.

#### Bounded Contexts, Value-Aligned Work, and Integration

The CritterSupply system is composed of multiple bounded contexts ("BC"), sometimes called subdomains in other organizations, each responsible for a specific area of business functionality. These contexts communicate via asynchronous messaging to maintain loose coupling and high cohesion. You can think of each BC as a different department or team of people, all working together to run the overall business. These value-aligned teams:
- Own their domain/context end-to-end
- Minimize handoffs
- Maximize flow
- Have a narrow, well-defined cognitive load

**Why mention these concepts from Domain-Driven Design and Team Topologies?** Because the architecture and code patterns in this repository are designed to support these principles. The goal is to show how to build systems that align with modern software development practices, not just how to write code.

For details on the specific bounded contexts, their responsibilities, invariants, and integration patterns within CritterSupply, see **[CONTEXTS.md](CONTEXTS.md)**. That document provides the domain-level architecture and business workflow definitions that drive the technical implementations described in this file.

### CONTEXTS.md as Architectural North Star

**IMPORTANT:** `CONTEXTS.md` is the **architectural source of truth** for this system. When implementing integrations between bounded contexts:

1. **Always consult CONTEXTS.md first** - It defines what messages each BC receives and publishes
2. **Implementation follows specification** - Code should match the integration contracts defined in CONTEXTS.md
3. **Orchestration vs. Choreography** - CONTEXTS.md specifies which pattern to use (e.g., Orders orchestrates, other BCs react)
4. **Update CONTEXTS.md when architecture changes** - Keep it current; it's living documentation, not a one-time artifact

If there's a discrepancy between code and CONTEXTS.md, **CONTEXTS.md wins** unless there's a deliberate architectural decision to update the specification.

## What This Repository Provides

This repository provides a reference architecture and code examples, focusing on concepts, principles, and idioms such as:

- **Event-Driven Architecture (EDA)** patterns (language-agnostic principles)
- **Event Sourcing** data persistence techniques (demonstrated with the Marten library and Postgres)
- **CQRS** command and query segregation (demonstrated with Wolverine's command execution capabilities)
- **Low Ceremony Railway Oriented Programming** approach for flow control by chaining together functions (through Wolverine's support of validation and pre-loading of data prior to message handling )
- **Pure Functions** to encapsulate business or workflow logic whenever possible (demonstrated through the A-Frame Architecture metaphor)
- **A-Frame Architecture** through functional decomposition rather than excessive abstractions and layers
- **Domain-Driven Design (DDD)** strategies applied where applicable, urging pragmatism and low ceremony tactically
- **BDD-Style Testing** through Alba, Wolverine, and Marten idioms, focusing largely on integration over unit tests

## Quick References

A list of tools, technologies, techniques, and other details to help define how this project is constructed.

### Preferred Tools:

- **Language**: C# 14+ (.NET 10+)
- **Testing**: xUnit, Testcontainers, Alba, Shouldly
- **State Management**: Prefer immutable patterns and records
- **Validation**: FluentValidation
- **Serialization**: System.Text.Json
- **Database**: Postgres
- **Event Sourcing**: Marten 8+
- **Document Store**: Marten 8+
- **Command Execution**: Wolverine 5+
- **Event-Driven Framework**: Wolverine 5+
- **Messaging Tool**: RabbitMQ as the message-broker using the AMQP to communicate across bounded contexts, value streams, etc.
- **Collections**: Use `IReadOnlyList<T>`, `IReadOnlyCollection<T>` for immutability (see Collection Patterns below)

### Prefer Pure Functions for Business Logic

Use "pure functions" whenever possible. That goes for the write models that constitute aggregates which write events. That goes for the message handlers, AKA the "deciders", for any business logic.

More details and examples are provided in the sections below.

#### Recommended Reading

On the note of using Wolverine, the framework and its accompanying libraries (such as `Wolverine.Http`), has a lot of specific functionality to move infrastructure concerns out of the way of your business or workflow logic. For insight and tips on how to create pure functions for your Wolverine message handlers or HTTP endpoints, check out the following articles:

- [A-Frame Architecture with Wolverine](https://jeremydmiller.com/2023/07/19/a-frame-architecture-with-wolverine/)
- [Testing Without Mocks: A Pattern Language by Jim Shore](https://www.jamesshore.com/v2/projects/nullables/testing-without-mocks)
- [Compound Handlers in Wolverine](https://jeremydmiller.com/2023/03/07/compound-handlers-in-wolverine/)
- [Isolating Side Effects from Wolverine Handlers](https://jeremydmiller.com/2023/04/24/isolating-side-effects-from-wolverine-handlers/)


## C# and .NET Guidelines

### Solution and Project Organization

This repository follows a **bounded context aligned** organization strategy where physical folder structure, .NET solution structure, and domain boundaries are deliberately aligned.

#### Physical Folder Structure

```
src/
  <Bounded Context Name>/             # Physical folder named after the bounded context (e.g., "Customer Identity", "Payment Processing")
    <ProjectName>/                    # The core project containing domain logic, handlers, events, commands, queries
                                      # Uses Microsoft.NET.Sdk.Web for self-contained BCs with HTTP endpoints
    <ProjectName>.Api/                # [Optional] Separate API project if BC has complex hosting requirements
                                      # Most BCs combine API and domain logic in a single project
tests/
  <Bounded Context Name>/             # Mirrors the src/ structure for test organization
    <ProjectName>.IntegrationTests/   # Integration tests using Alba, Testcontainers, xUnit
    <ProjectName>.UnitTests/          # [Optional] Unit tests for complex domain logic
```

**Real Examples from CritterSupply:**

```
src/
  Customer Identity/
    Customers/                        # Single project (Web SDK) with domain + HTTP endpoints
  Order Management/
    Orders/                           # Core domain logic
    Orders.Api/                       # HTTP hosting (this BC uses separate projects)
  Payment Processing/
    Payments/
    Payments.Api/
  Shared/
    Messages.Contracts/               # Shared integration message contracts

tests/
  Customer Identity/
    Customers.Api.IntegrationTests/
  Order Management/
    Orders.Api.IntegrationTests/
    Orders.UnitTests/
```

#### .NET Solution File Organization (`.slnx`)

The solution file uses **solution folders** to mirror the physical folder structure and bounded context boundaries. This provides IDE organization that matches the filesystem and domain model.

**Structure:**
- Each bounded context gets its own solution folder (e.g., `/Customer Identity/`, `/Payment Processing/`)
- Solution folders contain all projects for that BC (domain, API, tests)
- Shared projects live in a `/Shared/` solution folder
- Solution items (docs, config) live in `/.SolutionItems/`

**Example from CritterSupply.slnx:**

```xml
<Solution>
  <Folder Name="/.SolutionItems/">
    <File Path="CLAUDE.md" />
    <File Path="CONTEXTS.md" />
    <File Path="DEVPROGRESS.md" />
  </Folder>

  <Folder Name="/Customer Identity/">
    <Project Path="src/Customer Identity/CustomerIdentity.Api/CustomerIdentity.Api.csproj" />
    <Project Path="src/Customer Identity/Customers/CustomerIdentity.csproj" />
    <Project Path="tests/Customer Identity/CustomerIdentity.Api.IntegrationTests/CustomerIdentity.Api.IntegrationTests.csproj" />
  </Folder>

  <Folder Name="/Order Management/">
    <Project Path="src/Order Management/Orders/Orders.csproj" />
    <Project Path="src/Order Management/Orders.Api/Orders.Api.csproj" />
    <Project Path="tests/Order Management/Orders.Api.IntegrationTests/Orders.Api.IntegrationTests.csproj" />
    <Project Path="tests/Order Management/Orders.UnitTests/Orders.UnitTests.csproj" />
  </Folder>

  <Folder Name="/Shared/">
    <Project Path="src/Shared/Messages.Contracts/Messages.Contracts.csproj" />
  </Folder>
</Solution>
```

**Key Benefits:**

1. **Bounded Context Visibility**: IDE tree structure makes BC boundaries explicit
2. **Team Alignment**: Each BC folder represents a potential team's ownership area
3. **Discoverability**: New developers can see the system's bounded contexts immediately
4. **Consistency**: Physical folders, solution folders, and domain concepts all align

**When Adding a New Bounded Context:**

1. Create physical folder in `src/<BC Name>/` and `tests/<BC Name>/`
2. Create projects following established naming patterns
3. Add solution folder in `CritterSupply.slnx` matching the physical folder name
4. Add all BC projects to the solution folder
5. Document the BC in `CONTEXTS.md`

**Note on Naming:**
- Prefer descriptive BC names that convey purpose (e.g., "Customer Identity" over "Customer Management")
- Avoid redundant suffixes like "Management" unless they add clarity
- Physical folder names, solution folder names, and CONTEXTS.md section headings should match

### Project Structure (Within a Bounded Context)

```
src/
  <BC Name>/
    <ProjectName>/                    # Single project approach (preferred for most BCs)
      Program.cs                      # Hosting configuration (Wolverine, Marten, HTTP)
      <Subdomain>/                    # Organize by subdomain or feature area
        <Feature>.cs                  # Commands, queries, handlers, validators colocated
        <Entity>.cs                   # Domain models
```

#### Avoid Having Folders Based on Technical Features

Inside our projects, we want to avoid creating folders (directories) based on technical feature (Entities, Models, Controllers, DTO's). Instead, create folders based on the actual business value that grouped set of code performs. Loosely following a vertical slice architecture style. A new developer should be able to look at the files/folders inside a project and understand what is is that the application does.

If there is a folder based on a technical feature, treat it as temporary and that its contents will be moved to a better-fitting namespace soon.

#### File Organization for Commands, Queries, and Handlers

In CQRS + Event Sourcing systems, commands and queries have a **1:1 relationship** with their handlers by design. To improve code comprehension and reduce cognitive load, **colocate commands/queries with their handlers AND validators in the same file**.

##### Commands and Command Handlers

Commands, their validators, and their handlers should all be in the same file. This creates a single location for understanding a complete vertical slice/workflow - from validation to execution to result.

```csharp
// File: AddItemToCart.cs
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart;

// Command definition
public sealed record AddItemToCart(
    Guid CartId,
    string Sku,
    int Quantity,
    decimal UnitPrice)
{
    public class AddItemToCartValidator : AbstractValidator<AddItemToCart>
    {
        public AddItemToCartValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        }
    }
}

// Handler for the command above
public static class AddItemToCartHandler
{
    public static ProblemDetails Before(
        AddItemToCart command,
        Cart? cart)
    {
        if (cart is null)
            return new ProblemDetails { Detail = "Cart not found", Status = 404 };

        if (cart.IsTerminal)
            return new ProblemDetails
            {
                Detail = "Cannot modify a cart that has been abandoned, cleared, or checked out",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/carts/{cartId}/items")]
    public static ItemAdded Handle(
        AddItemToCart command,
        [WriteAggregate] Cart cart)
    {
        return new ItemAdded(
            command.Sku,
            command.Quantity,
            command.UnitPrice,
            DateTimeOffset.UtcNow);
    }
}
```

##### Events

Events should remain in separate files **unless** they have associated handlers (such as Marten event subscriptions). If an event has a subscription handler, include it in the same file as the event.

```csharp
// File: ItemAdded.cs - Event only, no handler
namespace Shopping.Cart;

public sealed record ItemAdded(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    DateTimeOffset AddedAt);
```

##### Benefits of This Approach

1. **Single Location for Comprehension**: Developers can see the complete workflow (command → validation → preconditions → business logic → result) without hunting through multiple files
2. **Tight Coupling Made Explicit**: Commands, validators, and handlers are tightly coupled by design (1:1:1 relationship), so colocating them makes this coupling obvious and intentional
3. **Reduced File Hopping**: No need to navigate between `AddItemToCart.cs`, `AddItemToCartValidator.cs`, and `AddItemToCartHandler.cs` to understand the complete picture
4. **Onboarding Efficiency**: New developers can quickly understand "what happens" when a command is issued - from validation rules to execution logic

##### File Naming Convention

- **Commands**: `{CommandName}.cs` (e.g., `AddItemToCart.cs`)
- **Queries**: `{QueryName}.cs` (e.g., `GetCartById.cs`)
- **Events**: `{EventName}.cs` (e.g., `ItemAdded.cs`)

**Nested class naming conventions:**
- **Validators**: `{MessageName}Validator` as a nested class (e.g., `AddItemToCartValidator`)
- **Handlers**: `{MessageName}Handler` as a static class (e.g., `AddItemToCartHandler`)

### C# Language Features

#### Records and Immutability

Use records for data transfer objects (DTOs) and value objects (VOs), such as:

```csharp
// Good - Immutable record
public sealed record PaymentRequest(
    decimal Amount,
    string Currency,
    string CardId,
    string CustomerId,
    string? Description = null,
    Dictionary<string, object>? Metadata = null,
    string? IdempotencyKey = null,
    AddressDetails AddressDetails,
    PayingCardDetails PayingCardDetails
);

public sealed record AddressDetails(
    string HouseNumber,
    string? HouseName,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string Postcode
);
```

#### Collection Patterns

**Always prefer immutable collections** unless there is an explicit, documented use case requiring mutability.

**Prefer:**
- `IReadOnlyList<T>` for ordered collections
- `IReadOnlyCollection<T>` for unordered collections
- `IReadOnlyDictionary<TKey, TValue>` for key-value pairs

**Avoid:**
- `List<T>` (mutable)
- `IList<T>` (mutable interface)
- `ICollection<T>` (mutable interface)

```csharp
// Good - Immutable collections
public sealed record Product(
    string Sku,
    string Name,
    IReadOnlyList<ProductImage> Images,  // Immutable
    IReadOnlyList<string> Tags);         // Immutable

// Bad - Mutable collections
public sealed record Product(
    string Sku,
    string Name,
    List<ProductImage> Images,  // Can be modified externally
    List<string> Tags);         // Can be modified externally
```

**Creating Immutable Collections:**

```csharp
// Constructor accepting arrays/lists
public static Product Create(string sku, string name, ProductImage[] images)
{
    return new Product(
        sku,
        name,
        images.ToList().AsReadOnly(),  // Convert to IReadOnlyList
        []  // Empty collection
    );
}

// Using collection expressions (C# 12+)
var images = new List<ProductImage> { image1, image2 };
var product = new Product(sku, name, images.AsReadOnly(), []);
```

**Why Immutability:**
- Prevents accidental mutation (thread-safe by default)
- Clear intent (this collection won't change)
- Easier to reason about (no hidden side effects)
- Consistent with functional programming principles
- Works seamlessly with Marten, EF Core, and JSON serialization

#### Value Object Patterns

**Use value objects** to wrap primitive types with domain-specific validation and behavior. Value objects make implicit constraints explicit and prevent invalid states from being representable.

**When to Use Value Objects:**
- Identity values with format constraints (SKU, OrderId, CustomerId)
- Domain concepts with business rules (ProductName, EmailAddress, PhoneNumber)
- Values requiring validation (Money, Percentage, PositiveInteger)
- Primitive obsession code smell (too many raw strings/ints)

**Standard Value Object Pattern:**

All value objects in CritterSupply follow this consistent pattern:

1. **Sealed record** - Immutable and value-based equality
2. **Factory method** - `From(string)` for validated construction
3. **Private parameterless constructor** - Required by Marten/JSON deserializers
4. **Implicit string operator** - Seamless conversion to string for queries/serialization
5. **JSON converter** - Transparent serialization (serializes as plain string, not wrapped object)

**Example: Sku Value Object**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProductCatalog.Products;

/// <summary>
/// Strongly-typed SKU identifier for products.
/// Enforces format constraints while remaining string-compatible for serialization.
/// Rules: A-Z (uppercase only), 0-9, hyphens (-), max 24 characters.
/// </summary>
[JsonConverter(typeof(SkuJsonConverter))]
public sealed record Sku
{
    private const int MaxLength = 24;
    private static readonly Regex ValidPattern = new(@"^[A-Z0-9\-]+$", RegexOptions.Compiled);

    public string Value { get; init; } = null!;

    private Sku() { }  // For Marten/JSON deserialization

    public static Sku From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("SKU cannot be empty", nameof(value));

        if (value.Length > MaxLength)
            throw new ArgumentException($"SKU cannot exceed {MaxLength} characters", nameof(value));

        if (!ValidPattern.IsMatch(value))
            throw new ArgumentException(
                "SKU must contain only uppercase letters (A-Z), numbers (0-9), and hyphens (-)",
                nameof(value));

        return new Sku { Value = value };
    }

    public static implicit operator string(Sku sku) => sku.Value;

    public override string ToString() => Value;
}

public sealed class SkuJsonConverter : JsonConverter<Sku>
{
    public override Sku Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null
            ? throw new JsonException("SKU cannot be null")
            : Sku.From(value);
    }

    public override void Write(Utf8JsonWriter writer, Sku value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
```

**Example: ProductName Value Object**

```csharp
/// <summary>
/// Strongly-typed product name.
/// Enforces format constraints while remaining string-compatible for serialization.
/// Rules: Mixed case allowed, letters, numbers, spaces, and special chars (. , ! & ( ) -), max 100 characters.
/// </summary>
[JsonConverter(typeof(ProductNameJsonConverter))]
public sealed record ProductName
{
    private const int MaxLength = 100;

    // Allows: A-Z, a-z, 0-9, spaces, and special chars: . , ! & ( ) -
    private static readonly Regex ValidPattern = new(@"^[A-Za-z0-9\s.,!&()\-]+$", RegexOptions.Compiled);

    public string Value { get; init; } = null!;

    private ProductName() { }  // For Marten/JSON deserialization

    public static ProductName From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Product name cannot be empty", nameof(value));

        var trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
            throw new ArgumentException($"Product name cannot exceed {MaxLength} characters", nameof(value));

        if (!ValidPattern.IsMatch(trimmed))
            throw new ArgumentException(
                "Product name contains invalid characters. Allowed: letters, numbers, spaces, and . , ! & ( ) -",
                nameof(value));

        return new ProductName { Value = trimmed };
    }

    public static implicit operator string(ProductName name) => name.Value;

    public override string ToString() => Value;
}

public sealed class ProductNameJsonConverter : JsonConverter<ProductName>
{
    public override ProductName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null
            ? throw new JsonException("Product name cannot be null")
            : ProductName.From(value);
    }

    public override void Write(Utf8JsonWriter writer, ProductName value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
```

**Usage in Domain Models:**

```csharp
// Good - Using value objects
public sealed record Product
{
    public Sku Sku { get; init; } = null!;
    public ProductName Name { get; init; } = null!;
    public CategoryName Category { get; init; } = null!;
    // ...

    private Product() { }

    public static Product Create(string sku, string name, string category)
    {
        return new Product
        {
            Sku = Sku.From(sku),              // Validates format
            Name = ProductName.From(name),    // Validates format
            Category = CategoryName.From(category),
            Status = ProductStatus.Active,
            AddedAt = DateTimeOffset.UtcNow
        };
    }
}
```

**Usage in Marten Configuration:**

```csharp
// Marten document store configuration
opts.Schema.For<Product>()
    .Identity(x => x.Sku)  // Implicit conversion to string works seamlessly
    .SoftDeleted();
```

**Usage in HTTP Commands:**

```csharp
// Command using value objects
public sealed record AddProduct(
    string Sku,              // Plain string in HTTP JSON
    string Name,             // Plain string in HTTP JSON
    string Category,
    string Description)
{
    public class AddProductValidator : AbstractValidator<AddProduct>
    {
        public AddProductValidator()
        {
            RuleFor(x => x.Sku).NotEmpty();
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.Category).NotEmpty();
        }
    }
}

// Handler converts strings to value objects
public static class AddProductHandler
{
    [WolverinePost("/api/products")]
    public static async Task<CreationResponse> Handle(
        AddProduct command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var product = Product.Create(
            command.Sku,      // Sku.From() called inside Create
            command.Name,     // ProductName.From() called inside Create
            command.Category);

        session.Store(product);
        await session.SaveChangesAsync(ct);

        return new CreationResponse($"/api/products/{command.Sku}");
    }
}
```

**JSON Serialization Examples:**

```json
// HTTP Request (plain strings)
{
  "sku": "DOG-BOWL-001",
  "name": "Premium Stainless Steel Dog Bowl",
  "category": "Dog Bowls",
  "description": "Dishwasher-safe, non-slip base"
}

// Marten Document (plain strings via JSON converter)
{
  "sku": "DOG-BOWL-001",
  "name": "Premium Stainless Steel Dog Bowl",
  "category": "Dog Bowls",
  "status": "Active",
  "addedAt": "2026-01-19T12:00:00Z"
}
```

**Key Benefits:**

1. **Type Safety** - Compiler prevents using wrong identifier types (can't pass Sku where ProductName expected)
2. **Self-Documenting** - `Sku.From("ABC-123")` is clearer than `"ABC-123"` (explicit validation)
3. **Validation at Boundaries** - Invalid values rejected at construction, not deep in business logic
4. **Transparent Serialization** - JSON converter makes them serialize as plain strings (no {"value": "ABC-123"} wrapping)
5. **Marten Compatibility** - Implicit operators work seamlessly with `.Identity()`, queries, and indexing
6. **Refactoring Safety** - Changing constraints updates one place (factory method), not scattered validation code

**When NOT to Use Value Objects:**

- Simple strings with no constraints (descriptions, free-text fields)
- Primitive types with no business rules (counts, flags)
- Values that are truly primitives in the domain (temperatures, distances without validation)

#### Disway Use of Inheritance

By default commands, queries, requests, and responses should be `sealed`. Sadly, C# does not have an option built-in to make all classes and records `sealed` automatically like the programming language Kotlin does. To prevent these types from being used with a handler outside its specified one, make them `sealed`.

```csharp
// Good - This query is meant for one handler and one handler only, typically
// the same name appended with "Handler" (such as GetCustomerAddressHandler)
public sealed record GetCustomerAddress(
    Guid CustomerId
);
```

Likewise, our models should also have the `sealed` keyboard always applied and should be a `record` instead of a `class` unless an explicit use case has been designated.

```csharp
public sealed record CustomerShippingAddress(
    Guid Id,
    Guid CustomerId,
    string AddressNickname,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string Postcode,
    string Country)
{
    // Inner code removed for brevity
}
```

### Event Sourcing, Functional Programming Inspired, and the Decider Pattern

When building event sourced models with Marten, prefer immutability and pure functions to apply events to the model.

When building event-driven applications with Wolverine, prefer pure functions to encapsulate and insulate decisions made by the business logic.

When building event-driven solutions with the "Critter Stack" that employee event sourcing, prefer using Wolverine's "Aggregate Handler Workflow" which is the Critter Stack's flavor of the "Decider" pattern.

Use `sealed` and `record` by default for types considered write models, such as aggregates, projections, and domain models. Follow Marten's event sourcing patterns for creating and applying events to these models.

#### Event Sourced Domain Models are Write Models

These immutable, functional programming-inspired models (sometimes called "aggregates" or "write models") have no behavior methods that decide if an event should be written or not, as that is for our decider functions to handle. There is no base class or interface for these models to inherit from, such as an abstract `Aggregate` class or `IEntity` interface one may see with typical object-oriented rich domain models.

```csharp
// Good - A model promoting immutability and pure functions to apply events.
public sealed record Payment(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset? StartedAt,
    decimal Amount,
    PaymentStatus Status,
    int RetryCount,
    DateTimeOffset? ProcessedAt,
    string? FailureReason)
{
    public static Payment Create(IEvent<PaymentProcessingStarted> @event) =>
        new(@event.StreamId,
            @event.Data.OrderId,
            @event.Data.CustomerId,
            @event.Data.StartedAt,
            @event.Data.Amount,
            PaymentStatus.Pending,
            0,
            null,
            null);

    public Payment Apply(PaymentSucceeded @event) =>
        this with
        {
            Status = PaymentStatus.Succeeded,
            ProcessedAt = @event.ProcessedAt
        };

    public Payment Apply(PaymentFailed @event) =>
        this with
        {
            Status = PaymentStatus.Failed,
            ProcessedAt = @event.ProcessedAt,
            FailureReason = @event.Reason,
            RetryCount = RetryCount + 1
        };
        
    // The remaining inner code has been removed for brevity    
}
```

There's nothing "wrong" with this traditional approach, but it not the preferred way to build event sourced models in this system! We want to separate the decision-making logic from the model itself to promote better testability, maintainability, and separation of concerns by employing the Decider pattern through Wolverine's Aggregate Handler Workflow.

```csharp
// Bad - A traditional object-oriented aggregate model with behavior methods to make decisions
public class Payment : Aggregate
{
    // Properties removed for brevity
    
    public Payment(){}
    
    public static Payment Begin(Guid paymentId, Guid orderId, decimal amount) => new(paymentId, orderId, amount);

    private Payment(Guid paymentId, Guid orderId, decimal amount)
    {
        var @event = new PaymentRequested(paymentId, orderId, amount);

        Enqueue(@event);
        Apply(@event);
    }
    
    public void Apply(PaymentRequested @event)
    {
        Id = @event.PaymentId;
        OrderId = @event.OrderId;
        Amount = @event.Amount;
    }
    
    public void Complete()
    {
        if(Status != PaymentStatus.Pending)
            throw new InvalidOperationException($"Cannot complete a payment that is in {Status} status.");

        var @event = new PaymentCompleted(Id, DateTime.UtcNow);

        Enqueue(@event);
        Apply(@event);
    }

    public void Apply(PaymentCompleted @event)
    {
        Status = PaymentStatus.Completed;
    }

    // Additional behavior methods and apply methods removed for brevity
}
```

#### Domain Event Structure and Identity

Domain events are the persisted records of state changes in event-sourced aggregates. When designing events for Marten's event store, follow these guidelines to ensure proper projection and aggregate reconstruction.

**Include Aggregate IDs in Event Data**

Even though the stream ID identifies which aggregate an event belongs to, **always include the aggregate ID as the first parameter in your event record**. This serves multiple purposes:

1. **Marten Projection Conventions**: Marten's inline projections and snapshot aggregates rely on event data structure for proper reconstruction
2. **Event Clarity**: Makes events self-documenting when viewed in isolation (e.g., in logs, debugging, or event replays)
3. **Cross-Reference**: Enables easier correlation between events and aggregates in queries and diagnostics

**Example - Correct Event Structure:**

```csharp
// Good - Includes CheckoutId even though it's also the stream ID
public sealed record CheckoutStarted(
    Guid CheckoutId,    // Always include the aggregate ID first
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<CheckoutLineItem> Items,
    DateTimeOffset StartedAt);

// Handler starting the stream
public static class CheckoutInitiatedHandler
{
    public static void Handle(
        Shopping.CheckoutInitiated message,
        IDocumentSession session)
    {
        var startedEvent = new CheckoutStarted(
            message.CheckoutId,  // Pass the ID explicitly
            message.CartId,
            message.CustomerId,
            message.Items,
            message.InitiatedAt);

        // Stream ID matches the CheckoutId in the event
        session.Events.StartStream<Checkout>(message.CheckoutId, startedEvent);
    }
}

// Aggregate Create method expects the ID in the event
public sealed record Checkout(...)
{
    public static Checkout Create(IEvent<CheckoutStarted> @event) =>
        new(@event.StreamId,           // Stream ID from Marten
            @event.Data.CartId,        // Event data
            @event.Data.CustomerId,
            @event.Data.Items,
            @event.Data.StartedAt,
            ...);
}
```

**Example - Incorrect Event Structure:**

```csharp
// Bad - Omitting the aggregate ID breaks Marten conventions
public sealed record CheckoutStarted(
    // Missing CheckoutId - this will cause projection issues
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<CheckoutLineItem> Items,
    DateTimeOffset StartedAt);
```

**Why This Matters:**

When Marten applies inline projections using `opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline)`, it expects event data to follow certain conventions for aggregate identity. Omitting the aggregate ID from event data can cause:

- `NullReferenceException` during projection application
- Failed aggregate reconstruction from event streams
- Timeout errors in integration tests (events not being applied correctly)

**General Rule:** If you're migrating event-sourced aggregates between bounded contexts or refactoring event structures, **preserve the original event parameter order and always include the aggregate ID as the first parameter**.

#### Use Status Enums for Aggregate State

Prefer using an expressive `Status` enum property to represent aggregate lifecycle state instead of multiple boolean flags. While not a hard rule, this pattern proves necessary sooner rather than later as handlers and business logic need to evaluate aggregate state.

**Benefits of Status Enums:**

1. **Single Source of Truth**: One `Status` property eliminates possibility of invalid state combinations (e.g., both `IsCompleted` and `IsCancelled` being true)
2. **Explicit State Machine**: The enum makes the aggregate lifecycle self-documenting and easier to reason about
3. **Better Expressiveness**: `Status == CartStatus.CheckedOut` is more readable than `CheckoutInitiated == true`
4. **Extensibility**: Adding new states doesn't require new boolean properties
5. **Consistency**: Aligns aggregates across the system with a common pattern

```csharp
// Good - Using Status enum
public sealed record Cart(
    Guid Id,
    Guid? CustomerId,
    Dictionary<string, CartLineItem> Items,
    CartStatus Status)  // Single enum property
{
    public bool IsTerminal => Status != CartStatus.Active;

    public Cart Apply(CartCleared @event) =>
        this with { Status = CartStatus.Cleared };

    public Cart Apply(CheckoutInitiated @event) =>
        this with { Status = CartStatus.CheckedOut };
}

public enum CartStatus
{
    Active,      // Cart can be modified
    Abandoned,   // Terminal state
    Cleared,     // Terminal state
    CheckedOut   // Terminal state
}
```

```csharp
// Bad - Using multiple boolean flags
public sealed record Cart(
    Guid Id,
    Guid? CustomerId,
    Dictionary<string, CartLineItem> Items,
    bool IsAbandoned,        // Multiple booleans create ambiguity
    bool IsCleared,          // What if multiple are true?
    bool CheckoutInitiated)  // Unclear state machine
{
    public bool IsTerminal => IsAbandoned || IsCleared || CheckoutInitiated;
}
```

**When to Use Status Enums:**

- Aggregates with lifecycle states (Orders, Payments, Shipments, Carts, etc.)
- Any entity where handlers need to check "what state is this in?"
- When state transitions need to be explicit and validated
- When terminal states need to be identified

#### Recommended Reading

- Wolverine Aggregate Handlers and Event Sourcing documentation: https://wolverinefx.net/guide/durability/marten/event-sourcing.html#aggregate-handlers-and-event-sourcing
- Wolverine Handler Workflow documentation: https://wolverinefx.net/guide/http/marten.html#marten-aggregate-workflow
- Original functional programming blog article on the Decider Pattern using F#: https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider

### Traditional Relational Data Models

While most of this system uses event sourcing, there may be cases where "traditional" models are used to persist data in a relational table. In these cases, like with our event sourcing models, we prefer immutability unless dependencies and behaviors are executed to produce a new instance of said model. If the ORM known as EF Core is being used, additional configuration may be needed to promote these behaviors.

In modern C#, `record` allows for value-based equality and init-only mutation patterns.

```csharp
// Good - An immutable relational data model using EF Core capable principles.
public sealed record Payment
{
    // Required by EF Core
    private Payment() { }

    public Payment(
        Guid id,
        Guid orderId,
        decimal amount,
        string currency,
        DateTimeOffset createdAt,
        PaymentStatus status,
        string? provider = null,
        string? providerReference = null)
    {
        Id = id;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        CreatedAt = createdAt;
        Status = status;
        Provider = provider;
        ProviderReference = providerReference;
    }

    public Guid Id { get; init; }

    public Guid OrderId { get; init; }
    public Order? Order { get; init; } // nav property (still supported)

    public decimal Amount { get; init; }
    public string Currency { get; init; } = null!;

    public DateTimeOffset CreatedAt { get; init; }

    public PaymentStatus Status { get; init; }

    public string? Provider { get; init; }
    public string? ProviderReference { get; init; } // e.g., Stripe charge ID

    // Instead of mutating state, we return a new instance.
    public Payment MarkAsCompleted(string provider, string providerReference) =>
        this with
        {
            Status = PaymentStatus.Completed,
            Provider = provider,
            ProviderReference = providerReference
        };

    public Payment MarkAsFailed() =>
        this with { Status = PaymentStatus.Failed };
}
```

Below we have a more traditional `class` based relational data model that allows for mutation of its properties. This is not the preferred way to build models in this system, but it is acceptable *if* and only if the use case demands it.

```csharp
// Bad - A mutable relational data model
public class Payment
{
    // EF Core requires a parameterless constructor
    private Payment() { }

    public Payment(
        Guid id,
        Guid orderId,
        decimal amount,
        string currency,
        DateTimeOffset createdAt,
        PaymentStatus status)
    {
        Id = id;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        CreatedAt = createdAt;
        Status = status;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }
    public Order? Order { get; private set; }   // Navigation property

    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string? Provider { get; private set; }
    public string? ProviderReference { get; private set; } // e.g., Stripe charge ID

    // Example behavior for OO-style rich domain model
    public void MarkAsCompleted(string provider, string providerReference)
    {
        if (Status == PaymentStatus.Completed)
            return;

        Status = PaymentStatus.Completed;
        Provider = provider;
        ProviderReference = providerReference;
    }

    public void MarkAsFailed()
    {
        if (Status == PaymentStatus.Completed)
            throw new InvalidOperationException(
                "Cannot mark a completed payment as failed.");

        Status = PaymentStatus.Failed;
    }
}
```

## Wolverine Messaging Idioms

### Message Handlers

Prefer single responsibility message handlers. Each handler should do one thing and do it well. If a handler is getting too large or complex, consider breaking it down into smaller handlers or using helper classes.

### Compound Handlers

It's frequently beneficial to split message handling for a single message up into methods that load any necessary data and the business logic that transforms the current state or decides to take other actions.

Wolverine allows you to use the conventional middleware naming conventions on each handler to do exactly this. The goal here is to use separate methods for different concerns like loading data or validating data so that the "main" message handler (or HTTP endpoint method) can be a pure function that is completely focused on domain logic or business workflow logic for easy reasoning and effective unit testing.

As such, `Validate()` and `Before()` methods are invokved before the primary `Handle()` method is invokved. We do not want that handle method to do tasks such as making sure an entity/aggregate exists, pre-loading an entity, that said entity is in the expected status/state, or reaching out to another service. Those are not the concerns of the main handler method, those are not part of the business logic.

#### Background Info:
> Wolverine's "compound handler" feature where handlers can be built from multiple methods that are called one at a time
by Wolverine was heavily inspired by Jim Shore's writing on the "A-Frame Architecture". See Jeremy's post [A-Frame Architecture with Wolverine](https://jeremydmiller.com/2023/07/19/a-frame-architecture-with-wolverine/)
for more background on the goals and philosophy behind this approach.

#### Naming Conventions

Message handlers should be named with the suffix "Handler" to clearly indicate their purpose. For example, a handler for processing orders might be named `ProcessOrderHandler`.

To separate handlers, one can use `Before`, `After`, `Validate`, `Load`, and `Finally` methods to separate concerns within a handler. The conventions are as follows:

| Lifecycle                                                | Method Names                |
|----------------------------------------------------------|-----------------------------|
| Before the Handler(s)                                    | `Before`, `BeforeAsync`, `Load`, `LoadAsync`, `Validate`, `ValidateAsync` |
| After the Handler(s)                                     | `After`, `AfterAsync`, `PostProcess`, `PostProcessAsync` |
| In `finally` blocks after the Handlers & "After" methods | `Finally`, `FinallyAsync`   |

The exact name has no impact on functionality, but the idiom is that `Load/LoadAsync` is used to load input data for
the main handler method.

#### Example 1

A message (command) handler with validation on the command, business logic in the pure function, and said pure function starting a new event stream in Marten for event sourcing usage.

```csharp
// Good - A command that promotes immutability and uses FluentValidation for validation
public sealed record PlaceOrder(
    Guid CustomerId,
    IEnumerable<OrderLine> Lines)
{
    public class PlaceOrderValidator : AbstractValidator<PlaceOrder>
    {
        public PlaceOrderValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.Lines).NotEmpty();
        }
    }
}

// Good - Levaring Wolverine's CreationResponse helper for consistent API responses
public sealed record PlaceOrderResponse(Guid Id)
    : CreationResponse($"/api/orders/{Id}");

// Good - A pure function handler, isolated from side effects and infrastructure concerns, which
// handles a command (AKA a type of message to Wolverine) and is beginning a new event stream in Marten.
public static class PlaceOrderHandler
{
    [WolverinePost("/api/orders")]
    public static (PlaceOrderResponse, IStartStream) Handle(PlaceOrder command)
    {
        var now = DateTimeProvider.UtcNow;
        var placed = new OrderPlaced(command.CustomerId, command.Lines, now);
        var orderId = Guid.CreateVersion7();
        var start = MartenOps.StartStream<OrderPlaced>(orderId, placed);

        return (new PlaceOrderResponse(start.StreamId), start);
    }
}
```

#### Example 2: 

A similar message handler as example 1, but here the stream already exists. By returning the event and outgoing messages, Wolverine will handle the persistence of the event to the stream and the sending of any outgoing messages. Wolverine knows the associated stream because the command contains the `OrderId`, which is the same as the stream identifier.

```csharp
// Good - A command that promotes immutability and uses FluentValidation for validation
public sealed record ProcessPayment(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount)
{
    public class ProcessPaymentValidator : AbstractValidator<ProcessPayment>
    {
        public ProcessPaymentValidator()
        {
            RuleFor(x => x.OrderId).NotEmpty();
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.Amount).NotEmpty().GreaterThan(0m);
        }
    }
}

// Good - A pure function message handler that processes a payment command. It returns an event,
// which will be persisted to the associated event stream, and outgoing messages to be sent to
// other bounded contexts or systems. In short, all that matters here is the business logic.
public static class ProcessPaymentHandler
{
    public static (PaymentProcessingStarted, OutgoingMessages) Handle(ProcessPayment command)
    {
        var paymentId = Guid.CreateVersion7();
        var @event = new PaymentProcessingStarted(
            command.OrderId,
            command.CustomerId,
            command.Amount,
            DateTimeProvider.UtcNow);

        var messages = new OutgoingMessages();
        messages.Add(new ChargePaymentProcessor(paymentId, command.Amount, command.OrderId));

        return (@event, messages);
    }
}
```

#### Example 3

This message handler demonstrates the use of a `Before()` method to pre-load the necessary aggregate from the event store before executing the main business logic in the `Handle()` method. The `Before()` method checks for preconditions and returns a `ProblemDetails` object if any issues are found, allowing for clean separation of concerns. Note that the handle method uses the newer `[WriteAggregate]` attribute to indicate that the `ProductInventory` aggregate should be loaded for writing specifically. If only reading was being done, we may want to use `[ReadAggregate]` instead, as it's optimized for that use case.

```csharp
public sealed record ReleaseInventory(
    Guid ProductInventoryId,
    Guid OrderId,
    string Reason)
{
    public class ReleaseInventoryValidator : AbstractValidator<ReleaseInventory>
    {
        public ReleaseInventoryValidator()
        {
            RuleFor(x => x.ProductInventoryId).NotEmpty();
            RuleFor(x => x.OrderId).NotEmpty();
            RuleFor(x => x.Reason).NotEmpty().MaximumLength(128);
        }
    }
}

public static class ReleaseInventoryHandler
{
    public static ProblemDetails Before(
        ReleaseInventory command,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails { Detail = "Product inventory not found", Status = 404 };

        if (!inventory.Reservations.ContainsKey(command.OrderId))
            return new ProblemDetails
            {
                Detail = $"No reservation found for order {command.OrderId}",
                Status = 404
            };

        return WolverineContinue.NoProblems;
    }

    public static InventoryReleased Handle(
        ReleaseInventory command,
        [WriteAggregate] ProductInventory inventory)
    {
        var quantity = inventory.Reservations[command.OrderId];

        return new InventoryReleased(
            command.OrderId,
            quantity,
            command.Reason,
            DateTimeProvider.UtcNow);
    }
}
```

#### Example 4

This is the more "traditional" way of writing a Wolverine message handler, with the `[AggregateHandler]` on the `Handle()` method, before the more-optimized `[WriteAggregate]` and `[ReadAggregate]` attributes were introduced. This example is provided for context and comparison to the preferred approach shown in Example 1, 2, and 3 above.

```csharp
[AggregateHandler]
public static IEnumerable<object> Handle(MarkItemReady command, Order order)
{
    if (order.Items.TryGetValue(command.ItemName, out var item))
    {
        // Not doing this in a purist way here, but just trying to illustrate the Wolverine mechanics
        item.Ready = true;

        // Mark that the this item is ready
        yield return new ItemReady(command.ItemName);
    }
    else
    {
        // Some crude validation
        throw new InvalidOperationException($"Item {command.ItemName} does not exist in this order");
    }

    // If the order is ready to ship, also emit an OrderReady event
    if (order.IsReadyToShip())
    {
        yield return new OrderReady();
    }
}
```

For more examples and documentation for Wolverine idioms surrounding its message handlers, check out the official [Wolverine Message Handlers documentation](https://wolverine.net/docs/messages/handlers/).

As this software solution heavily leverages Wolverine's integration with Marten, for additional information about it such as stream verification in a message handler, check out the the [Wolverine document on Aggregate Handlers and Event Sourcing](https://wolverine.netlify.app/guide/durability/marten/event-sourcing.html#validation-on-stream-existence).

### Handler Return Patterns for Event Sourcing

When building event-sourced handlers with Wolverine and Marten, the return type of your handler communicates intent and determines how events are persisted and messages are published. This section clarifies the established patterns for maximum readability and developer comprehension.

#### Pattern 1: Handlers with `[WriteAggregate]` - Existing Streams

For handlers that operate on existing aggregates (loaded via `[WriteAggregate]` or `[ReadAggregate]`), return a tuple of `(Events, OutgoingMessages)`:

- **`Events`**: Domain events to append to the aggregate's event stream
- **`OutgoingMessages`**: Integration messages to publish to other bounded contexts

```csharp
public static class CapturePaymentHandler
{
    public static ProblemDetails Before(
        CapturePayment command,
        Payment? payment)
    {
        if (payment is null)
            return new ProblemDetails { Detail = "Payment not found", Status = 404 };

        if (payment.Status != PaymentStatus.Authorized)
            return new ProblemDetails
            {
                Detail = $"Payment is not in authorized status. Current status: {payment.Status}",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task<(Events, OutgoingMessages)> Handle(
        CapturePayment command,
        [WriteAggregate] Payment payment,
        IPaymentGateway gateway,
        CancellationToken cancellationToken)
    {
        var result = await gateway.CaptureAuthorizedAsync(
            payment.AuthorizationId!,
            command.AmountToCapture ?? payment.Amount,
            cancellationToken);

        var capturedAt = DateTimeOffset.UtcNow;

        var events = new Events();
        var outgoing = new OutgoingMessages();

        if (!result.Success)
        {
            var failedEvent = new PaymentFailed(
                payment.Id,
                result.FailureReason ?? "Unknown capture failure",
                result.IsRetriable,
                capturedAt);

            events.Add(failedEvent);

            outgoing.Add(new IntegrationMessages.PaymentFailed(
                payment.Id,
                payment.OrderId,
                result.FailureReason ?? "Unknown capture failure",
                result.IsRetriable,
                capturedAt));

            return (events, outgoing);
        }

        var domainEvent = new PaymentCaptured(
            payment.Id,
            result.TransactionId!,
            capturedAt);

        events.Add(domainEvent);

        outgoing.Add(new IntegrationMessages.PaymentCaptured(
            payment.Id,
            payment.OrderId,
            payment.Amount,
            result.TransactionId!,
            capturedAt));

        return (events, outgoing);
    }
}
```

**Key Points:**
- Use `[WriteAggregate]` to load the aggregate for modification
- Return `(Events, OutgoingMessages)` - both collections are explicit and readable
- Add domain events to `Events` - these append to the aggregate's stream
- Add integration messages to `OutgoingMessages` - these publish to other contexts
- If no domain events needed (e.g., on failure), return empty `Events` collection

#### Pattern 2: Handlers Starting New Streams - Message Handlers

For message handlers (not HTTP endpoints) that start new event streams, use `IDocumentSession.Events.StartStream()` directly and return `OutgoingMessages`:

```csharp
public static class AuthorizePaymentHandler
{
    public static async Task<OutgoingMessages> Handle(
        AuthorizePayment command,
        IPaymentGateway gateway,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var paymentId = Guid.CreateVersion7();
        var initiatedAt = DateTimeOffset.UtcNow;

        var initiated = new PaymentInitiated(
            paymentId,
            command.OrderId,
            command.CustomerId,
            command.Amount,
            command.Currency,
            command.PaymentMethodToken,
            initiatedAt);

        var result = await gateway.AuthorizeAsync(
            command.Amount,
            command.Currency,
            command.PaymentMethodToken,
            cancellationToken);

        var processedAt = DateTimeOffset.UtcNow;
        var outgoing = new OutgoingMessages();

        if (!result.Success)
        {
            var failedEvent = new PaymentFailed(
                paymentId,
                result.FailureReason ?? "Unknown authorization failure",
                result.IsRetriable,
                processedAt);

            session.Events.StartStream<Payment>(paymentId, initiated, failedEvent);

            outgoing.Add(new IntegrationMessages.PaymentFailed(
                paymentId,
                command.OrderId,
                result.FailureReason ?? "Unknown authorization failure",
                result.IsRetriable,
                processedAt));

            return outgoing;
        }

        var authorizedEvent = new PaymentAuthorized(
            paymentId,
            result.TransactionId!,
            processedAt);

        session.Events.StartStream<Payment>(paymentId, initiated, authorizedEvent);

        outgoing.Add(new IntegrationMessages.PaymentAuthorized(
            paymentId,
            command.OrderId,
            command.Amount,
            result.TransactionId!,
            processedAt,
            processedAt.AddDays(7)));

        return outgoing;
    }
}
```

**Key Points:**
- Inject `IDocumentSession session` to access event store
- Use `session.Events.StartStream<T>(streamId, ...events)` to create new stream
- Return `OutgoingMessages` for integration events
- Pass multiple events to `StartStream()` to initialize the stream

#### Pattern 3: HTTP Endpoints Starting New Streams

For HTTP endpoints that start new streams, use `MartenOps.StartStream()` which returns `IStartStream` for Wolverine's HTTP workflow:

```csharp
public static class InitializeCartHandler
{
    [WolverinePost("/api/carts")]
    public static (IStartStream, CreationResponse) Handle(InitializeCart command)
    {
        var cartId = Guid.CreateVersion7();
        var @event = new CartInitialized(
            command.CustomerId,
            command.SessionId,
            DateTimeOffset.UtcNow);

        var stream = MartenOps.StartStream<Cart>(cartId, @event);

        return (stream, new CreationResponse($"/api/carts/{cartId}"));
    }
}
```

**Key Points:**
- Use `[WolverinePost]` or other HTTP attributes to mark as HTTP endpoint
- Use `MartenOps.StartStream<T>()` which returns `IStartStream`
- Return tuple with `IStartStream` and HTTP response (e.g., `CreationResponse`)
- Wolverine's HTTP workflow handles stream persistence and response

#### Why Not Return Raw Tuples or `object`?

**❌ Avoid:**
```csharp
// BAD - Unclear what this returns
public static async Task<object> Handle(...)
{
    return (domainEvent, integrationEvent);  // Wolverine may treat tuple as single event!
}
```

**✅ Prefer:**
```csharp
// GOOD - Explicit and clear intent
public static async Task<(Events, OutgoingMessages)> Handle(...)
{
    var events = new Events();
    var outgoing = new OutgoingMessages();

    events.Add(domainEvent);
    outgoing.Add(integrationEvent);

    return (events, outgoing);
}
```

**Reasons:**
- **Clarity**: `(Events, OutgoingMessages)` is self-documenting - developers immediately understand the intent
- **Type Safety**: Wolverine knows how to handle `Events` and `OutgoingMessages` correctly
- **Avoids Bugs**: Raw tuples like `(event, event)` can be misinterpreted by Marten/Wolverine as a single tuple event
- **Consistency**: Establishes a clear pattern across all handlers in the codebase

#### Summary Table

| Scenario | Handler Type | Return Type | Stream Creation |
|----------|-------------|-------------|-----------------|
| Update existing aggregate | Message Handler | `(Events, OutgoingMessages)` | N/A - uses `[WriteAggregate]` |
| Start new stream | Message Handler | `OutgoingMessages` | `session.Events.StartStream<T>()` |
| Start new stream | HTTP Endpoint | `(IStartStream, HttpResponse)` | `MartenOps.StartStream<T>()` |

### When to Use `[WriteAggregate]` vs `Load()` Pattern

Wolverine's `[WriteAggregate]` attribute provides automatic aggregate loading and event persistence, but it requires the aggregate ID to be **directly resolvable** from the command properties. Understanding when to use each pattern is crucial for building reliable handlers.

#### Preference Hierarchy

**ALWAYS prefer `[WriteAggregate]` as your first choice.** It is the cleanest, most efficient, and most idiomatic pattern for Wolverine handlers. Only fall back to the `Load()` pattern when you encounter specific use cases that prevent Wolverine from auto-resolving the aggregate ID.

| Scenario | Use This Pattern | Why |
|----------|-----------------|-----|
| **Aggregate ID is a direct command property** (e.g., `PaymentId`, `OrderId`) | ✅ `[WriteAggregate]` | Wolverine auto-loads and auto-persists - clean and efficient |
| **Aggregate ID must be computed** (e.g., `CombinedGuid(Sku, WarehouseId)`) | ⚠️ `Load()` method | Wolverine can't resolve computed properties - manual loading required |
| **Aggregate must be discovered via query** (e.g., find by `ReservationId`, not `InventoryId`) | ⚠️ `Load()` method | Wolverine needs a direct ID - queries require manual loading |

**Key Principle:** If Wolverine can auto-resolve the aggregate ID from the command properties, use `[WriteAggregate]`. This gives you automatic loading, validation, and event persistence with minimal ceremony.

#### Use `[WriteAggregate]` Pattern When:

1. **Command has aggregate ID as a direct property** that Wolverine can auto-resolve
2. **Aggregate ID doesn't need computation or querying**
3. **You want Wolverine to handle event persistence automatically**

**Example (Payments BC):**
```csharp
public sealed record CapturePayment(
    Guid PaymentId,  // Direct property - Wolverine can auto-resolve
    decimal? AmountToCapture);

public static class CapturePaymentHandler
{
    public static ProblemDetails Before(
        CapturePayment command,
        Payment? payment)  // Wolverine loads by PaymentId
    {
        if (payment is null)
            return new ProblemDetails { Detail = "Payment not found", Status = 404 };

        return WolverineContinue.NoProblems;
    }

    public static (Events, OutgoingMessages) Handle(
        CapturePayment command,
        [WriteAggregate] Payment payment)  // Wolverine handles persistence
    {
        var events = new Events();
        events.Add(new PaymentCaptured(...));

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.PaymentCaptured(...));

        return (events, outgoing);  // Wolverine persists events automatically
    }
}
```

#### Use `Load()` Pattern When:

1. **Aggregate ID must be computed** (e.g., derived from multiple properties)
2. **Aggregate must be discovered via query** (e.g., by non-ID properties)
3. **Wolverine cannot auto-resolve the aggregate ID**
4. **You need custom loading logic**

**Example (Inventory BC with computed ID):**
```csharp
public sealed record ReserveStock(
    Guid OrderId,
    string Sku,
    string WarehouseId,  // ID computed from Sku + WarehouseId
    Guid ReservationId,
    int Quantity)
{
    // Computed property - Wolverine cannot auto-resolve this
    public Guid InventoryId => ProductInventory.CombinedGuid(Sku, WarehouseId);
}

public static class ReserveStockHandler
{
    // Manual loading with custom logic
    public static async Task<ProductInventory?> Load(
        ReserveStock command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var inventoryId = command.InventoryId;  // Compute ID
        return await session.LoadAsync<ProductInventory>(inventoryId, ct);
    }

    public static ProblemDetails Before(
        ReserveStock command,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"No inventory found for SKU {command.Sku} at warehouse {command.WarehouseId}",
                Status = 404
            };

        return WolverineContinue.NoProblems;
    }

    // Manual event persistence - aggregate parameter has NO [WriteAggregate]
    public static OutgoingMessages Handle(
        ReserveStock command,
        ProductInventory inventory,  // NO [WriteAggregate] - already loaded by Load()
        IDocumentSession session)
    {
        var domainEvent = new StockReserved(...);

        // Manual append - NOT using [WriteAggregate]
        session.Events.Append(inventory.Id, domainEvent);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ReservationConfirmed(...));

        return outgoing;  // Return ONLY OutgoingMessages, not (Events, OutgoingMessages)
    }
}
```

**Example (Querying by non-ID property):**
```csharp
public sealed record ReservationCommitRequested(
    Guid OrderId,
    Guid ReservationId);  // Need to find inventory by ReservationId, not InventoryId

public static class ReservationCommitRequestedHandler
{
    // Query to discover which aggregate to load
    public static async Task<ProductInventory?> Load(
        ReservationCommitRequested message,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.Query<ProductInventory>()
            .FirstOrDefaultAsync(i => i.Reservations.ContainsKey(message.ReservationId), ct);
    }

    public static ProblemDetails Before(
        ReservationCommitRequested message,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails { Detail = "Inventory not found", Status = 404 };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        ReservationCommitRequested message,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var domainEvent = new ReservationCommitted(...);

        session.Events.Append(inventory.Id, domainEvent);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ReservationCommitted(...));

        return outgoing;
    }
}
```

#### Key Differences

| Aspect | `[WriteAggregate]` Pattern | `Load()` Pattern |
|--------|---------------------------|------------------|
| **Aggregate Loading** | Automatic by Wolverine | Manual via `Load()` method |
| **ID Resolution** | Must be direct command property | Can be computed or queried |
| **Event Persistence** | Automatic via return tuple | Manual via `session.Events.Append()` |
| **Return Type** | `(Events, OutgoingMessages)` | `OutgoingMessages` only |
| **Use `IDocumentSession` in `Handle()`** | No - Wolverine handles it | Yes - for manual persistence |

#### When in Doubt

**ALWAYS start with `[WriteAggregate]`** - it's the cleanest, most efficient, and idiomatic pattern. It leverages Wolverine's automatic aggregate loading and event persistence, minimizing ceremony and reducing the chance of errors.

Only fall back to `Load()` when you encounter one of these specific situations:
- **Wolverine gives an error:** "Unable to determine an aggregate id"
- **Command doesn't have a direct ID property:** The aggregate ID must be computed from multiple properties (e.g., `CombinedGuid(Sku, WarehouseId)`)
- **Aggregate must be discovered:** You need to query by a non-ID property (e.g., find inventory by `ReservationId` instead of `InventoryId`)

**Important:** Never use `Load()` + `[WriteAggregate]` together - this would hit the database twice. When using `Load()`, the aggregate parameter should **not** have `[WriteAggregate]` attribute, and you must manually persist events via `session.Events.Append()`.

#### Common Pitfall: Double Event Persistence

When using `Load()` pattern, **never** return both events via `session.Events.Append()` AND in the `Events` collection - this will persist events twice:

```csharp
// BAD - Double persistence!
public static (Events, OutgoingMessages) Handle(...)
{
    var domainEvent = new SomeEvent(...);

    session.Events.Append(inventory.Id, domainEvent);  // Persisted here

    var events = new Events();
    events.Add(domainEvent);  // Also persisted here - WRONG!

    return (events, outgoing);
}

// GOOD - Single persistence with Load() pattern
public static OutgoingMessages Handle(...)
{
    var domainEvent = new SomeEvent(...);

    session.Events.Append(inventory.Id, domainEvent);  // Persisted only here

    var outgoing = new OutgoingMessages();
    outgoing.Add(new IntegrationMessage(...));

    return outgoing;  // No Events collection
}
```

## External Service Integration Patterns

When integrating with external services (address verification, payment gateways, shipping providers, etc.), follow these patterns to maintain testability, configurability, and separation of concerns.

### Strategy Pattern with Dependency Injection

**Use Case:** Integrating with third-party APIs that require different implementations for development (stub) vs production (real service).

**Pattern:** Define an interface, provide multiple implementations, and register the appropriate one via dependency injection.

**Example: Address Verification Service**

```csharp
// 1. Define the interface (in domain project)
public interface IAddressVerificationService
{
    Task<AddressVerificationResult> VerifyAsync(
        string addressLine1,
        string? addressLine2,
        string city,
        string stateOrProvince,
        string postalCode,
        string country,
        CancellationToken ct);
}

// 2. Stub implementation for development/testing
public sealed class StubAddressVerificationService : IAddressVerificationService
{
    public Task<AddressVerificationResult> VerifyAsync(...)
    {
        // Always return verified - no external calls
        var result = new AddressVerificationResult(
            VerificationStatus.Verified,
            ErrorMessage: null,
            SuggestedAddress: corrected,
            ConfidenceScore: 1.0);

        return Task.FromResult(result);
    }
}

// 3. Production implementation (e.g., SmartyStreets)
public sealed class SmartyStreetsAddressVerificationService : IAddressVerificationService
{
    private readonly HttpClient _httpClient;
    private readonly string _authId;
    private readonly string _authToken;

    public SmartyStreetsAddressVerificationService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _authId = configuration["SmartyStreets:AuthId"]
                  ?? throw new InvalidOperationException("SmartyStreets AuthId not configured");
        _authToken = configuration["SmartyStreets:AuthToken"]
                     ?? throw new InvalidOperationException("SmartyStreets AuthToken not configured");
    }

    public async Task<AddressVerificationResult> VerifyAsync(...)
    {
        // Real API call to SmartyStreets
        var response = await _httpClient.PostAsJsonAsync(...);
        // Parse and return result
    }
}

// 4. Register in Program.cs (API project)
// Development:
builder.Services.AddSingleton<IAddressVerificationService, StubAddressVerificationService>();

// Production:
builder.Services.AddHttpClient<IAddressVerificationService, SmartyStreetsAddressVerificationService>();
```

### Handlers with External Service Dependencies

Wolverine's dependency injection works seamlessly with external services - just add the interface as a handler parameter.

```csharp
public static class AddAddressHandler
{
    [WolverinePost("/api/customers/{customerId}/addresses")]
    public static async Task<CreationResponse> Handle(
        AddAddress command,
        IDocumentSession session,
        IAddressVerificationService verificationService, // <-- DI injected
        CancellationToken ct)
    {
        // Call external service
        var verificationResult = await verificationService.VerifyAsync(
            command.AddressLine1,
            command.AddressLine2,
            command.City,
            command.StateOrProvince,
            command.PostalCode,
            command.Country,
            ct);

        // Use corrected address if available
        var finalAddress = verificationResult.SuggestedAddress ?? new CorrectedAddress(...);

        // Save with verification status
        var address = new CustomerAddress(
            addressId,
            command.CustomerId,
            command.Type,
            command.Nickname,
            finalAddress.AddressLine1,
            finalAddress.AddressLine2,
            finalAddress.City,
            finalAddress.StateOrProvince,
            finalAddress.PostalCode,
            finalAddress.Country,
            command.IsDefault,
            IsVerified: verificationResult.Status is VerificationStatus.Verified or VerificationStatus.Corrected,
            DateTimeOffset.UtcNow,
            LastUsedAt: null);

        session.Store(address);
        await session.SaveChangesAsync(ct);

        return new CreationResponse($"/api/customers/{command.CustomerId}/addresses/{addressId}");
    }
}
```

### Graceful Degradation

**Never let external service failures block critical customer workflows.** Implement fallback strategies:

```csharp
public async Task<AddressVerificationResult> VerifyAsync(...)
{
    try
    {
        var response = await _httpClient.PostAsJsonAsync(...);
        response.EnsureSuccessStatusCode();

        // Parse and return successful verification
        return new AddressVerificationResult(...);
    }
    catch (HttpRequestException ex)
    {
        // Service unavailable - fallback to unverified
        return new AddressVerificationResult(
            VerificationStatus.Unverified,
            $"Verification service unavailable: {ex.Message}",
            SuggestedAddress: null,
            ConfidenceScore: null);
    }
}
```

**In handlers:**
```csharp
// If verification fails, still save the address as unverified
IsVerified: verificationResult.Status is VerificationStatus.Verified or VerificationStatus.Corrected
```

### Configuration Management

Store API keys and service URLs in `appsettings.json`, not in code:

**appsettings.json (production):**
```json
{
  "SmartyStreets": {
    "AuthId": "your-auth-id",
    "AuthToken": "your-auth-token",
    "BaseUrl": "https://us-street.api.smartystreets.com"
  }
}
```

**appsettings.Development.json:**
```json
{
  "UseStubServices": true
}
```

### Testing External Services

**Unit Tests:** Test stub implementations to verify contracts:
```csharp
[Fact]
public async Task StubService_AlwaysReturnsVerified()
{
    var service = new StubAddressVerificationService();

    var result = await service.VerifyAsync(...);

    result.Status.ShouldBe(VerificationStatus.Verified);
    result.ConfidenceScore.ShouldBe(1.0);
}
```

**Integration Tests:** Use stub services by default - tests shouldn't depend on external APIs:
```csharp
// Test fixture registers stub
builder.Services.AddSingleton<IAddressVerificationService, StubAddressVerificationService>();

[Fact]
public async Task AddAddress_WithValidAddress_MarksAsVerified()
{
    var command = new AddAddress(...);

    var response = await _host.Scenario(s =>
    {
        s.Post.Json(command).ToUrl($"/api/customers/{customerId}/addresses");
        s.StatusCodeShouldBe(201);
    });

    var saved = await _session.LoadAsync<CustomerAddress>(addressId);
    saved.IsVerified.ShouldBeTrue(); // Stub returns verified
}
```

### Key Benefits

1. **Testability**: Stub services eliminate external dependencies in tests
2. **Configurability**: Swap implementations without code changes (DI registration)
3. **Resilience**: Graceful degradation prevents service outages from blocking customers
4. **Separation of Concerns**: Domain logic remains pure; external calls are isolated
5. **Development Speed**: Stub services work offline without API keys

## HTTP Endpoint Naming Conventions

CritterSupply follows **flat, resource-centric** HTTP endpoint patterns that align with RESTful best practices and provide consistency across all bounded contexts.

### Standard Pattern: `/api/{resource}/*`

**All HTTP endpoints follow this convention:**
- Resources are top-level paths (not nested under bounded context names)
- Resource names are plural nouns
- BC ownership is an internal implementation detail, not exposed in URLs

**Examples from our codebase:**
```
Shopping BC:     /api/carts/{cartId}
Orders BC:       /api/orders/{orderId}
                 /api/checkouts/{checkoutId}
Payments BC:     /api/payments/{paymentId}
Inventory BC:    /api/inventory/products/{productId}
Fulfillment BC:  /api/fulfillment/shipments/{shipmentId}
```

### Why Flat Over Nested?

**❌ Avoid nested paths like `/api/orders/{orderId}/checkouts/{checkoutId}`:**
- Implies Checkout is subordinate to Order (but Checkout *creates* the Order)
- Longer, more complex URLs
- Tight coupling between resources in the URL structure
- Violates REST principle of treating resources independently

**✅ Prefer flat paths:**
- Shorter, cleaner URLs
- Resources are independent (better DX for frontend developers)
- BC boundaries remain internal concerns (HTTP clients don't need to know which BC owns which resource)
- Aligns with ASP.NET Core conventions and Microsoft's API design guidance

### Resource Lifecycle Independence

Checkout comes **before** Order in the workflow, so they should be independent resources:

```csharp
// Checkout process (no Order exists yet)
POST   /api/checkouts                    // Start checkout
PUT    /api/checkouts/{checkoutId}/shipping
PUT    /api/checkouts/{checkoutId}/payment
POST   /api/checkouts/{checkoutId}/complete  // Creates Order

// Order tracking (after checkout completes)
GET    /api/orders/{orderId}             // Track order
GET    /api/orders/{orderId}/status
```

### When to Deviate

**One level of nesting is acceptable when:**
- A sub-resource is truly subordinate and cannot exist independently
- Example: `/api/fulfillment/shipments/*` (shipments are fulfillment-specific)

**Avoid deeper nesting:**
- `/api/orders/123/items/456/details` ❌ Too deep
- `/api/orders/123/items` ✅ Better: `/api/order-items?orderId=123`

### Consistency Over Perfection

If you're unsure, **choose the pattern that matches existing endpoints**. Consistency across the codebase is more valuable than theoretical purity. If we need to adjust our naming schema later, it's a straightforward search-and-replace operation.

## Backend-for-Frontend (BFF) Pattern

When building customer-facing frontends (web, mobile) that interact with multiple bounded contexts, use the **Backend-for-Frontend (BFF)** pattern to keep UI orchestration concerns separate from domain logic.

### What is a BFF?

A BFF is an intermediate layer between the frontend and domain BCs that:
- **Composes** data from multiple BCs into frontend-optimized view models
- **Orchestrates** queries and commands across BCs for UI workflows
- **Aggregates** domain events into real-time notifications for connected clients
- **Does NOT contain domain logic** - it delegates to domain BCs

### Customer Experience BC (Storefront BFF)

CritterSupply's customer-facing web store is implemented as a separate BC using the BFF pattern. See `CONTEXTS.md` for complete specification.

**Project Structure:**
```
src/
  Customer Experience/
    Storefront/                 # BFF domain (view composition, SignalR hub)
      Composition/              # View model composition from multiple BCs
      Notifications/            # SignalR hub + integration message handlers
      Queries/                  # BFF query handlers
      Commands/                 # BFF command handlers (delegation)
      Clients/                  # HTTP clients for domain BC queries
    Storefront.Web/             # Blazor Server app
      Pages/                    # Razor pages
      Components/               # Reusable Blazor components
```

### BFF Principles

**1. Composition Over Domain Logic**

BFFs compose data from domain BCs - they do NOT contain business rules.

```csharp
// Good - BFF composes view from multiple BCs
public static class GetCheckoutViewHandler
{
    [WolverineGet("/api/storefront/checkout/{checkoutId}")]
    public static async Task<CheckoutView> Handle(
        GetCheckoutView query,
        IOrdersClient ordersClient,
        ICustomerIdentityClient identityClient,
        CancellationToken ct)
    {
        // Query Orders BC
        var checkout = await ordersClient.GetCheckoutAsync(query.CheckoutId, ct);

        // Query Customer Identity BC
        var addresses = await identityClient.GetCustomerAddressesAsync(
            checkout.CustomerId,
            AddressType.Shipping,
            ct);

        // Compose view model optimized for frontend
        return new CheckoutView(
            checkout.CheckoutId,
            checkout.Items,
            addresses);
    }
}
```

```csharp
// Bad - BFF contains domain logic (belongs in Orders BC)
public static class CompleteCheckoutHandler
{
    public static async Task Handle(CompleteCheckout command, ...)
    {
        // BAD - Validation logic should be in Orders BC
        if (command.Items.Count == 0)
            throw new InvalidOperationException("Cannot checkout empty cart");

        // BAD - Domain rule enforcement should be in Orders BC
        if (command.TotalAmount < 0)
            throw new InvalidOperationException("Total cannot be negative");

        // Good - Delegation to domain BC
        await ordersClient.CompleteCheckoutAsync(command);
    }
}
```

**2. Real-Time Updates with SignalR**

BFFs subscribe to integration messages from domain BCs and push real-time updates to connected clients via SignalR.

```csharp
// Storefront/Notifications/CartUpdateNotifier.cs
public static class CartUpdateNotifier
{
    // Handler for Shopping BC integration message
    public static async Task Handle(
        Shopping.ItemAdded message,
        IHubContext<StorefrontHub> hubContext,
        IShoppingClient shoppingClient,
        CancellationToken ct)
    {
        // Query Shopping BC for updated cart state
        var cart = await shoppingClient.GetCartAsync(message.CartId, ct);

        // Compose view model
        var cartSummary = new CartSummaryView(
            cart.Id,
            cart.Items.Count,
            cart.TotalAmount);

        // Push to connected clients via SignalR
        await hubContext.Clients
            .Group($"cart:{message.CartId}")
            .SendAsync("CartUpdated", cartSummary, ct);
    }
}
```

**3. View Models are UI-Optimized, Not Domain-Pure**

BFF view models prioritize frontend performance and usability over domain model purity.

```csharp
// Good - View model optimized for checkout wizard UI
public sealed record CheckoutView(
    Guid CheckoutId,
    Guid CustomerId,
    CheckoutStatus Status,
    List<LineItemSummary> Items,        // Flattened for display
    decimal TotalAmount,                 // Pre-calculated
    List<AddressSummary> Addresses,     // Display strings ready
    bool CanComplete);                   // UI state flag

// Good - Display-optimized nested type
public sealed record LineItemSummary(
    string Sku,
    string ProductName,                  // Joined from Catalog BC
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,                   // Pre-calculated
    bool IsInStock);                     // From Inventory BC
```

### Blazor + Wolverine Integration

**Dependency Injection:**

Wolverine handlers can inject `IHubContext<T>` for SignalR notifications.

```csharp
// Program.cs - Storefront.Web
builder.Services.AddSignalR();

builder.Host.UseWolverine(opts =>
{
    // Wolverine can inject IHubContext<StorefrontHub>
    opts.Services.AddSingleton<IHubContext<StorefrontHub>>();

    // Subscribe to integration messages from domain BCs
    opts.ListenToRabbitQueue("storefront-notifications");
});

app.MapHub<StorefrontHub>("/storefronthub");
```

**Blazor Component with Real-Time Updates:**

```razor
@page "/cart/{cartId:guid}"
@inject IStorefrontClient StorefrontClient
@inject NavigationManager Navigation
@implements IAsyncDisposable

<h1>Shopping Cart</h1>

@if (cart is null)
{
    <p>Loading...</p>
}
else
{
    <CartSummary Cart="@cart" />
    <button @onclick="Checkout">Proceed to Checkout</button>
}

@code {
    [Parameter] public Guid CartId { get; set; }

    private HubConnection? hubConnection;
    private CartView? cart;

    protected override async Task OnInitializedAsync()
    {
        // Initial load from BFF
        cart = await StorefrontClient.GetCartViewAsync(CartId);

        // Connect to SignalR hub
        hubConnection = new HubConnectionBuilder()
            .WithUrl(Navigation.ToAbsoluteUri("/storefronthub"))
            .Build();

        // Subscribe to real-time updates
        hubConnection.On<CartSummaryView>("CartUpdated", async (updatedCart) =>
        {
            cart = await StorefrontClient.GetCartViewAsync(CartId);
            StateHasChanged(); // Trigger re-render
        });

        await hubConnection.StartAsync();
        await hubConnection.InvokeAsync("SubscribeToCart", CartId);
    }

    public async ValueTask DisposeAsync()
    {
        if (hubConnection is not null)
            await hubConnection.DisposeAsync();
    }
}
```

### When NOT to Use BFF

**Avoid BFF for:**
- Internal admin tools (direct BC access acceptable)
- Simple CRUD apps with single BC (unnecessary complexity)
- APIs consumed by other backend services (use direct BC-to-BC communication)

**Use BFF for:**
- Customer-facing web/mobile apps querying multiple BCs
- Real-time notification requirements (SignalR, WebSockets)
- Different client types with different composition needs (web vs mobile)

### BFF Testing Strategy

**Integration Tests Only:**

BFFs don't contain domain logic, so unit tests provide little value. Focus on integration tests using Alba.

```csharp
[Fact]
public async Task GetCheckoutView_ComposesFromMultipleBCs()
{
    // Arrange
    var checkoutId = Guid.NewGuid();
    var customerId = Guid.NewGuid();

    // Seed Orders BC with checkout data
    await _ordersClient.CreateCheckoutAsync(checkoutId, customerId, ...);

    // Seed Customer Identity BC with addresses
    await _identityClient.AddAddressAsync(customerId, ...);

    // Act - Query BFF composition endpoint
    var result = await _host.Scenario(s =>
    {
        s.Get.Url($"/api/storefront/checkout/{checkoutId}");
        s.StatusCodeShouldBe(200);
    });

    var view = result.ReadAsJson<CheckoutView>();

    // Assert - View model contains composed data
    view.CheckoutId.ShouldBe(checkoutId);
    view.Items.ShouldNotBeEmpty();          // From Orders BC
    view.Addresses.ShouldNotBeEmpty();      // From Customer Identity BC
}
```

## Entity Framework Core + Wolverine Integration

While most of CritterSupply uses Marten (event sourcing + document store), **Customer Identity BC uses Entity Framework Core** to demonstrate relational modeling and showcase Wolverine's EF Core integration. This section documents patterns for using EF Core with Wolverine.

### When to Use EF Core vs Marten

**Use EF Core when:**
- Traditional relational model fits naturally (Customer → Addresses with foreign keys)
- Navigation properties simplify queries (joins, includes)
- Foreign key constraints enforce referential integrity
- Schema evolution via migrations
- Team is more familiar with EF Core patterns
- Current state is all that matters (no need for event history)

**Use Marten when:**
- Event sourcing is beneficial (Orders, Payments, Inventory, Fulfillment)
- Document model fits (flexible schema, JSON storage)
- High-performance queries with JSONB indexes
- No complex relational joins needed
- Projection-based read models
- Event history is valuable for audit, replay, or temporal queries

### Package Dependencies: EF Core vs Marten

Wolverine provides separate packages for EF Core and Marten integration. **Choose the appropriate package based on your persistence strategy:**

**For EF Core (Customer Identity BC):**
```xml
<ItemGroup>
    <!-- Core Wolverine packages -->
    <PackageReference Include="WolverineFx.Http.FluentValidation" />
    <PackageReference Include="WolverineFx.RabbitMQ" />

    <!-- EF Core integration -->
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="WolverineFx.EntityFrameworkCore" />
</ItemGroup>
```

**For Marten (Orders, Payments, Inventory, Fulfillment BCs):**
```xml
<ItemGroup>
    <!-- Core Wolverine packages -->
    <PackageReference Include="WolverineFx.Http.FluentValidation" />
    <PackageReference Include="WolverineFx.RabbitMQ" />

    <!-- Marten integration (includes event sourcing + document store) -->
    <PackageReference Include="WolverineFx.Http.Marten" />
</ItemGroup>
```

**Key Differences:**
- `WolverineFx.Http.Marten` - Provides Marten-specific HTTP endpoint helpers (e.g., `[AggregateHandler]`, `IStartStream`, event sourcing workflow)
- `WolverineFx.EntityFrameworkCore` - Provides EF Core-specific features (e.g., automatic `DbContext` injection, transactional middleware)
- Both use the same core `WolverineFx.Http` for HTTP endpoints, so handler patterns remain consistent
- `WolverineFx.Http.Marten` transitively includes Marten packages (no need to reference Marten separately)
- EF Core requires explicit `Microsoft.EntityFrameworkCore.*` package references

**Common Packages (Both Approaches):**
- `WolverineFx.Http.FluentValidation` - FluentValidation integration for command validation
- `WolverineFx.RabbitMQ` - RabbitMQ messaging for cross-BC communication
- `Microsoft.AspNetCore.OpenApi` / `Swashbuckle.AspNetCore` - API documentation

### Customer Identity BC: EF Core Example

Customer Identity is the reference implementation for EF Core + Wolverine patterns in CritterSupply.

**Entity Model:**
```csharp
// Customer.cs - Aggregate root
public sealed class Customer
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    // Navigation property (EF Core one-to-many)
    public ICollection<CustomerAddress> Addresses { get; private set; } = new List<CustomerAddress>();

    // Required by EF Core
    private Customer() { }

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

    public CustomerAddress AddAddress(
        AddressType type,
        string nickname,
        string addressLine1,
        string city,
        string postcode,
        string country)
    {
        var address = CustomerAddress.Create(Id, type, nickname, addressLine1, city, postcode, country);
        Addresses.Add(address);
        return address;
    }
}

// CustomerAddress.cs - Entity with foreign key
public sealed class CustomerAddress
{
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }  // Foreign key
    public AddressType Type { get; private set; }
    public string Nickname { get; private set; } = string.Empty;
    public string AddressLine1 { get; private set; } = string.Empty;
    public string? AddressLine2 { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string Postcode { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public bool IsDefault { get; private set; }
    public bool IsVerified { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Navigation property (back to Customer)
    public Customer Customer { get; private set; } = null!;

    // Required by EF Core
    private CustomerAddress() { }

    internal static CustomerAddress Create(
        Guid customerId,
        AddressType type,
        string nickname,
        string addressLine1,
        string city,
        string postcode,
        string country)
    {
        return new CustomerAddress
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            Type = type,
            Nickname = nickname,
            AddressLine1 = addressLine1,
            City = city,
            Postcode = postcode,
            Country = country,
            IsDefault = false,
            IsVerified = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkAsVerified()
    {
        IsVerified = true;
    }

    public void SetAsDefault()
    {
        IsDefault = true;
    }
}
```

**DbContext Configuration:**
```csharp
// CustomerIdentityDbContext.cs
public class CustomerIdentityDbContext : DbContext
{
    public CustomerIdentityDbContext(DbContextOptions<CustomerIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> Addresses => Set<CustomerAddress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Email)
                .IsRequired()
                .HasMaxLength(256);

            entity.HasIndex(c => c.Email)
                .IsUnique();

            entity.Property(c => c.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(c => c.LastName)
                .IsRequired()
                .HasMaxLength(100);

            // One-to-many relationship
            entity.HasMany(c => c.Addresses)
                .WithOne(a => a.Customer)
                .HasForeignKey(a => a.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerAddress>(entity =>
        {
            entity.HasKey(a => a.Id);

            entity.Property(a => a.Nickname)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(a => a.AddressLine1)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(a => a.City)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(a => a.Postcode)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(a => a.Country)
                .IsRequired()
                .HasMaxLength(2);  // ISO 3166-1 alpha-2

            // Unique constraint (customer can't have duplicate nicknames)
            entity.HasIndex(a => new { a.CustomerId, a.Nickname })
                .IsUnique();
        });
    }
}
```

**Program.cs Configuration:**
```csharp
// Configure EF Core with Postgres
builder.Services.AddDbContext<CustomerIdentityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("CustomerIdentity")));

// Wolverine can inject DbContext into handlers
builder.Host.UseWolverine(opts =>
{
    // EF Core transactional middleware (optional - auto-saves changes)
    opts.Policies.AutoApplyTransactions();

    // Wolverine discovers handlers in assembly
    opts.Discovery.IncludeAssembly(typeof(CustomerIdentityDbContext).Assembly);
});
```

**Wolverine Handler with EF Core:**
```csharp
// AddAddress.cs
public sealed record AddAddress(
    Guid CustomerId,
    AddressType Type,
    string Nickname,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string Postcode,
    string Country,
    bool IsDefault)
{
    public class AddAddressValidator : AbstractValidator<AddAddress>
    {
        public AddAddressValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.Nickname).NotEmpty().MaximumLength(50);
            RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(200);
            RuleFor(x => x.City).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Postcode).NotEmpty().MaximumLength(20);
            RuleFor(x => x.Country).NotEmpty().Length(2);
        }
    }
}

public static class AddAddressHandler
{
    // Wolverine injects DbContext (same pattern as IDocumentSession)
    public static async Task<ProblemDetails> Before(
        AddAddress command,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        // Load customer with addresses (Include for navigation property)
        var customer = await dbContext.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == command.CustomerId, ct);

        if (customer is null)
            return new ProblemDetails
            {
                Detail = "Customer not found",
                Status = 404
            };

        // Check nickname uniqueness
        if (customer.Addresses.Any(a => a.Nickname == command.Nickname))
            return new ProblemDetails
            {
                Detail = $"Address with nickname '{command.Nickname}' already exists",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/customers/{customerId}/addresses")]
    public static async Task<CreationResponse> Handle(
        AddAddress command,
        CustomerIdentityDbContext dbContext,
        IAddressVerificationService verificationService,
        CancellationToken ct)
    {
        // Load customer (already validated in Before)
        var customer = await dbContext.Customers
            .Include(c => c.Addresses)
            .FirstAsync(c => c.Id == command.CustomerId, ct);

        // Verify address
        var verificationResult = await verificationService.VerifyAsync(
            command.AddressLine1,
            command.AddressLine2,
            command.City,
            command.Postcode,
            command.Country,
            ct);

        // Add address to customer (navigation property)
        var address = customer.AddAddress(
            command.Type,
            command.Nickname,
            verificationResult.SuggestedAddress?.AddressLine1 ?? command.AddressLine1,
            command.City,
            command.Postcode,
            command.Country);

        if (verificationResult.Status is VerificationStatus.Verified or VerificationStatus.Corrected)
        {
            address.MarkAsVerified();
        }

        // If IsDefault, unset other defaults
        if (command.IsDefault)
        {
            foreach (var addr in customer.Addresses.Where(a => a.Type == command.Type && a.Id != address.Id))
            {
                // EF Core tracks changes automatically
                addr.SetAsDefault(false);
            }
            address.SetAsDefault();
        }

        // SaveChanges persists all changes (Customer + Addresses)
        await dbContext.SaveChangesAsync(ct);

        return new CreationResponse($"/api/customers/{command.CustomerId}/addresses/{address.Id}");
    }
}
```

**Query with Navigation Properties:**
```csharp
// GetCustomerAddresses.cs
public sealed record GetCustomerAddresses(
    Guid CustomerId,
    AddressType? Type = null);

public static class GetCustomerAddressesHandler
{
    [WolverineGet("/api/customers/{customerId}/addresses")]
    public static async Task<List<AddressSummary>> Handle(
        Guid customerId,
        AddressType? type,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        // Query with Include (navigation property)
        var query = dbContext.Addresses
            .Where(a => a.CustomerId == customerId);

        if (type.HasValue)
            query = query.Where(a => a.Type == type.Value);

        var addresses = await query
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Nickname)
            .Select(a => new AddressSummary(
                a.Id,
                a.Nickname,
                $"{a.AddressLine1}, {a.City}, {a.Postcode}",
                a.IsDefault,
                a.IsVerified))
            .ToListAsync(ct);

        return addresses;
    }
}
```

### EF Core Migrations

Use EF Core migrations for schema evolution:

```bash
# Create initial migration
dotnet ef migrations add InitialCreate --project src/Customer\ Identity/Customers

# Apply migrations
dotnet ef database update --project src/Customer\ Identity/Customers

# Add new migration when schema changes
dotnet ef migrations add AddLastUsedAtColumn --project src/Customer\ Identity/Customers
```

**Migration Example:**
```csharp
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Customers",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Email = table.Column<string>(maxLength: 256, nullable: false),
                FirstName = table.Column<string>(maxLength: 100, nullable: false),
                LastName = table.Column<string>(maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Customers", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Addresses",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                CustomerId = table.Column<Guid>(nullable: false),
                Type = table.Column<int>(nullable: false),
                Nickname = table.Column<string>(maxLength: 50, nullable: false),
                AddressLine1 = table.Column<string>(maxLength: 200, nullable: false),
                // ... more columns
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Addresses", x => x.Id);
                table.ForeignKey(
                    name: "FK_Addresses_Customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "Customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Customers_Email",
            table: "Customers",
            column: "Email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Addresses_CustomerId_Nickname",
            table: "Addresses",
            columns: new[] { "CustomerId", "Nickname" },
            unique: true);
    }
}
```

### EF Core Testing with Alba + TestContainers

Integration tests work seamlessly with EF Core:

```csharp
// CustomerIdentityTestFixture.cs
public class CustomerIdentityTestFixture : IAsyncLifetime
{
    private IAlbaHost _host = null!;
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        // Start Postgres container
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .Build();

        await _postgres.StartAsync();

        // Configure Alba with EF Core
        _host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace connection string with TestContainers connection
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

// Integration test
public class AddAddressTests : IClassFixture<CustomerIdentityTestFixture>
{
    [Fact]
    public async Task AddAddress_CreatesAddress()
    {
        var customerId = Guid.NewGuid();

        // Seed customer
        using (var scope = _host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CustomerIdentityDbContext>();
            var customer = Customer.Create("test@example.com", "John", "Doe");
            dbContext.Customers.Add(customer);
            await dbContext.SaveChangesAsync();
        }

        // Execute command
        var command = new AddAddress(customerId, AddressType.Shipping, "Home", ...);

        var response = await _host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl($"/api/customers/{customerId}/addresses");
            s.StatusCodeShouldBe(201);
        });

        // Verify in database
        using (var scope = _host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CustomerIdentityDbContext>();
            var addresses = await dbContext.Addresses
                .Where(a => a.CustomerId == customerId)
                .ToListAsync();

            addresses.Count.ShouldBe(1);
            addresses.First().Nickname.ShouldBe("Home");
        }
    }
}
```

### Key Takeaways: EF Core + Wolverine

1. **DbContext Injection** - Wolverine injects `DbContext` into handlers like any other dependency
2. **Navigation Properties** - Use `Include()` for eager loading, simplifies queries
3. **Change Tracking** - EF Core automatically tracks entity changes, call `SaveChangesAsync()`
4. **Migrations** - Use EF Core migrations for schema evolution (versioned, repeatable)
5. **Foreign Keys** - Database-level referential integrity (cascade deletes)
6. **Testing** - Alba + TestContainers work seamlessly with EF Core

## Marten Document Store (Non-Event-Sourced) + Wolverine Integration

Some BCs use Marten as a **document database** (like MongoDB) rather than an event store. This is appropriate for **master data** or **read-heavy** use cases where event sourcing doesn't provide value.

### When to Use Marten Document Store vs Event Sourcing

**Use Marten Document Store (NOT Event Sourcing) when:**
- Master data with infrequent changes (Product Catalog)
- Current state is all that matters (no need for historical replay)
- Read-heavy workload (90%+ reads, few writes)
- Document model fits naturally (flexible schema, nested objects)
- No complex business logic requiring event replay

**Use Marten Event Store when:**
- Transaction data with frequent state changes (Orders, Payments, Inventory)
- Historical changes are valuable (audit trail, temporal queries, event replay)
- Complex business logic benefits from event sourcing patterns
- Saga orchestration required

**Examples in CritterSupply:**
- **Document Store**: Product Catalog (products are master data, changes infrequent)
- **Event Store**: Orders, Payments, Inventory, Fulfillment (transactional, event-driven)

### Marten Document Store Patterns

**1. Document Model with Immutability**

Use immutable records with factory methods, just like event-sourced aggregates:

```csharp
// Good - Immutable document model with value objects
public sealed record Product
{
    public Sku Sku { get; init; } = null!;  // Document identifier (value object)
    public ProductName Name { get; init; } = null!;  // Value object with validation
    public string Description { get; init; } = null!;
    public CategoryName Category { get; init; } = null!;  // Value object
    public IReadOnlyList<ProductImage> Images { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public ProductStatus Status { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset AddedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    // Required by Marten
    private Product() { }

    // Factory method for creation - accepts plain strings, converts to value objects
    public static Product Create(
        string sku,
        string name,
        string description,
        string category,
        IReadOnlyList<ProductImage> images)
    {
        return new Product
        {
            Sku = Sku.From(sku),              // Validates format (uppercase, max 24 chars)
            Name = ProductName.From(name),    // Validates format (max 100 chars, allowed chars)
            Description = description,
            Category = CategoryName.From(category),
            Images = images,
            Tags = [],
            Status = ProductStatus.Active,
            IsDeleted = false,
            AddedAt = DateTimeOffset.UtcNow
        };
    }

    // Update methods using 'with' syntax
    public Product Update(
        string? name = null,
        string? description = null,
        string? category = null)
    {
        return this with
        {
            Name = name is not null ? ProductName.From(name) : Name,
            Description = description ?? Description,
            Category = category is not null ? CategoryName.From(category) : Category,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public Product ChangeStatus(ProductStatus newStatus)
    {
        return this with
        {
            Status = newStatus,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public Product SoftDelete()
    {
        return this with
        {
            IsDeleted = true,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
```

**2. Marten Configuration for Document Store**

```csharp
// Program.cs
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);

    // Document storage configuration (NOT event sourcing)
    opts.Schema.For<Product>()
        .Identity(x => x.Sku)  // Use SKU as identifier (not Guid)
        .UniqueIndex(x => x.Sku)  // Enforce uniqueness
        .Index(x => x.Category)  // Index for category queries
        .Index(x => x.Status)  // Index for status filtering
        .SoftDeleted();  // Built-in soft delete support
});
```

**3. Handler Pattern for Document CRUD**

```csharp
// AddProduct.cs
public sealed record AddProduct(
    string Sku,
    string Name,
    string Description,
    CategoryName Category,
    IReadOnlyList<ProductImage> Images)
{
    public class AddProductValidator : AbstractValidator<AddProduct>
    {
        public AddProductValidator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Images).NotEmpty();
        }
    }
}

public static class AddProductHandler
{
    [WolverinePost("/api/products")]
    public static async Task<CreationResponse> Handle(
        AddProduct command,
        IDocumentSession session,  // Document session (NOT event store)
        CancellationToken ct)
    {
        // Check for duplicate SKU
        var existing = await session.Query<Product>()
            .FirstOrDefaultAsync(p => p.Sku == command.Sku, ct);

        if (existing is not null)
            return new CreationResponse($"/api/products/{command.Sku}")
            {
                // Return 409 Conflict
            };

        // Create document
        var product = Product.Create(
            command.Sku,
            command.Name,
            command.Description,
            command.Category,
            command.Images);

        session.Store(product);  // Direct document insert (NOT StartStream)
        await session.SaveChangesAsync(ct);

        // Publish integration message (NOT domain event)
        // TODO: Publish ProductAdded integration message

        return new CreationResponse($"/api/products/{command.Sku}");
    }
}
```

**4. Query Patterns**

```csharp
// GetProductListing.cs
public sealed record GetProductListing(
    CategoryName? Category = null,
    ProductStatus? Status = null,
    int PageNumber = 1,
    int PageSize = 20);

public static class GetProductListingHandler
{
    [WolverineGet("/api/products")]
    public static async Task<PagedResult<ProductSummary>> Handle(
        GetProductListing query,
        IDocumentSession session,
        CancellationToken ct)
    {
        var productsQuery = session.Query<Product>()
            .Where(p => !p.IsDeleted);  // Soft delete filter

        if (query.Category is not null)
            productsQuery = productsQuery.Where(p => p.Category == query.Category);

        if (query.Status.HasValue)
            productsQuery = productsQuery.Where(p => p.Status == query.Status.Value);

        var total = await productsQuery.CountAsync(ct);

        var products = await productsQuery
            .OrderBy(p => p.Name)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new ProductSummary(p.Sku, p.Name, p.Category, p.Status))
            .ToListAsync(ct);

        return new PagedResult<ProductSummary>(products, total, query.PageNumber, query.PageSize);
    }
}
```

**5. Update Patterns**

```csharp
// UpdateProduct.cs
public static class UpdateProductHandler
{
    [WolverinePut("/api/products/{sku}")]
    public static async Task Handle(
        string sku,
        UpdateProduct command,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Load document
        var product = await session.LoadAsync<Product>(sku, ct);

        if (product is null)
            return; // 404 Not Found

        // Update using immutable pattern
        var updated = product.Update(
            name: command.Name,
            description: command.Description,
            category: command.Category);

        // Store updated document (Marten detects changes)
        session.Store(updated);
        await session.SaveChangesAsync(ct);

        // Publish integration message
        // TODO: Publish ProductUpdated integration message
    }
}
```

### Key Differences: Document Store vs Event Sourcing

| Aspect | Document Store | Event Sourcing |
|--------|---------------|----------------|
| **Session Type** | `IDocumentSession` | `IDocumentSession` (same interface) |
| **Write Operation** | `session.Store(document)` | `session.Events.Append(aggregateId, event)` |
| **Read Operation** | `session.Query<T>()` or `session.LoadAsync<T>(id)` | `session.Events.AggregateStreamAsync<T>(id)` |
| **Identity** | Any type (string, Guid, int) | Stream ID (typically Guid) |
| **Persistence** | Document stored directly (like MongoDB) | Events stored, aggregate rebuilt on read |
| **Updates** | In-place document updates | Append new events to stream |
| **History** | No historical changes | Full event history preserved |
| **Use Cases** | Master data, read-heavy, infrequent changes | Transactional data, event-driven logic |

### Key Takeaways: Marten Document Store + Wolverine

1. **IDocumentSession Injection** - Same DI pattern as event sourcing, different usage
2. **Immutability** - Use immutable records with `with` syntax, even for document models
3. **Factory Methods** - `Create()` for construction, `Update()` methods for changes
4. **Soft Delete** - Use Marten's `.SoftDeleted()` configuration for built-in filtering
5. **Indexes** - Configure indexes for query performance (category, status, etc.)
6. **String Identifiers** - Documents can use human-readable IDs (SKU) instead of GUIDs
7. **Integration Messages** - Publish messages to notify other BCs (not persisted as domain events)
8. **Testing** - Same Alba + TestContainers pattern as event-sourced BCs

## Testing Principles

In general, prefer integration tests over unit tests. The latter have their place and importance, but we want to leverage tools like Alba and TestContainers to go through the use cases our vertical slices are built to fulfill.

Thanks to the Wolverine framework and it "getting out of the way" of development by following A-Frame and Railway principles, most the business logic we need to test are in a pure function. This makes unit tests extremely simple since various dependencies are decoupled, such as having validation and pre-loading of data entities through a `Before()` or `Validate()` method that Wolverine automatically runs before the `Handle()` method. Unit tests can focus on the validation or the actual decision-making process involved with the business process. Likewise, integration tests will cover entire slices, or use cases, with a simple call thanks to Alba invoking our systems' endpoints, regardless if they're HTTP or message based.

### Testing Tools

- **[xUnit](https://github.com/xunit/xunit.net/tree/main)** for testing framework
- **[Shouldly](https://github.com/shouldly/shouldly)** for readable assertions
- **[NSubstitute](https://github.com/nsubstitute/NSubstitute)** for mocking, only when it's necessary, as we prefer real implementations
- **[Alba](https://github.com/JasperFx/alba)** for integration testing and scenario configuration
- **[Testcontainers](https://github.com/testcontainers/testcontainers-dotnet)** to support tests with throwaway instances of Docker containers

### C# Standards for Test Code

All test code must follow the same C# standards as production code.

### BDD-Style Testing

Prefer BDD-style testing for integration tests, focusing on the behavior of the system from an outside-in perspective. Use Alba to help with this style of testing.

## Cross-Context Refactoring

When performing refactoring that affects multiple bounded contexts, such as moving code between projects or updating namespaces, we want to make sure the behavior of the system remains unchanged.

### Testing Checklist

After any of the following changes, **always run the full test suite** before committing:

### When to Run All Tests
- **Adding/removing project references** between bounded contexts
- **Moving code between projects** (e.g., integration messages to shared contracts)
- **Updating namespaces** across multiple files
- **Refactoring handlers or sagas** that coordinate contexts
- **Modifying shared infrastructure** (e.g., Messages.Contracts)

### Test Execution Steps

1. Build the entire solution (catches compile errors early)
   ```bash
   dotnet build
   ```
2. Run all unit tests
   ```bash
   dotnet test --filter "Category=Unit"
   ```
3. Run all integration tests
   ```bash
   dotnet test --filter "Category=Integration"
   ```
4. Or run everything at once
   ```bash
   dotnet test
   ```
5. Verify the result

### Why This Matters
- Integration messages crossing context boundaries must be correctly referenced
- Project dependencies can create unintended coupling
- Namespace changes can break type resolution

### Exit Criteria
- ✅ Solution builds with 0 errors
- ✅ All unit tests pass
- ✅ All integration tests pass
- ✅ No unused project references remain

## Available Skills

Skills are documented separately in the `skills/` directory. Each skill provides patterns, templates, and practices for a specific aspect of building CritterSupply.

### Modern C# Coding Standards

**Purpose**: Modern C# language features, code style guidelines, and best practices for writing clean, maintainable .NET code aligned with the CritterSupply conventions.

**File**: [skills/modern-csharp-coding-standards.md](skills/modern-csharp-coding-standards.md)

### Critter Stack Testing Patterns

**Purpose**: Patterns and practices for writing unit tests and integration tests when using Wolverine and Marten, AKA The Critter Stack. Includes guidance on using the Alba integration testing library.

**File**: [skills/critterstack-testing-patterns.md](skills/critterstack-testing-patterns.md)

### TestContainers Integration Testing

**Purpose**: Patterns and practices for writing integration tests using TestContainers and xUnit to test bounded contexts with real infrastructure dependencies.

**File**: [skills/testcontainers-integration-tests.md](skills/testcontainers-integration-tests.md)

## Development Progress

The development style, current development cycle, completion status, in progress, planned work, and related details can be be found in the following document:

**File**: [Dev Progress](./DEVPROGRESS.md)
