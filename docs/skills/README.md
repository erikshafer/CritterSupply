# CritterSupply Skills Documentation

This directory contains implementation patterns and best practices for building with the Critter Stack (Wolverine + Marten).

## 📚 Quick Navigation

### By Use Case

**Starting from Scratch:**
- 🏗️ [Adding a New Bounded Context](./adding-new-bounded-context.md) - Complete checklist: projects, Docker, Postgres database, Aspire, CONTEXTS.md, tests

**Creating Commands & Handlers:**
- 🎯 [Wolverine Message Handlers](./wolverine-message-handlers.md) - Command/query handlers, HTTP endpoints, compound handlers, return patterns
- 🔄 [Wolverine Sagas](./wolverine-sagas.md) - Stateful orchestration sagas, multi-BC coordination, compensation chains, idempotency

**Working with Aggregates:**
- 📝 [Marten Event Sourcing](./marten-event-sourcing.md) - Event-sourced aggregates, decider pattern, factory methods
- 📦 [Marten Document Store](./marten-document-store.md) - Document models, CRUD patterns, value objects for queryable fields

**Traditional Relational Data:**
- 🗄️ [EF Core + Wolverine Integration](./efcore-wolverine-integration.md) - Entity models, DbContext setup, migrations, navigation properties

**Building BFFs:**
- 🎁 [BFF Real-time Patterns](./bff-realtime-patterns.md) - View composition, HTTP clients, SignalR/SSE updates, Blazor integration, MudBlazor
- 📡 [Wolverine + SignalR](./wolverine-signalr.md) - Wolverine's native SignalR transport: hub setup, authentication (session & JWT), group routing, client integration, Marten side-effect pipeline

**Testing Your Code:**
- 🧪 [CritterStack Testing Patterns](./critterstack-testing-patterns.md) - Alba integration tests, pure function testing, TestFixture patterns
- 🐳 [TestContainers Integration Tests](./testcontainers-integration-tests.md) - Why real infrastructure, container setup, performance tips
- 🥒 [Reqnroll BDD Testing](./reqnroll-bdd-testing.md) - Gherkin features, step definitions, when to use BDD
- 🎭 [E2E Testing with Playwright](./e2e-playwright-testing.md) - Browser E2E tests, real Kestrel, POM, MudBlazor patterns

**Code Organization & Style:**
- 📂 [Vertical Slice Organization](./vertical-slice-organization.md) - File structure, colocation, project naming conventions
- 💎 [Modern C# Coding Standards](./modern-csharp-coding-standards.md) - Records, immutability, value objects, collection patterns

**Infrastructure & Setup:**
- 🏗️ [Adding a New Bounded Context](./adding-new-bounded-context.md) - Complete step-by-step guide: .NET projects, Docker, Postgres, Aspire, CONTEXTS.md, tests

**External Integrations:**
- 🔌 [External Service Integration](./external-service-integration.md) - Strategy pattern, stub vs production, graceful degradation

---

## 🛠️ By Technology

### Wolverine
- [Wolverine Message Handlers](./wolverine-message-handlers.md) - Command/query handling, HTTP endpoints
- [Wolverine Sagas](./wolverine-sagas.md) - Stateful orchestration, multi-BC coordination, compensation chains

### Marten
- [Marten Event Sourcing](./marten-event-sourcing.md) - Event-sourced aggregates, projections
- [Marten Document Store](./marten-document-store.md) - Document models, queries, value objects

### Entity Framework Core
- [EF Core + Wolverine Integration](./efcore-wolverine-integration.md) - Traditional relational persistence with Wolverine

### Testing Frameworks
- [CritterStack Testing Patterns](./critterstack-testing-patterns.md) - Alba, Wolverine, Marten integration tests
- [TestContainers Integration Tests](./testcontainers-integration-tests.md) - Real infrastructure testing
- [Reqnroll BDD Testing](./reqnroll-bdd-testing.md) - Behavior-driven development with Gherkin
- [E2E Testing with Playwright](./e2e-playwright-testing.md) - Browser E2E with real Kestrel servers

### UI Frameworks
- [BFF Real-time Patterns](./bff-realtime-patterns.md) - Blazor Server, MudBlazor, SSE/SignalR
- [Wolverine + SignalR](./wolverine-signalr.md) - Wolverine's native SignalR transport, real-time hub patterns

---

## 🎯 By Development Phase

### Planning Phase
1. **New BC?** → Start with [Adding a New Bounded Context](./adding-new-bounded-context.md) for the full checklist
2. Review [Vertical Slice Organization](./vertical-slice-organization.md) for file structure
3. Write Gherkin features (see [Reqnroll BDD Testing](./reqnroll-bdd-testing.md))
4. Choose persistence strategy:
   - Event sourcing? → [Marten Event Sourcing](./marten-event-sourcing.md)
   - Document store? → [Marten Document Store](./marten-document-store.md)
   - Traditional relational? → [EF Core + Wolverine Integration](./efcore-wolverine-integration.md)
   - Multi-BC orchestration over time? → [Wolverine Sagas](./wolverine-sagas.md)

### Implementation Phase
1. Write handlers → [Wolverine Message Handlers](./wolverine-message-handlers.md)
2. Follow coding standards → [Modern C# Coding Standards](./modern-csharp-coding-standards.md)
3. Integrate external services? → [External Service Integration](./external-service-integration.md)
4. Building BFF? → [BFF Real-time Patterns](./bff-realtime-patterns.md)
5. Adding SignalR real-time? → [Wolverine + SignalR](./wolverine-signalr.md)

### Testing Phase
1. Write integration tests → [CritterStack Testing Patterns](./critterstack-testing-patterns.md)
2. Set up TestContainers → [TestContainers Integration Tests](./testcontainers-integration-tests.md)
3. Implement BDD scenarios → [Reqnroll BDD Testing](./reqnroll-bdd-testing.md)
4. Write browser E2E tests → [E2E Testing with Playwright](./e2e-playwright-testing.md)

---

## 🔗 Related Documentation

- **[CLAUDE.md](../CLAUDE.md)** - AI development guidelines and skill invocation guide
- **[CONTEXTS.md](../CONTEXTS.md)** - Bounded context specifications (architectural source of truth)
- **[docs/decisions/](../docs/decisions/)** - Architectural Decision Records (ADRs)
- **[docs/planning/](../docs/planning/)** - Development cycles and roadmap
- **[docs/features/](../docs/features/)** - BDD feature specifications

---

## 📝 Document Conventions

Each skill document follows this structure:

1. **When to Use** - Clear guidance on when this skill applies
2. **Core Concepts** - Key patterns and principles
3. **Examples** - Code samples with explanations
4. **Common Pitfalls** - What to avoid
5. **Testing** - How to test the pattern
6. **See Also** - Links to related skills

---

## 🆘 Getting Help

**Can't find what you need?**

1. Check the [skill invocation guide in CLAUDE.md](../CLAUDE.md#skill-invocation-guide) for AI-specific guidance
2. Review [CONTEXTS.md](../CONTEXTS.md) for bounded context integration patterns
3. Search for ADRs in [docs/decisions/](../docs/decisions/) for architectural decisions

**Common Questions:**

- *"How do I build a real-time hub with SignalR and Wolverine?"* → [Wolverine + SignalR](./wolverine-signalr.md)
- *"How do I create a new command handler?"* → [Wolverine Message Handlers](./wolverine-message-handlers.md)
- *"How do I test my handler?"* → [CritterStack Testing Patterns](./critterstack-testing-patterns.md)
- *"Should I use event sourcing or document store?"* → Compare [Marten Event Sourcing](./marten-event-sourcing.md) vs [Marten Document Store](./marten-document-store.md)
- *"How do I build a saga that coordinates multiple BCs?"* → [Wolverine Sagas](./wolverine-sagas.md)
- *"How do I organize my code?"* → [Vertical Slice Organization](./vertical-slice-organization.md)
- *"When should I use EF Core?"* → [EF Core + Wolverine Integration](./efcore-wolverine-integration.md)
- *"How do I add a new bounded context?"* → [Adding a New Bounded Context](./adding-new-bounded-context.md)

---

**Last Updated:** 2026-03-06
