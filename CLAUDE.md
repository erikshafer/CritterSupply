# Development Guidelines for CritterSupply with Claude

This repository contains production-ready skills designed specifically for use with Claude, Claude Agent, Claude Code, and other uses of "Claude" that aim to assist with software development.

These skills range from specific modern C# and .NET practices to idioms established in frameworks and libraries such as Wolverine and Marten to build robust event-driven systems.

> **Universal Applicability**: While explained using the C# programming language and .NET platform, these patterns apply to any object-oriented programming language (Java TypeScript, Python, etc.), as well as borrow concepts from functional programming languages (F#, Clojure, Elixir, etc.). Concepts, ideas, strategies, and tactics are influenced by pragmatic use of Domain-Driven Design (DDD) and Command Query Responsibility Segregation (CQRS), which are language-agnostic.

## Repository Purpose

This repository demonstrates how to build robust, production-ready, event-driven systems using a realistic e-commerce domain.

It also serves as a reference architecture for idiomatically leveraging the "Critter Stack"—[Wolverine](https://github.com/JasperFx/wolverine) and [Marten](https://github.com/JasperFx/marten)—to supercharge your .NET development. These tools just get out of your way so you can focus on the actual business problems at hand.

### 🛒 Ecommerce

CritterSupply is a fictional pet supply retailer—the name a playful nod to the Critter Stack powering it, with the tagline "Stocked for every season."

E-commerce was chosen as the domain partly from the maintainer's industry experience, but more importantly because it's a domain most developers intuitively understand. Everyone has placed an order online. That familiarity lets us focus on *how* the system is built rather than getting bogged down explaining *what* it does.

### Domain & Architecture Overview

For details on the specific bounded contexts, their responsibilities, invariants, and integration patterns within CritterSupply, see **[CONTEXTS.md](CONTEXTS.md)**. That document provides the domain-level architecture and business workflow definitions that drive the technical implementations described in this file.

## What This Repository Provides

This repository provides a reference architecture and code examples, focus on:

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

As much as possible, use "pure functions" for any business logic. In the world of C#, that means a static class with a static method that does one operate and does it well.

Using the Wolverine framework, there may also be a `Validate()` or `Before()` method before the primary method is invoked, as we want to avoid the primary method ("function") to do tasks such as making sure an entity exists or an incoming object (parameter) isn't null.

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

Likewise, for domain models, aggregates, write models, and similar types have `sealed` automatically applied. Nothing else should be inheriting them unless there is an explicit use case that has been designated.

```csharp
// Good - This query is meant for one handler and one handler only, typically
// the same name appended with "Handler" (such as GetCustomerAddressHandler)
public sealed record GetCustomerAddress(
    Guid CustomerId
);
```

#### Write Models, AKA Aggregates and Domain Models

Use `sealed` and `record` by default for types considered write models, such as aggregates, projections, and domain models. Follow Marten's event sourcing patterns for creating and applying events to these models.

```csharp
// Good - A model promoting immutability with no inheritance
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

#### Traditional Relational Data Models

While most of this system uses event sourcing, there may be cases where "traditional" models are used to persist data in a relational table. In these cases, like with our event sourcing models, we prefer immutability unless dependencies and behaviors are executed to produce a new instance of said model. If the ORM known as EF Core is being used, additional configuration may be needed to promote these behaviors.

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

### TestContainers Integration Testing

**Purpose**: Patterns and practices for writing integration tests using TestContainers and xUnit to test bounded contexts with real infrastructure dependencies.

**File**: [skills/testcontainers-integration-tests.md](skills/testcontainers-integration-tests.md)
