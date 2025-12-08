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

### Project Structure

```
src/
  Context/                            # This is a folder that contains the projects that belong to a particular context, value stream, or bounded context. This could be something like Order Management, Product Catalog, or Payment Processing.
    YourApp/                          # The core project with not just value objects and other primitives, but the core application logic for use cases, domain models, events, commands, queries, and the like. Minimal dependencies unless it achieves the previously mentioned core pieces for the module, such as Wolverine and Marten.
    YourApp.Api/                      # The API project that leverages the module domain logic from YourApp. The configuration for the web api, web sockets, messaging, and grpc is located here.
    YourApp.BackgroundWorker/         # The BackgroundWorker project is separate as it needs to run independently from a synchronous API. This may be used for something like a message consuming with Kafka or an independent Marten projection handler that is has additional IO operations.
tests/
  Context/                            # This mirrors the same context that are in the /src/ directory, but for their respective test projects.
    YourApp.Api.Tests/                # Most the tests. Tests concerning API, application logic, domain model, etc.
    YourApp.Tests/                    # Tests for ensuring the value objects and other low-level primitives function exactly as expected.
```

#### Avoid Having Folders Based on Technical Features

Inside our projects, we want to avoid creating folders (directories) based on technical feature (Entities, Models, Controllers, DTO's). Instead, create folders based on the actual business value that grouped set of code performs. Loosely following a vertical slice architecture style. A new developer should be able to look at the files/folders inside a project and understand what is is that the application does.

If there is a folder based on a technical feature, treat it as temporary and that its contents will be moved to a better-fitting namespace soon.

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
