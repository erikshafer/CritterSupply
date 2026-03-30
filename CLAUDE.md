# Development Guidelines for CritterSupply with Claude

This repository demonstrates how to build robust, production-ready, event-driven systems using a realistic e-commerce domain.

It serves as a reference architecture for idiomatically leveraging the "Critter Stack"—[Wolverine](https://github.com/JasperFx/wolverine) and [Marten](https://github.com/JasperFx/marten)—to supercharge your .NET development.

> **Universal Applicability**: While demonstrated in C#/.NET, these patterns apply to any language. Concepts are influenced by pragmatic Domain-Driven Design (DDD) and CQRS.

---

## 📋 Table of Contents

- [Quick Start (First 5 Minutes)](#quick-start-first-5-minutes)
- [CritterSupply Overview](#critterSupply)
- [Architectural North Star](#architectural-north-star)
- [Documentation Hierarchy](#documentation-hierarchy)
- [Quick Reference: Common Tasks](#quick-reference-common-tasks)
- [Glossary](#glossary)
- [Documentation Structure](#documentation-structure)
- [Quick References](#quick-references)
- [Solution Organization](#solution-organization)
- [API Project Configuration](#api-project-configuration)
- [Local Development Options](#local-development-options)
- [How Skill Files Work with AI Agents](#how-skill-files-work-with-ai-agents)
- [Skill Invocation Guide](#skill-invocation-guide)
- [Testing Strategy](#testing-strategy)
- [Integration Patterns](#integration-patterns)
- [Common Mistakes & Anti-patterns](#common-mistakes--anti-patterns)
- [Project Creation Workflow](#project-creation-workflow)
- [Cross-Context Refactoring](#cross-context-refactoring)
- [When to Use Context7](#when-to-use-context7)
- [Available Skills](#available-skills)

---

## Quick Start (First 5 Minutes)

**New to CritterSupply? Start here:**

1. **Understand what you're looking at:**
   - Monorepo with 11+ bounded contexts (BCs) under `src/`
   - Event-driven architecture using Marten (event sourcing) + Wolverine (message handling)
   - Each BC has a domain project + optional `.Api` project

2. **Run the system locally:**
   ```bash
   # Start infrastructure (Postgres, RabbitMQ, Jaeger)
   docker-compose --profile infrastructure up -d

   # Run a service natively (fast, hot reload, debuggable)
   dotnet run --project "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"

   # OR run everything in containers (full stack demo)
   docker-compose --profile all up --build
   ```

3. **Key files to orient yourself:**
   - **[CONTEXTS.md](./CONTEXTS.md)** — What each BC owns and who it talks to
   - **[AGENTS.md](./AGENTS.md)** — 5-minute quick guide (exact commands, copy-paste snippets)
   - **[docs/skills/README.md](./docs/skills/README.md)** — Implementation patterns by use case

4. **Before implementing anything:**
   - Check [CONTEXTS.md](./CONTEXTS.md) for BC boundaries
   - Read the relevant skill file from [docs/skills/](./docs/skills/) (see [Skill Invocation Guide](#skill-invocation-guide))
   - Look for existing examples in the codebase (code is the source of truth)

5. **Common first tasks:**
   - Adding a command handler? → Read `docs/skills/wolverine-message-handlers.md`
   - Creating a saga? → Read `docs/skills/wolverine-sagas.md`
   - Adding a new BC? → Read `docs/skills/adding-new-bounded-context.md`
   - Writing tests? → Read `docs/skills/critterstack-testing-patterns.md`

**Still confused?** See [AGENTS.md](./AGENTS.md) for copy-paste snippets showing exact wiring patterns.

---

## CritterSupply

CritterSupply is a fictional pet supply retailer—the name a playful nod to the Critter Stack powering it, with the tagline "Stocked for every season."

E-commerce was chosen because it's a domain most developers intuitively understand. Nearly everyone has placed an order online. That familiarity lets us focus on *how* the system is built rather than explaining *what* it does.

---

## Architectural North Star

**IMPORTANT:** `CONTEXTS.md` is a **single-file, at-a-glance reference** for all bounded contexts — what each BC owns, which adjacent BCs it communicates with, and non-obvious constraints. It is not a specification.

**Code is the source of truth** for events, commands, handlers, and message contracts. `CONTEXTS.md` answers *"what does this BC own and who does it talk to?"* — nothing more.

When implementing integrations between bounded contexts:

1. **Consult CONTEXTS.md for orientation** — It shows BC ownership and communication directions
2. **Consult the codebase for contracts** — Events, commands, and message shapes live in code (especially `src/Shared/Messages.Contracts/` and BC handler files)
3. **Do not add implementation details to CONTEXTS.md** — If something requires ongoing updates to stay accurate, it does not belong there

---

## Documentation Hierarchy

**AI agents: follow this hierarchy when seeking information:**

```
Code (src/, tests/)                     ← Source of truth for all implementation details
    ↑
CONTEXTS.md                             ← BC ownership, integration directions
    ↑
CLAUDE.md (this file)                   ← Development workflows, tool configuration
    ↑
docs/skills/*.md                        ← Implementation patterns (read on demand)
    ↑
docs/decisions/*.md (ADRs)              ← Architectural decisions with rationale
    ↑
docs/planning/CURRENT-CYCLE.md          ← Active work tracking
```

**When in doubt:** Read code first, then CONTEXTS.md, then skill files. Documentation describes patterns; code shows reality.

---

## Quick Reference: Common Tasks

### Top 10 Tasks (with file locations)

1. **Add a command handler**
   - Skill: `docs/skills/wolverine-message-handlers.md`
   - Example: `src/Shopping/Shopping/Cart/AddItemToCart.cs`

2. **Create an event-sourced aggregate**
   - Skill: `docs/skills/marten-event-sourcing.md`
   - Example: `src/Orders/Orders/Order/Order.cs`

3. **Build a saga (multi-BC orchestration)**
   - Skill: `docs/skills/wolverine-sagas.md`
   - Example: `src/Orders/Orders/Order/OrderSaga.cs`

4. **Add a new bounded context**
   - Skill: `docs/skills/adding-new-bounded-context.md`
   - Checklist: Projects → Docker → Postgres → Aspire → CONTEXTS.md → Tests

5. **Write an integration test**
   - Skill: `docs/skills/critterstack-testing-patterns.md`
   - Example: `tests/Orders/Orders.IntegrationTests/OrdersTestFixture.cs`

6. **Add an HTTP endpoint**
   - Skill: `docs/skills/wolverine-message-handlers.md` (section: HTTP Endpoints)
   - Example: `src/Shopping/Shopping.Api/Queries/GetCartQuery.cs`

7. **Integrate an external service**
   - Skill: `docs/skills/external-service-integration.md`
   - Example: `src/Payments/Payments/PaymentGateway/` (Strategy pattern)

8. **Add real-time SignalR updates**
   - Skill: `docs/skills/wolverine-signalr.md`
   - Example: `src/Customer Experience/Storefront.Api/StorefrontHub.cs`

9. **Create a Marten projection**
   - Skill: `docs/skills/marten-event-sourcing.md` (section: Projections)
   - Example: `src/Orders/Orders.Api/Program.cs` (Marten projection configuration)

10. **Add a BDD feature test**
    - Skill: `docs/skills/reqnroll-bdd-testing.md`
    - Example: `docs/features/orders/order-placement.feature`

### Copy-Paste Snippets

**Handler with validation:**
```csharp
public sealed record AddItemToCart(Guid CartId, string Sku, int Quantity);

public sealed class AddItemToCartValidator : AbstractValidator<AddItemToCart>
{
    public AddItemToCartValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public static class AddItemToCartHandler
{
    public static ShoppingCartItemAdded Handle(AddItemToCart cmd, ShoppingCart cart)
    {
        // Pure function - returns event
        return new ShoppingCartItemAdded(cart.Id, cmd.Sku, cmd.Quantity);
    }
}
```

**Saga initialization:**
```csharp
public static class PlaceOrderHandler
{
    public static (Order order, OrderPlaced @event) Handle(PlaceOrder cmd)
    {
        var order = Order.Create(cmd.CustomerId, cmd.Items);
        return (order, new OrderPlaced(order.Id, cmd.CustomerId));
    }
}
```

**HTTP endpoint:**
```csharp
public static class GetCartQuery
{
    [WolverineGet("/api/cart/{cartId}")]
    public static Task<CartView> Get(Guid cartId, ICartClient client)
        => client.GetCart(cartId);
}
```

---

## Glossary

**Core Domain Concepts:**

- **Aggregate** — Boundary for atomic transactions. Event-sourced aggregates are reconstituted from their event stream. Example: `Order`, `ShoppingCart`.

- **Bounded Context (BC)** — Logical boundary within which domain terms have consistent meaning. Each BC is a separate folder under `src/`. Example: `Orders`, `Payments`, `Shopping`.

- **BFF (Backend-for-Frontend)** — API layer tailored to a specific UI. Composes data from multiple BCs. Examples: `Storefront.Api` (customer-facing), `VendorPortal.Api` (vendor-facing).

- **Choreography** — Integration pattern where BCs autonomously react to events from other BCs. No central orchestrator. Example: Inventory reacts to `OrderPlaced`.

- **Command** — Imperative request to change state. Named with verbs: `PlaceOrder`, `AddItemToCart`, `CapturePayment`.

- **Event** — Immutable fact describing something that happened. Named with past tense: `OrderPlaced`, `ItemAddedToCart`, `PaymentCaptured`.

- **Event Sourcing** — Storing events as the source of truth instead of storing current state. Aggregates are reconstituted by replaying their event stream.

- **Integration Message** — Event published across BC boundaries via RabbitMQ. Defined in `src/Shared/Messages.Contracts/`.

- **Orchestration** — Integration pattern where one BC actively coordinates others (via commands/requests). Example: `OrderSaga` orchestrates `Payments`, `Inventory`, `Fulfillment`.

- **Projection** — Denormalized read model built from events. Can be in-memory, Marten document, or relational table (EF Core). Example: `OrderSummary` projection.

- **Query** — Read-only request for data. Examples: `GetOrderDetails`, `GetCartView`.

- **Saga** — Stateful orchestrator coordinating multiple BCs over time. Stored as a Marten document with numeric revisions for optimistic concurrency. Example: `OrderSaga`, `ReturnSaga`.

- **Snapshot** — Immutable data captured at a point in time to preserve temporal consistency. Example: Address at checkout, price at add-to-cart.

- **Value Object** — Immutable object without identity, defined by its properties. Examples: `Money`, `Address`, `OrderLineItem`.

**Technology Stack:**

- **Marten** — Event sourcing + document store library for Postgres. Used for event-sourced aggregates, sagas, and document storage.

- **Wolverine** — Message handling + orchestration library. Discovers handlers via convention, manages inbox/outbox, routes messages to RabbitMQ.

- **Alba** — Integration testing library built on ASP.NET Core's `TestServer`. Used for HTTP endpoint testing.

- **Reqnroll** — BDD testing framework (successor to SpecFlow). Executes Gherkin `.feature` files.

- **TestContainers** — Library for spinning up Docker containers in tests. Used for real Postgres/RabbitMQ infrastructure.

---

## Documentation Structure

CritterSupply uses a modular documentation structure optimized for AI-assisted development and reference architecture clarity.

### Planning & Progress Documentation

**⚠️ Migration in Progress (2026-02-23):** CritterSupply is moving from markdown-based planning to **GitHub Projects + Issues**. See [GITHUB-MIGRATION-PLAN.md](./docs/planning/GITHUB-MIGRATION-PLAN.md) and [ADR 0011](./docs/decisions/0011-github-projects-issues-migration.md).

**⚡ Planning Schema Update (2026-03-15):** CritterSupply has migrated from "Cycle N Phase M" to **Milestone-Based Versioning** (M.N format). See [ADR 0032](./docs/decisions/0032-milestone-based-planning-schema.md) and [Milestone Mapping](./docs/planning/milestone-mapping.md).

**Active Milestone Tracking (GitHub-First):**
- **GitHub Issues** — One Issue per task; label with `bc:*`, `type:*`, `priority:*`, `status:*`
- **GitHub Milestones** — One Milestone per deliverable (e.g., `M30.1: Promotions Redemption Workflow`)
- **GitHub Project Board** — Kanban board view with columns: Backlog → In Progress → In Review → Done
- **[docs/planning/CURRENT-CYCLE.md](./docs/planning/CURRENT-CYCLE.md)** — Lightweight AI-readable summary (fallback when GitHub MCP not available)

**Legacy archives:** See [`docs/planning/`](./docs/planning/) for milestone retrospectives and milestone mapping. For GitHub MCP setup, see [GITHUB-ACCESS-GUIDE.md](./docs/planning/GITHUB-ACCESS-GUIDE.md).

**When to Use What:**
- Starting a new milestone: Create GitHub Milestone + Issues; write `docs/planning/milestones/mNN.M-name.md` for detailed plan
- Tracking progress: Close GitHub Issues (use `Fixes #XX` in commits); GitHub Project board auto-updates
- Checking current state: Call `list_issues(milestone="M30.1")` OR read `docs/planning/CURRENT-CYCLE.md`
- Retrospective: Create markdown doc in `milestones/`; update `CURRENT-CYCLE.md`

### Architectural Decision Records (ADRs)

**Location:** [docs/decisions/](./docs/decisions/)

**Format:** `NNNN-title.md` (e.g., `0004-sse-over-signalr.md`)

**Purpose:** Capture **why** we made key architectural choices without lengthy prose

**When to Create an ADR:**
- Technology selection decisions (SSE vs SignalR, EF Core vs Marten, etc.)
- Pattern/approach decisions (value objects vs primitives for queryable fields)
- Bounded context boundary changes (Checkout migration from Shopping to Orders)
- Integration pattern choices (orchestration vs choreography)

**ADR Template:**
```markdown
# ADR NNNN: Title

**Status:** ✅ Accepted / ⚠️ Proposed / ❌ Rejected / 🔄 Superseded

**Date:** YYYY-MM-DD

**Context:** [What problem are we solving? What constraints exist?]

**Decision:** [What did we decide?]

**Rationale:** [Why did we decide this? What are the benefits?]

**Consequences:** [What are the positive/negative outcomes? Trade-offs?]

**Alternatives Considered:** [What other options did we evaluate? Why rejected?]

**References:** [Links to cycle plans, CONTEXTS.md sections, skills docs]
```

See [`docs/decisions/`](./docs/decisions/) for the full ADR index (42+ records).

### BDD Feature Specifications (Gherkin)

**Location:** [docs/features/](./docs/features/)

**Organization:** One subdirectory per bounded context (e.g., `docs/features/shopping/`, `docs/features/customer-experience/`)

**Purpose:** Capture user-facing behavior in Given/When/Then format **before** implementation

**Benefits:**
- **Living Documentation:** Features describe system capabilities from user perspective
- **Test Generation:** Can scaffold integration tests from Gherkin scenarios
- **Clarity:** Forces thinking about user value before writing code
- **Reference Architecture Value:** Shows BDD practices for developers learning from CritterSupply

**When to Create Feature Files:**
- Before starting a milestone: Write 2-3 `.feature` files for key user stories
- During implementation: Reference scenarios as acceptance criteria for integration tests
- After completion: Feature files serve as living documentation (verified by tests)

**Gherkin Template:**
```gherkin
Feature: Feature Name
  As a [user type]
  I want to [action]
  So that [business value]

  Background:
    Given [common setup for all scenarios]

  Scenario: Happy path scenario name
    Given [precondition]
    When [action]
    Then [expected outcome]
    And [additional assertion]

  Scenario: Edge case scenario name
    Given [different precondition]
    When [action]
    Then [different expected outcome]
```

**Feature File Organization:**
```
docs/features/
├── shopping/
│   ├── cart-management.feature
│   └── checkout-wizard.feature
├── orders/
│   ├── order-placement.feature
│   └── order-saga-orchestration.feature
├── customer-experience/
│   ├── product-browsing.feature
│   ├── cart-real-time-updates.feature
│   └── checkout-flow.feature
└── [other BCs]/
```

### Workflow for New Milestones

**Before Starting a Milestone (Planning Phase):**
1. Create GitHub Milestone: `M<N>.<M>: <Short Description>` with target due date (e.g., `M30.1: Promotions Redemption Workflow`)
2. Create parent "Milestone Epic" Issue linked to Milestone
3. Create individual task Issues (linked to Milestone) using labels: `bc:*`, `type:feature`, `priority:*`
4. Write 2-3 Gherkin `.feature` files for key user stories
5. Create ADR markdown file + companion GitHub Issue for architectural decisions
6. Review `CONTEXTS.md` for BC ownership and communication directions
7. Update `docs/planning/CURRENT-CYCLE.md` with new milestone info

**During a Milestone (Implementation Phase):**
1. Implement features using `.feature` files as acceptance criteria
2. Write integration tests verifying Gherkin scenarios
3. Close Issues via `Fixes #XX` in commit messages (or PR description)
4. Comment on Issues with implementation notes / blockers
5. Create ADR markdown file when making architectural decisions

**After Completing a Milestone (Retrospective Phase):**
1. Close GitHub Milestone (records completion date)
2. Export closed Issues to markdown for fork compatibility:
   `bash scripts/github-migration/04-export-cycle.sh "M30.1: Promotions Redemption Workflow"`
   Then commit the exported file (`docs/planning/milestones/m30.1-issues-export.md`)
3. Update `CONTEXTS.md` only if BC ownership or communication directions changed (not for implementation details)
4. Create retrospective doc: `docs/planning/milestones/m30.1-retrospective.md`
5. Update `docs/planning/CURRENT-CYCLE.md` to next milestone

---

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
- **Local Orchestration**: Docker Compose (primary), .NET Aspire 13.1+ (optional)
- **Observability**: OpenTelemetry, Jaeger

### Manual Testing

For manual API testing using `.http` files in JetBrains IDEs, see **[docs/HTTP-FILES-GUIDE.md](./docs/HTTP-FILES-GUIDE.md)**.

Each bounded context API includes a comprehensive `.http` file with test scenarios, assertions, and RabbitMQ integration verification.

### Core Principles

- **Pure functions for business logic** — Handlers should be pure; side effects at the edges
- **Immutability by default** — Use records, `IReadOnlyList<T>`, `with` expressions
- **Sealed by default** — All commands, queries, events, and models are `sealed`
- **Integration tests over unit tests** — Test complete vertical slices with Alba
- **A-Frame architecture** — Infrastructure at edges, pure logic in the middle
- **Always update .sln** — When creating new projects, immediately add them to `CritterSupply.sln` using `dotnet sln add`

---

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
  Orders/Orders/, Orders.Api/
  Payments/Payments/, Payments.Api/
  Shopping/Shopping/, Shopping.Api/
  Inventory/Inventory/, Inventory.Api/
  Fulfillment/Fulfillment/, Fulfillment.Api/
  Product Catalog/ProductCatalog/, ProductCatalog.Api/
  Customer Experience/Storefront/, Storefront.Api/, Storefront.Web/
  Shared/Messages.Contracts/
```

See `docs/skills/vertical-slice-organization.md` for complete file organization patterns.

---

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

| BC                                  | Port     | Status      | Folder Name                                      |
|-------------------------------------|----------|-------------|--------------------------------------------------|
| Orders                              | 5231     | ✅ Assigned  | Orders/                                          |
| Payments                            | 5232     | ✅ Assigned  | Payments/                                        |
| Inventory                           | 5233     | ✅ Assigned  | Inventory/                                       |
| Fulfillment                         | 5234     | ✅ Assigned  | Fulfillment/                                     |
| Customer Identity                   | 5235     | ✅ Assigned  | Customer Identity/                               |
| Shopping                            | 5236     | ✅ Assigned  | Shopping/                                        |
| Product Catalog                     | 5133     | ✅ Assigned  | Product Catalog/                                 |
| **Customer Experience (BFF)**       | **5237** | ✅ Assigned  | Customer Experience/Storefront.Api/              |
| **Customer Experience (Web)**       | **5238** | ✅ Assigned  | Customer Experience/Storefront.Web/              |
| Vendor Portal                       | 5239     | ✅ Assigned  | Vendor Portal API                                |
| Vendor Identity                     | 5240     | ✅ Assigned  | Vendor Identity API                              |
| **Vendor Portal (Web/Blazor WASM)** | **5241** | ✅ Assigned  | Vendor Blazor WASM frontend (see ADR 0021)       |
| **Pricing**                         | **5242** | ✅ Assigned  | Pricing API                                      |
| **Backoffice (API)**                | **5243** | 📋 Reserved | Backoffice API (future)                          |
| **Backoffice (Web)**                | **5244** | 📋 Reserved | Backoffice frontend (future; React/Vue/Blazor)   |
| **Returns**                         | **5245** | ✅ Assigned  | Returns/                                         |
| **Listings**                        | **5246** | ✅ Assigned  | Listings/                                        |
| **Marketplaces**                    | **5247** | ✅ Assigned  | Marketplaces/                                    |
| **Correspondence**                  | **5248** | ✅ Assigned  | Correspondence/                                  |
| **Backoffice Identity**             | **5249** | ✅ Assigned  | Backoffice Identity/                             |
| **Promotions**                      | **5250** | ✅ Assigned  | Promotions/                                      |

**Why this matters:**
- Allows running multiple APIs simultaneously during development
- Prevents port conflicts when debugging in IDE
- Enables easy service discovery during local integration testing

**When creating a new BC:** Check the port allocation table above and use the next available port number.

### 3. BFF Project Structure Pattern

BFF projects follow the same **domain/API split** as all other BCs: a domain project (regular SDK) holds `Clients/`, `Composition/`, `Notifications/`, and `RealTime/`; the API project (Web SDK) holds `Queries/`, client implementations, and the SignalR hub.

**Critical:** `Program.cs` must discover handlers from both assemblies:
```csharp
opts.Discovery.IncludeAssembly(typeof(Program).Assembly);               // API (Queries)
opts.Discovery.IncludeAssembly(typeof(Storefront.RealTime.IXxx).Assembly); // Domain (Notifications)
```

See `docs/skills/bff-realtime-patterns.md` for full BFF anatomy and examples.

---

## Local Development Options

CritterSupply supports **three development workflows**: Docker Compose + dotnet run (primary), .NET Aspire (optional), and fully containerized (Docker Compose only).

### Docker Compose + dotnet run (⭐ Recommended)

**The default workflow for CritterSupply:**

```bash
# Start infrastructure only
docker-compose --profile infrastructure up -d

# Run individual services as needed
dotnet run --project "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"
```

**Why this is the default:**
- ✅ **Zero additional setup** - Just Docker Desktop + .NET 10
- ✅ **Fastest hot reload** - Native .NET process, not containerized
- ✅ **Best debugging** - F5 in Rider/Visual Studio works seamlessly
- ✅ **Lower memory** - Only infrastructure in containers (~300MB vs ~2GB)
- ✅ **Full IDE tooling** - Profilers, diagnostics, live unit testing all work

**When to use:** Daily feature development, debugging, iterative coding

---

### .NET Aspire (Optional - Enhanced Observability)

**For developers who want unified observability:**

```bash
# Step 1: Start infrastructure with fixed ports
docker-compose --profile infrastructure up -d

# Step 2: Start all 9 services via Aspire
dotnet run --project src/CritterSupply.AppHost

# Aspire Dashboard: http://localhost:15000
# Storefront Web: http://localhost:5238
```

**What Aspire adds:**
- ✅ **Unified dashboard** - Live logs, traces, metrics, health checks for all .NET services
- ✅ **One command** - Starts all 9 .NET services (vs 9 manual `dotnet run` commands)
- ✅ **Service discovery** - Automatic HTTP endpoint discovery for service-to-service calls
- ✅ **Demo-friendly** - Polished UI for showcasing system to stakeholders

**Why Aspire is optional:**
- Docker Compose provides all functionality needed for development
- Aspire adds learning curve and additional concepts
- Most developers prefer native debugging over containerized orchestration

**When to use:** Demos, exploring OpenTelemetry patterns, unified observability

**See [docs/ASPIRE-GUIDE.md](./docs/ASPIRE-GUIDE.md) for complete Aspire documentation.**

---

### Fully Containerized (Demos & Onboarding)

**Run entire stack in Docker containers:**

```bash
# Start infrastructure + all 8 APIs + Blazor web in containers
docker-compose --profile all up --build
```

**Start selective services:**
```bash
# Infrastructure + specific BCs
docker-compose --profile infrastructure --profile orders --profile shopping up

# Infrastructure + all APIs (no Blazor web)
docker-compose --profile infrastructure --profile orders --profile payments --profile inventory --profile fulfillment --profile customeridentity --profile shopping --profile catalog --profile storefront up
```

**When to use:**
- Onboarding new developers (no .NET SDK required on host)
- Demonstrating full system to stakeholders
- Cross-BC integration testing without running services natively
- Simulating production-like networking

---

### Docker Compose Profiles

| Profile            | Services Started                         | Use Case                     |
|--------------------|------------------------------------------|------------------------------|
| `infrastructure`   | Postgres + RabbitMQ                      | Native development (default) |
| `all`              | All infrastructure + 8 APIs + Blazor web | Full system demo, onboarding |
| `orders`           | Orders.Api                               | Selective service testing    |
| `payments`         | Payments.Api                             | Selective service testing    |
| `inventory`        | Inventory.Api                            | Selective service testing    |
| `fulfillment`      | Fulfillment.Api                          | Selective service testing    |
| `customeridentity` | CustomerIdentity.Api                     | Selective service testing    |
| `shopping`         | Shopping.Api                             | Selective service testing    |
| `catalog`          | ProductCatalog.Api                       | Selective service testing    |
| `storefront`       | Storefront.Api                           | Selective service testing    |
| `ci`               | Infrastructure only                      | CI/CD pipelines              |

**Combine profiles:**
```bash
docker-compose --profile infrastructure --profile orders up
```

---

### Dockerfile Maintenance

**When to rebuild container images:**
- After modifying `Directory.Packages.props` (package version changes)
- After adding/removing project references
- After modifying `Program.cs` or DI registrations
- After changing Marten/Wolverine configuration

**Force rebuild:**
```bash
docker-compose --profile all up --build
```

**Clean build (remove cached layers):**
```bash
docker-compose --profile all build --no-cache
```

---

### Connection String Differences

**IMPORTANT:** Connection strings differ between native and containerized development.

| Environment | Postgres Host | Postgres Port | RabbitMQ Host | RabbitMQ Port |
|-------------|---------------|---------------|---------------|---------------|
| **Native Development** | `localhost` | `5433` | `localhost` | `5672` |
| **Containerized** | `postgres` | `5432` | `rabbitmq` | `5672` |

**Why?**
- **Native:** APIs run on host, connect to containerized Postgres/RabbitMQ via localhost:5433
- **Containerized:** APIs run in containers, use Docker service names (`postgres`, `rabbitmq`) for inter-container communication

**Implementation:**
- `appsettings.json` uses `localhost:5433` (native default)
- `docker-compose.yml` overrides connection strings with environment variables (`postgres:5432`)

---

### Troubleshooting

**Problem:** Container builds fail with "file not found" errors
**Solution:** Ensure `.dockerignore` excludes `bin/`, `obj/`, `tests/` folders

**Problem:** Containers start but APIs return 500 errors
**Solution:** Check connection strings in `docker-compose.yml` environment variables

**Problem:** Hot reload doesn't work in containers
**Solution:** Use native development for iterative coding. Containers are for demos/integration testing.

**Problem:** Ports already in use
**Solution:** Stop other containers or change port mappings in `docker-compose.yml`

**Problem:** High memory usage with all containers
**Solution:** Use selective profiles or native development. Full stack requires ~2-3GB RAM.

---

## How Skill Files Work with AI Agents

**For AI agents reading this file:** This `CLAUDE.md` is your automatically-loaded configuration. Individual skill files (in `docs/skills/`) are **not** auto-loaded — you must proactively fetch them using your file-reading tools when the Skill Invocation Guide below says to do so.

**For humans configuring AI tools:**

| Tool | How skill files reach the AI |
|------|------------------------------|
| **Copilot Coding Agent** (GitHub Issues) | This file is auto-loaded; agent fetches skill files on demand based on guidance below |
| **Copilot Chat** (IDE) | Use `#file:docs/skills/your-skill.md` to explicitly include a skill |
| **Claude Desktop** (Project) | Upload skill `.md` files to Project Knowledge; copy this file into Project Instructions |
| **Any chat AI** (ad-hoc) | Copy-paste the relevant skill file content directly into your prompt |

See `docs/skills/README.md` → *"How AI Agents Use Skill Files"* for a full guide.

---

## Skill Invocation Guide

Skills provide detailed patterns and examples. Read the appropriate skill **before** implementing.

### When Building Wolverine Sagas

For stateful orchestration sagas that coordinate multiple bounded contexts over time:

**Read:** `docs/skills/wolverine-sagas.md`

Covers:
- When to use a saga vs. event-sourced aggregate vs. message handler
- `Saga` base class, `Guid Id` convention, message correlation, `MarkCompleted()`
- Saga initialization pattern (`PlaceOrderHandler` returning `(Order, Event)` tuple)
- Marten document configuration for sagas (`UseNumericRevisions`, optimistic concurrency)
- Decider pattern for pure-function business logic
- Multi-SKU / multi-entity race condition handling
- At-least-once idempotency guards
- Scheduling delayed messages (`outgoing.Delay()`)
- Saga lifecycle completion — all terminal paths must call `MarkCompleted()`
- Common pitfalls (orphaned sagas, dead validators, `IncludeType` vs `IncludeAssembly`)

### When Implementing Wolverine Handlers

For message handlers, HTTP endpoints, compound handlers, or aggregate workflows:

**Read:** `docs/skills/wolverine-message-handlers.md`

Covers:
- Compound handler lifecycle (`Before`, `Validate`, `Load`, `Handle`)
- Return patterns (`Events`, `OutgoingMessages`, `IStartStream`, `UpdatedAggregate<T>`)
- Aggregate loading patterns (`[ReadAggregate]`, `[WriteAggregate]`, `Load()`)
- HTTP endpoint attributes and URL conventions

### When Wiring Integration Messages Between Services

For asynchronous message-based communication between bounded contexts via RabbitMQ:

**Read:** `docs/skills/integration-messaging.md`

Covers:
- Integration messages vs domain events (foundational distinction)
- Message contracts in `src/Shared/Messages.Contracts/` (namespace conventions, payload design)
- Publishing messages (`opts.PublishMessage<T>()`, multi-destination patterns)
- Subscribing to queues (`opts.ListenToRabbitQueue()`, queue naming conventions)
- Integration message handlers (choreography vs orchestration patterns)
- End-to-end message flow walkthrough (contract → publish → subscribe → handle)
- Adding new integrations (step-by-step checklist)
- Critical warnings (silent failures, queue name mismatches, contract changes)
- Lessons learned from Cycles 18–31 (queue wiring, contract expansion, terminal state handlers, tuple return pitfalls)

### When Working with Event Sourcing

For event-sourced aggregates, domain events, or the decider pattern:

**Read:** `docs/skills/marten-event-sourcing.md`

Covers:
- Immutable aggregate design with `Create()` and `Apply()` methods
- Domain event structure
- Status enum patterns
- Marten projection configuration

### When Using Marten as Document Store

For non-event-sourced persistence (like Product Catalog):

**Read:** `docs/skills/marten-document-store.md`

Covers:
- Document model design with factory methods
- CRUD handler patterns
- Query patterns with filtering/pagination
- Soft delete configuration

### When Using Entity Framework Core (EF Core)

For relational data (like Customer Identity BC):

**Read:** `docs/skills/efcore-wolverine-integration.md`

Covers:
- Entity model design with navigation properties
- DbContext configuration and migrations
- Wolverine handler patterns with EF Core
- Testing with Alba + TestContainers

### When Projecting Events to Relational Tables

For using EF Core as a projection target for Marten event streams:

**Read:** `docs/skills/efcore-marten-projections.md`

Covers:
- `Marten.EntityFrameworkCore` package and three projection base classes
- Single-stream, multi-stream, and event projections
- DbContext configuration with Weasel schema migration
- Conjoined multi-tenancy patterns and limitations (ITenanted requirement)
- Polecat/SQL Server compatibility and collation considerations
- Testing EF Core-backed projections with TestContainers
- When to use EF Core projections vs. native Marten projections

### When Building Marten Projections

For building read models from event streams using Marten's native projection system:

**Read:** `docs/skills/event-sourcing-projections.md`

Covers:
- Three projection types: single-stream snapshots, multi-stream aggregations, live aggregation
- Three projection lifecycles: inline (zero lag), async (eventually consistent), live (session-scoped)
- `MultiStreamProjection<TDoc, TId>` for string-keyed documents from Guid streams
- `Identity<T>()` method for mapping events to document IDs
- `FetchForWriting()` API for command handler workflows
- Snapshot projections for queryable aggregates
- Real examples: `CurrentPriceView` (Pricing BC), `CouponLookupView` (Promotions BC), `Checkout` (Orders BC)
- Common anti-patterns: missing projection registration, mutable projection state, async snapshots
- Polecat (SQL Server) compatibility considerations
- Testing patterns with Alba + TestContainers

### When Integrating External Services

For payment gateways, address verification, shipping providers:

**Read:** `docs/skills/external-service-integration.md`

Covers:
- Strategy pattern with dependency injection
- Stub vs production implementations
- Graceful degradation patterns
- Configuration management

### When Building BFF Layers

For customer-facing frontends aggregating multiple BCs:

**Read:** `docs/skills/bff-realtime-patterns.md`

Covers:
- View composition from multiple BCs
- SignalR real-time updates
- Blazor + Wolverine integration
- When to use / not use BFF

### When Using Wolverine's SignalR Transport

For real-time hub communication — bidirectional WebSocket messaging, group-based routing, JWT/session auth, and the SignalR Client transport:

**Read:** `docs/skills/wolverine-signalr.md`

Covers:
- `opts.UseSignalR()` configuration and publish rules
- Marker interface pattern for message routing
- Custom hub design (`WolverineHub` for bidirectional, plain `Hub` for push-only)
- Group management (`vendor:{tenantId}`, `user:{userId}`, `customer:{id}`)
- Authentication: session cookies (Storefront) vs JWT Bearer (Vendor Portal)
- JavaScript and Blazor client integration
- SignalR Client transport for integration testing
- Marten projection side effects → Wolverine → SignalR pipeline
- Anti-patterns: hand-rolled broadcasters, query-string identity, missing `IAsyncDisposable`

### When Building Blazor WASM Frontends with JWT

For Blazor WebAssembly projects using JWT authentication (Vendor Portal pattern):

**Read:** `docs/skills/blazor-wasm-jwt.md`

Covers:
- `Microsoft.NET.Sdk.BlazorWebAssembly` SDK and `wwwroot/index.html` static entry point
- Named `HttpClient` registrations with explicit `BaseAddress` (cross-origin in WASM)
- `AddAuthorizationCore()` (not `AddAuthorization()`) for WASM
- In-memory JWT storage (`VendorAuthState`) — never localStorage (XSS risk)
- Custom `AuthenticationStateProvider` reading from in-memory state
- Background token refresh via `System.Threading.Timer` (no `IHostedService` in WASM)
- SignalR `AccessTokenProvider` delegate for reconnect-safe JWT auth
- Browser tab throttling and on-resume token expiry check
- RBAC: role-based UI with server-side enforcement
- CORS `AllowCredentials()` for cross-origin HttpOnly refresh token cookie
- Key differences from Storefront.Web (Blazor Server)

### When Adding a New Bounded Context

For creating a new BC from scratch — project scaffolding, Docker, Postgres schema, Aspire, CONTEXTS.md, tests:

**Read:** `docs/skills/adding-new-bounded-context.md`

Covers:
- Complete project creation checklist
- `.sln` and `.slnx` registration
- Docker Compose profile and service setup
- Port allocation and `launchSettings.json`
- Integration test fixture wiring

### When Organizing Code

For file structure and vertical slice organization:

**Read:** `docs/skills/vertical-slice-organization.md`

Covers:
- Feature-oriented folder structure (not technical layers)
- File naming conventions with anti-pattern warnings
- Command/Handler/Validator colocation patterns
- Event file organization
- Good vs anti-pattern examples from Shopping, Returns, and Vendor Identity BCs
- Lessons learned from Cycle 22 refactoring (ADR 0023)
- Solution structure mirroring BC boundaries

### For C# Standards

For language features, code style, immutability patterns:

**Read:** `docs/skills/modern-csharp-coding-standards.md`

Covers:
- Records and immutability
- Collection patterns (`IReadOnlyList<T>`)
- Value object patterns with JSON converters
- FluentValidation nested validators

### For Testing

For unit and integration test patterns:

**Read:** `docs/skills/critterstack-testing-patterns.md`

Covers:
- Alba integration test fixtures
- Testing pure function handlers
- Cross-context refactoring checklist

For TestContainers setup and infrastructure testing:

**Read:** `docs/skills/testcontainers-integration-tests.md`

Covers:
- Why TestContainers over mocks
- TestFixture patterns for Marten and EF Core
- Container lifecycle management
- Performance tips and best practices

For BDD testing with Gherkin and Reqnroll:

**Read:** `docs/skills/reqnroll-bdd-testing.md`

Covers:
- When to use BDD vs Alba-only tests
- Writing Gherkin feature files
- Step definition patterns
- Integration with TestFixture and Alba

For browser-level E2E testing with Playwright (full UI + SignalR):

**Read:** `docs/skills/e2e-playwright-testing.md`

Covers:
- Real Kestrel servers via WebApplicationFactory (not TestServer)
- Page Object Model with data-testid selectors
- MudBlazor MudSelect interaction patterns
- Stub coordination for deterministic IDs
- Playwright tracing for CI failure diagnosis
- SignalR antiforgery configuration
- Test lifecycle with Reqnroll hooks

For Blazor component unit testing with bUnit:

**Read:** `docs/skills/bunit-component-testing.md`

Covers:
- When to use bUnit vs Playwright (decision matrix)
- MudBlazor v9+ setup (MudPopoverProvider, IAsyncLifetime, loose JSInterop)
- BunitTestBase shared base class pattern
- Authentication state emulation with `AddAuthorization()`
- Mocking IHttpClientFactory for API-calling components
- Async data loading with `WaitForAssertion`
- CI-safe currency and locale-independent assertions

### When Running an Event Modeling Workshop

For collaborative design sessions, brain dumps, timeline construction, slice definition, scenario writing, or multi-persona facilitation:

**Read:** `docs/skills/event-modeling-workshop.md`

Covers:
- The four building blocks (Events, Commands, Views, UI) and timeline layout
- Five workshop phases with explicit input/output per phase
- Structured slice output format (table template)
- Multi-persona facilitation using CritterSupply's custom agents (Product Owner, Principal Architect, UX Engineer, QA Engineer)
- How workshop outputs connect to CritterSupply artifacts (GitHub Issues, Gherkin features, CONTEXTS.md, ADRs, Messages.Contracts)
- CritterSupply mini example (Returns BC)
- Reference: `docs/skills/references/scenarios.md` for Given/When/Then patterns

### When Writing a Copilot Session Prompt

For any request to write, plan, or generate a structured session prompt for CritterSupply's custom Copilot agent roster:

**Read:** `docs/skills/copilot-session-prompt.md`

Covers:
- Agent roster table and persona invocation (`@psa`, `@qae`, `@uxe`, etc.)
- Session types: implementation, planning, review, retrospective
- Nine-section prompt structure and required fields
- Retrospective requirements and CURRENT-CYCLE.md update protocol
- Anti-patterns and guard rails

### When Wrapping Up an Implementation Session

For any session that includes implementation work — including **implementation-only** sessions and **planning + implementation** sessions that move from design into repository changes:

**Read:** `docs/skills/final-qa-ux-review.md`

This review is **required before finalizing the session** whenever the agent has produced or modified implementation artifacts such as code, tests, configuration, workflows, or implementation-facing documentation.

**Do not skip this review just because the session also included planning.** If the session ended with implementation work or implementation-ready deliverables, the final QA/UX review still applies.

**Default review flow:**
1. Finish implementation and run the appropriate validation already required by this guide
2. Invoke both CritterSupply personas:
   - `@qa-engineer` for test coverage, failure modes, regression risk, and release readiness
   - `@ux-engineer` for workflow clarity, accessibility, consistency, cognitive load, and user-facing risk
3. Ask both reviewers to inspect the actual changed files and any relevant tests, not just the summary
4. Synthesize their feedback into one concise final review section
5. Clearly separate:
   - **Blocking / must-fix before merge**
   - **Should-fix soon / next session**
   - **Deferred polish / acceptable follow-up**
6. If either reviewer identifies a localized issue that should obviously be fixed now, fix it before finalizing
7. If findings are intentionally deferred, record them as explicit follow-up work rather than leaving them implied

**Expected final review output sections:**
- `QA Verdict`
- `UX Verdict`
- `Blocking Issues`
- `Should-Fix Soon`
- `Deferred / Polish`
- `Recommended Next Actions`
- `Release Readiness`

**Planning-only exception:** If a session is strictly exploratory or planning-only and does **not** produce implementation changes, this end-of-session QA/UX review is optional rather than required.

---

## Testing Strategy

**CritterSupply's testing pyramid:**

```
        E2E Tests (Playwright)           ← Real browser, full stack, Gherkin scenarios
       /                      \
      /                        \
    BDD Tests (Reqnroll)        ← Alba + TestContainers, Gherkin scenarios
   /                              \
  /                                \
Integration Tests (Alba)            ← HTTP endpoints, complete vertical slices
|                                    |
|                                    |
Unit Tests (xUnit)                   ← Pure function handlers, domain logic
```

### When to Use Each Test Type

**Unit Tests** — Fast, isolated, pure function testing
- **What:** Test handler logic in isolation (no DB, no HTTP, no external services)
- **When:** Handler is a pure function (input → output, no side effects)
- **Example:** `AddItemToCartHandler` logic, decider pattern functions
- **Tools:** xUnit, Shouldly
- **Speed:** < 100ms per test
- **Location:** `tests/*/UnitTests/` (optional — only if BC has significant pure logic)

**Integration Tests** — Real infrastructure, HTTP endpoints, complete workflows
- **What:** Test entire vertical slices with real Postgres, RabbitMQ, Marten, EF Core
- **When:** Testing handlers that persist data, publish messages, or call other BCs
- **Example:** `POST /api/cart/add-item` → cart persisted → event published
- **Tools:** Alba, TestContainers, xUnit, Shouldly
- **Speed:** 100-500ms per test
- **Location:** `tests/*/IntegrationTests/` (every BC has one)

**BDD Tests** — Gherkin scenarios, executable specifications
- **What:** Same as integration tests, but driven by Gherkin `.feature` files
- **When:** User-facing features with clear acceptance criteria
- **Example:** `Given a customer with items in cart, When they check out, Then order is placed`
- **Tools:** Reqnroll, Alba, TestContainers, xUnit
- **Speed:** 100-500ms per scenario
- **Location:** `tests/*/IntegrationTests/` (step definitions), `docs/features/*/` (feature files)

**E2E Tests** — Real browser, real Kestrel, full stack
- **What:** Browser automation testing the complete UI + API + SignalR flow
- **When:** Critical user journeys (checkout, order placement, vendor dashboard)
- **Example:** Open storefront → add item → checkout → verify order confirmation
- **Tools:** Playwright, Reqnroll (optional), xUnit
- **Speed:** 2-10 seconds per test
- **Location:** `tests/Storefront.E2ETests/`, `tests/VendorPortal.E2ETests/`

### Expected Test Ratios

For a mature BC like Orders:
- **~60% Integration Tests** — Most tests are Alba + TestContainers integration tests
- **~20% Unit Tests** — Pure function logic (deciders, validators, value objects)
- **~15% BDD Tests** — Key user scenarios in Gherkin format
- **~5% E2E Tests** — Critical happy paths only

---

## Integration Patterns

CritterSupply uses three integration patterns. Use the decision matrix below to choose the right one; see [CONTEXTS.md](./CONTEXTS.md) for which pattern each BC uses.

**Choreography** — BCs autonomously react to RabbitMQ events from other BCs. Use when loose coupling matters more than strict ordering and no compensation is needed. Examples: Correspondence reacts to order/payment events; Inventory reacts to `OrderPlaced`.

**Orchestration (Saga)** — One BC coordinates others via commands over time, with a stateful saga holding progress. Use when strict ordering, timeouts, or compensation logic are required. Examples: `OrderSaga` (Orders → Payments → Inventory → Fulfillment), `ReturnSaga`.

**Query-Only (BFF)** — BFF composes views by querying multiple BCs synchronously via HTTP. No state, no domain logic. Use for read-only UI composition. Examples: Storefront BFF, Vendor Portal BFF.

### Decision Matrix

| Scenario | Pattern | Rationale |
|----------|---------|-----------|
| Order placement requires payment, stock reservation, and fulfillment | **Orchestration (Saga)** | Strict ordering, compensation needed |
| Send email when order is placed | **Choreography** | Loose coupling, no compensation needed |
| Show customer their order history | **Query-Only (BFF)** | Read-only, no domain logic |
| Return approval triggers refund and replacement order | **Orchestration (Saga)** | Multi-step workflow with compensation |
| Update inventory when shipment is dispatched | **Choreography** | Autonomous reaction, eventual consistency OK |

---

## Common Mistakes & Anti-patterns

**AI agents: avoid these patterns at all costs.**

### 1. ❌ Mutable Aggregates

**Wrong:**
```csharp
public class Order
{
    public Guid Id { get; set; }  // ❌ Setter allows mutation
    public List<OrderLineItem> Items { get; set; }  // ❌ Mutable list
}
```

**Right:**
```csharp
public sealed record Order(Guid Id, IReadOnlyList<OrderLineItem> Items);
```

**Why:** Immutability prevents accidental state corruption. Event-sourced aggregates must be reconstitutable from events.

---

### 2. ❌ Handlers with Side Effects

**Wrong:**
```csharp
public static class AddItemHandler
{
    public static void Handle(AddItem cmd, ShoppingCart cart, IDocumentSession session)
    {
        cart.AddItem(cmd.Sku, cmd.Quantity);
        session.Store(cart);  // ❌ Side effect in handler
    }
}
```

**Right:**
```csharp
public static class AddItemHandler
{
    public static ItemAdded Handle(AddItem cmd, ShoppingCart cart)
    {
        // Pure function - returns event, no side effects
        return new ItemAdded(cart.Id, cmd.Sku, cmd.Quantity);
    }
}
```

**Why:** Wolverine manages persistence. Handlers should be pure functions.

---

### 3. ❌ Forgetting `MarkCompleted()` in Sagas

**Wrong:**
```csharp
public void Handle(OrderCancelled evt)
{
    Status = OrderStatus.Cancelled;
    // ❌ Saga never completes, stays in database forever
}
```

**Right:**
```csharp
public void Handle(OrderCancelled evt)
{
    Status = OrderStatus.Cancelled;
    MarkCompleted();  // ✅ Saga is removed from storage
}
```

**Why:** Orphaned sagas accumulate in the database and waste resources.

---

### 4. ❌ Forgetting to Add Projects to Solution Files

**Wrong:**
```bash
dotnet new classlib -n MyNewBC -o "src/MyNewBC"
# ❌ Forget to add to .sln and .slnx
```

**Right:**
```bash
dotnet new classlib -n MyNewBC -o "src/MyNewBC"
dotnet sln add "src/MyNewBC/MyNewBC.csproj"  # ✅ Add to .sln
# ✅ Manually add to .slnx as well (for IDE Solution Explorer)
```

**Why:** Project won't build in CI, won't appear in IDE Solution Explorer.

---

### 5. ❌ Using Mutable Collections

**Wrong:**
```csharp
public List<OrderLineItem> Items { get; init; }  // ❌ List<T> is mutable
```

**Right:**
```csharp
public IReadOnlyList<OrderLineItem> Items { get; init; }  // ✅ Immutable
```

**Why:** Caller could mutate the collection, breaking immutability guarantees.

---

### 6. ❌ Not Sealed by Default

**Wrong:**
```csharp
public record AddItemToCart(Guid CartId, string Sku, int Quantity);  // ❌ Not sealed
```

**Right:**
```csharp
public sealed record AddItemToCart(Guid CartId, string Sku, int Quantity);  // ✅ Sealed
```

**Why:** Commands, events, and models should be sealed by default. Inheritance is rarely needed.

---

### 7. ❌ Adding Implementation Details to CONTEXTS.md

**Wrong:**
```markdown
### Orders BC
Owns order lifecycle. Publishes `OrderPlaced`, `OrderConfirmed`, `OrderShipped`.
Handlers: `PlaceOrderHandler`, `ConfirmOrderHandler`.
```

**Right:**
```markdown
### Orders BC
Owns commercial commitment — checkout aggregate and order lifecycle saga that orchestrates Payments, Inventory, Fulfillment.
```

**Why:** CONTEXTS.md describes "what and who", not "how". Implementation details live in code.

---

### 8. ❌ Mixing Domain Logic in API Projects

**Wrong:**
```
src/Orders/
└── Orders.Api/                         # ❌ API project
    ├── PlaceOrderHandler.cs            # ❌ Domain logic in API project
    └── OrderSaga.cs                    # ❌ Domain logic in API project
```

**Right:**
```
src/Orders/
├── Orders/                             # ✅ Domain project
│   ├── Order/PlaceOrderHandler.cs
│   └── Order/OrderSaga.cs
└── Orders.Api/                         # ✅ API project (infrastructure only)
    ├── Program.cs
    └── Queries/GetOrderQuery.cs
```

**Why:** Separation of concerns. Domain logic should be in domain project, infrastructure in API project.

---

### 9. ❌ Not Reading Skill Files Before Implementing

**Wrong:**
```
User: "Add a saga for order placement"
Agent: [Writes saga without reading wolverine-sagas.md]
Agent: [Forgets MarkCompleted(), uses wrong Marten configuration, misses idempotency guards]
```

**Right:**
```
User: "Add a saga for order placement"
Agent: [Reads docs/skills/wolverine-sagas.md first]
Agent: [Implements saga correctly with all patterns from skill file]
```

**Why:** Skill files document 2+ years of lessons learned. Don't reinvent the wheel.

---

### 10. ❌ Using `List<T>.Add()` Instead of `with` Expressions

**Wrong:**
```csharp
cart.Items.Add(newItem);  // ❌ Mutates existing collection
```

**Right:**
```csharp
var updatedCart = cart with { Items = cart.Items.Append(newItem).ToList() };  // ✅ Immutable update
```

**Why:** Immutable updates preserve original state, enable time-travel debugging, and prevent race conditions.

---

## Project Creation Workflow

**IMPORTANT:** When creating new projects (APIs, test projects, Blazor apps, etc.), always follow this checklist:

### New Project Checklist

1. **Create the project:**
   ```bash
   dotnet new <template> -n <ProjectName> -o "<path>"
   ```

2. **Add to solution file(s):**
   ```bash
   # How to add the project to a .sln file (classic solution file - for dotnet CLI)
   dotnet sln add "<path>/<ProjectName>.csproj"

   # How to add to .slnx (modern XML solution file - for Visual Studio/Rider Solution Explorer)
   # Manually edit CritterSupply.slnx and add project to appropriate <Folder> section
   ```
   **Why:**
   - We want to make sure that projects, along with specific files like markdown documentation (.MD) are visible in the "Solution Explorer" view when it's enabled in Visual Studio or Rider.

3. **Add package references:**
   - Use `<PackageReference Include="PackageName" />` (no version - Central Package Management)
   - If package not in `Directory.Packages.props`, add it there first with version

4. **Configure launchSettings.json (if API/Web project):**
   - Check port allocation table (see API Project Configuration section above)
   - Use next available port
   - Update port allocation table in this file

5. **Build and verify:**
   ```bash
   dotnet build
   ```

6. **Update documentation:**
   - Add run instructions to README.md
   - Update bounded context status table if applicable
   - Update port allocation table in CLAUDE.md

**Common Mistakes:**
- Forgetting step 2 (adding to `.sln` and `.slnx`) - this breaks IDE tooling
- Adding to `.sln` but forgetting `.slnx` - project builds but doesn't appear in IDE Solution Explorer
- Creating duplicate entries in `.slnx` - keep projects organized in appropriate folder sections

---

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

---

## When to Use Context7

These skills document CritterSupply's established patterns. For exploring Wolverine/Marten capabilities beyond these patterns, use Context7:

- `@context7 wolverine saga` — when evaluating saga patterns
- `@context7 marten projections` — for advanced projection types
- `@context7 wolverine http` — for HTTP endpoint features

---

## Development Progress

See [docs/planning/CURRENT-CYCLE.md](./docs/planning/CURRENT-CYCLE.md) for active milestone tracking.

---

## Available Skills

See [`docs/skills/README.md`](./docs/skills/README.md) for the full skill index organized by use case, technology, and development phase.
