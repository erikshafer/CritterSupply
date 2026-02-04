# Development Guidelines for CritterSupply with Claude

This repository demonstrates how to build robust, production-ready, event-driven systems using a realistic e-commerce domain.

It serves as a reference architecture for idiomatically leveraging the "Critter Stack"—[Wolverine](https://github.com/JasperFx/wolverine) and [Marten](https://github.com/JasperFx/marten)—to supercharge your .NET development.

> **Universal Applicability**: While demonstrated in C#/.NET, these patterns apply to any language. Concepts are influenced by pragmatic Domain-Driven Design (DDD) and CQRS.

## CritterSupply

CritterSupply is a fictional pet supply retailer—the name a playful nod to the Critter Stack powering it, with the tagline "Stocked for every season."

E-commerce was chosen because it's a domain most developers intuitively understand. Nearly everyone has placed an order online. That familiarity lets us focus on *how* the system is built rather than explaining *what* it does.

## Architectural North Star

**IMPORTANT:** `CONTEXTS.md` is the **architectural source of truth** for this system's bounded context (BC) definitions, along with the lifecycles of events, core invariants, and integration flows.

When implementing integrations between bounded contexts:

1. **Always consult CONTEXTS.md first** — It defines what messages each BC receives and publishes
2. **Implementation follows specification** — Code should match the integration contracts defined there
3. **Orchestration vs. Choreography** — CONTEXTS.md specifies which pattern to use
4. **Update CONTEXTS.md when architecture changes** — Keep it current

If there's a discrepancy between code and CONTEXTS.md, **CONTEXTS.md wins**. This is why it's imperative to keep it update to date when plans, flows, behaviors, and integrations change.

## Quick References

### Preferred Tools

- **Language**: C# 14+ (.NET 10+)
- **Testing**: xUnit, Testcontainers, Alba, Shouldly
- **Validation**: FluentValidation
- **Serialization**: System.Text.Json
- **Database**: Postgres
- **Event Sourcing / Document Store**: Marten 8+
- **Message Handling**: Wolverine 5+
- **Messaging**: RabbitMQ (AMQP)

### Core Principles

- **Pure functions for business logic** — Handlers should be pure; side effects at the edges
- **Immutability by default** — Use records, `IReadOnlyList<T>`, `with` expressions
- **Sealed by default** — All commands, queries, events, and models are `sealed`
- **Integration tests over unit tests** — Test complete vertical slices with Alba
- **A-Frame architecture** — Infrastructure at edges, pure logic in the middle

## Solution Organization

```
src/
  <Bounded Context Name>/
    <ProjectName>/                    # Domain + handlers (Web SDK)
    <ProjectName>.Api/                # [Optional] Separate API project
tests/
  <Bounded Context Name>/
    <ProjectName>.IntegrationTests/
    <ProjectName>.UnitTests/          # [Optional]
```

**Real Examples:**
```
src/
  Customer Identity/Customers/
  Order Management/Orders/, Orders.Api/
  Payment Processing/Payments/, Payments.Api/
  Shared/Messages.Contracts/
```

See `skills/vertical-slice-organization.md` for complete file organization patterns.

## API Project Configuration

When creating a new API project (e.g., `Orders.Api`, `Payments.Api`), ensure these configurations are in place:

### 1. Connection String (appsettings.json)

**Standard format:**
```json
{
  "ConnectionStrings": {
    "marten": "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres"
  }
}
```

**Key Points:**
- **Host**: `localhost` (for local development with docker-compose)
- **Port**: `5433` (docker-compose maps container's 5432 → host's 5433)
- **Database**: `postgres` (single shared database)
- **Schema**: Each BC uses its own schema via `opts.DatabaseSchemaName = Constants.<BcName>.ToLowerInvariant()`

**EF Core Projects** use `"postgres"` as the connection string name instead of `"marten"`.

### 2. Launch Settings (Properties/launchSettings.json)

**Required:** Each API project MUST have a `Properties/launchSettings.json` file with a unique port.

**Standard format:**
```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "<BcName>Api": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "launchUrl": "api",
      "applicationUrl": "http://localhost:52XX",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

**Port Allocation Table** (increment by 1 for each new BC):

| BC                            | Port     | Status      |
|-------------------------------|----------|-------------|
| Orders                        | 5231     | ✅ Assigned  |
| Payments                      | 5232     | ✅ Assigned  |
| Inventory                     | 5233     | ✅ Assigned  |
| Fulfillment                   | 5234     | ✅ Assigned  |
| Customer Identity             | 5235     | ✅ Assigned  |
| Shopping                      | 5236     | ✅ Assigned  |
| Product Catalog               | 5133     | ✅ Assigned  |
| **Customer Experience (BFF)** | **5237** | 🔜 Next     |
| Vendor Portal                 | 5238     | 📋 Reserved |
| Vendor Identity               | 5239     | 📋 Reserved |

**Why this matters:**
- Allows running multiple APIs simultaneously during development
- Prevents port conflicts when debugging in IDE
- Enables easy service discovery during local integration testing

**When creating a new BC:** Check the port allocation table above and use the next available port number.

---

## Skill Invocation Guide

Skills provide detailed patterns and examples. Read the appropriate skill **before** implementing.

### When Implementing Wolverine Handlers

For message handlers, HTTP endpoints, compound handlers, or aggregate workflows:

**Read:** `skills/wolverine-message-handlers.md`

Covers:
- Compound handler lifecycle (`Before`, `Validate`, `Load`, `Handle`)
- Return patterns (`Events`, `OutgoingMessages`, `IStartStream`)
- `[WriteAggregate]` vs `Load()` pattern decision tree
- HTTP endpoint attributes

### When Working with Event Sourcing

For event-sourced aggregates, domain events, or the decider pattern:

**Read:** `skills/marten-event-sourcing.md`

Covers:
- Immutable aggregate design with `Create()` and `Apply()` methods
- Domain event structure
- Status enum patterns
- Marten projection configuration

### When Using Marten as Document Store

For non-event-sourced persistence (like Product Catalog):

**Read:** `skills/marten-document-store.md`

Covers:
- Document model design with factory methods
- CRUD handler patterns
- Query patterns with filtering/pagination
- Soft delete configuration

### When Using Entity Framework Core (EF Core)

For relational data (like Customer Identity BC):

**Read:** `skills/efcore-wolverine-integration.md`

Covers:
- Entity model design with navigation properties
- DbContext configuration and migrations
- Wolverine handler patterns with EF Core
- Testing with Alba + TestContainers

### When Integrating External Services

For payment gateways, address verification, shipping providers:

**Read:** `skills/external-service-integration.md`

Covers:
- Strategy pattern with dependency injection
- Stub vs production implementations
- Graceful degradation patterns
- Configuration management

### When Building BFF Layers

For customer-facing frontends aggregating multiple BCs:

**Read:** `skills/bff-signalr-patterns.md`

Covers:
- View composition from multiple BCs
- SignalR real-time updates
- Blazor + Wolverine integration
- When to use / not use BFF

### When Organizing Code

For file structure and vertical slice organization:

**Read:** `skills/vertical-slice-organization.md`

Covers:
- Command/Handler/Validator colocation
- File and folder naming conventions
- Solution structure mirroring BC boundaries

### For C# Standards

For language features, code style, immutability patterns:

**Read:** `skills/modern-csharp-coding-standards.md`

Covers:
- Records and immutability
- Collection patterns (`IReadOnlyList<T>`)
- Value object patterns with JSON converters
- FluentValidation nested validators

### For Testing

For unit and integration test patterns:

**Read:** `skills/critterstack-testing-patterns.md`

Covers:
- Alba integration test fixtures
- Testing pure function handlers
- TestContainers setup
- Cross-context refactoring checklist

---

## HTTP Endpoint Conventions

CritterSupply uses **flat, resource-centric** HTTP endpoints:

```
/api/carts/{cartId}
/api/orders/{orderId}
/api/payments/{paymentId}
/api/products/{sku}
```

- Resources are top-level (not nested under BC names)
- Resource names are plural nouns
- BC ownership is internal, not exposed in URLs

Avoid deep nesting: `/api/orders/{orderId}/items` → prefer `/api/order-items?orderId={orderId}`

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
- Modifying `Messages.Contracts`

**Exit criteria:**
- ✅ Solution builds with 0 errors
- ✅ All unit tests pass
- ✅ All integration tests pass

## When to Use Context7

These skills document CritterSupply's established patterns. For exploring Wolverine/Marten capabilities beyond these patterns, use Context7:

- `@context7 wolverine saga` — when evaluating saga patterns
- `@context7 marten projections` — for advanced projection types
- `@context7 wolverine http` — for HTTP endpoint features

## Development Progress

See [DEVPROGRESS.md](./DEVPROGRESS.md) for current development status.

---

## Available Skills

| Skill | Purpose |
|-------|---------|
| `wolverine-message-handlers.md` | Compound handlers, return patterns, aggregate workflows |
| `marten-event-sourcing.md` | Event-sourced aggregates, domain events, decider pattern |
| `marten-document-store.md` | Document database patterns (non-event-sourced) |
| `efcore-wolverine-integration.md` | Entity Framework Core with Wolverine |
| `external-service-integration.md` | Strategy pattern, graceful degradation |
| `bff-signalr-patterns.md` | Backend-for-Frontend, real-time updates |
| `vertical-slice-organization.md` | File structure, colocation patterns |
| `modern-csharp-coding-standards.md` | C# language features, immutability |
| `critterstack-testing-patterns.md` | Unit and integration testing |
