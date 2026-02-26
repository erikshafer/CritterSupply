# GitHub Copilot Instructions for CritterSupply

CritterSupply is a reference architecture for event-driven e-commerce systems built with the **Critter Stack** (.NET, Wolverine, Marten, PostgreSQL, RabbitMQ).

## Key Libraries & Documentation

- **Wolverine** (message bus + mediator): https://wolverinefx.net
- **Marten** (document store + event sourcing): https://martendb.io — LLM full doc: https://martendb.io/llms-full.txt

When working with Wolverine handlers, aggregates, or Marten projections, fetch the Marten documentation URL above or browse the Wolverine site for complete API details.

## Architecture

- **Language:** C# 14+ (.NET 10+)
- **Testing:** xUnit, Testcontainers, Alba, Shouldly
- **Validation:** FluentValidation
- **Database:** PostgreSQL (via Marten for event sourcing/documents; EF Core for Customer Identity)
- **Messaging:** RabbitMQ via Wolverine

**Solution structure:**
```
src/<Bounded Context Name>/<ProjectName>/         # Domain + handlers
src/<Bounded Context Name>/<ProjectName>.Api/     # API host
tests/<Bounded Context Name>/<ProjectName>.IntegrationTests/
```

**Bounded contexts:** Orders, Payments, Shopping, Inventory, Fulfillment, Customer Identity, Product Catalog, Customer Experience (BFF/Storefront)

## Core Principles

- **`CONTEXTS.md` is the architectural source of truth** — always consult it for bounded context definitions, event lifecycles, and integration flows
- Pure functions for business logic (handlers should be pure; side effects at edges)
- Immutability by default — use `record`, `IReadOnlyList<T>`, `with` expressions
- All commands, queries, events, and models are `sealed`
- Integration tests over unit tests — test complete vertical slices with Alba
- A-Frame architecture — infrastructure at edges, pure logic in the middle

## Skills Documentation

Detailed patterns are in `docs/skills/`:
- `wolverine-message-handlers.md` — compound handler lifecycle, return patterns
- `marten-event-sourcing.md` — immutable aggregates, domain events, decider pattern
- `marten-document-store.md` — CRUD handlers, query patterns
- `efcore-wolverine-integration.md` — EF Core with Wolverine
- `critterstack-testing-patterns.md` — Alba integration tests, pure function testing
- `vertical-slice-organization.md` — file structure and naming conventions
- `modern-csharp-coding-standards.md` — records, immutability, value objects
