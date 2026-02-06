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

## Documentation Structure

CritterSupply uses a modular documentation structure optimized for AI-assisted development and reference architecture clarity.

### Planning & Progress Documentation

**Active Cycles & Roadmap:**
- **[docs/planning/CYCLES.md](./docs/planning/CYCLES.md)** — Active development cycle, recent completions, upcoming work (replaces old DEVPROGRESS.md wall of text)
- **[docs/planning/cycles/](./docs/planning/cycles/)** — Detailed per-cycle plans with objectives, deliverables, completion criteria
- **[docs/planning/BACKLOG.md](./docs/planning/BACKLOG.md)** — Future work not yet scheduled

**When to Use:**
- Starting a new cycle: Create `docs/planning/cycles/cycle-NN-name.md` with detailed plan
- Tracking progress: Update `CYCLES.md` (move from "Upcoming" to "Current" to "Recently Completed")
- Retrospective: Add "Implementation Notes" section to cycle plan after completion

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

**Existing ADRs:**
- [ADR 0001: Checkout Migration to Orders](./docs/decisions/0001-checkout-migration-to-orders.md) (Cycle 8)
- [ADR 0002: EF Core for Customer Identity](./docs/decisions/0002-ef-core-for-customer-identity.md) (Cycle 13)
- [ADR 0003: Value Objects vs Primitives for Queryable Fields](./docs/decisions/0003-value-objects-vs-primitives-queryable-fields.md) (Cycle 14)
- [ADR 0004: SSE over SignalR](./docs/decisions/0004-sse-over-signalr.md) (Cycle 16)

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
- Before starting a cycle: Write 2-3 `.feature` files for key user stories
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

### Workflow for New Cycles

**Before Starting a Cycle (Planning Phase):**
1. Create cycle plan: `docs/planning/cycles/cycle-NN-name.md`
2. Write 2-3 Gherkin `.feature` files for key user stories
3. Create ADRs for any architectural decisions made during planning
4. Review CONTEXTS.md for integration requirements
5. Update `docs/planning/CYCLES.md` (move cycle from "Upcoming" to "Current")

**During a Cycle (Implementation Phase):**
1. Implement features using `.feature` files as acceptance criteria
2. Write integration tests verifying Gherkin scenarios
3. Update cycle plan with "Implementation Notes" section (learnings, gotchas)
4. Create ADRs when making architectural decisions during implementation

**After Completing a Cycle (Retrospective Phase):**
1. Mark cycle complete in `CYCLES.md` (add completion date + summary)
2. Update CONTEXTS.md with new integration flows
3. Archive detailed notes in cycle-specific doc (`cycles/cycle-NN-name.md`)
4. Plan next cycle based on backlog

### Legacy Documentation

**DEVPROGRESS.md** — Deprecated as of 2026-02-04. See deprecation notice at top of file. Kept for historical reference only.

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
- **Always update .sln** — When creating new projects, immediately add them to `CritterSupply.sln` using `dotnet sln add`

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
| **Customer Experience (BFF)** | **5237** | ✅ Assigned  |
| **Customer Experience (Web)** | **5238** | ✅ Assigned  |
| Vendor Portal                 | 5239     | 📋 Reserved |
| Vendor Identity               | 5240     | 📋 Reserved |

**Why this matters:**
- Allows running multiple APIs simultaneously during development
- Prevents port conflicts when debugging in IDE
- Enables easy service discovery during local integration testing

**When creating a new BC:** Check the port allocation table above and use the next available port number.

### 3. BFF Project Structure Pattern

**IMPORTANT:** Backend-for-Frontend (BFF) projects follow the **same domain/API split pattern** as all other bounded contexts.

**BFF Anatomy:**

```
src/<BC Name>/
├── <ProjectName>/                      # Domain project (regular SDK)
│   ├── <ProjectName>.csproj            # References: Messages.Contracts only
│   ├── Clients/                        # HTTP client interfaces (domain)
│   │   └── I*Client.cs
│   ├── Composition/                    # View models for UI
│   │   └── *View.cs
│   └── Notifications/                  # Integration message handlers
│       ├── IEventBroadcaster.cs        # SSE pub/sub interface
│       ├── EventBroadcaster.cs         # Channel-based implementation
│       ├── *Event.cs                   # Discriminated union for SSE
│       └── *Handler.cs                 # Integration message handlers
│
└── <ProjectName>.Api/                  # API project (Web SDK)
    ├── <ProjectName>.Api.csproj        # References: <ProjectName>, Messages.Contracts
    ├── Program.cs                      # Wolverine + Marten + DI setup
    ├── appsettings.json                # Connection strings
    ├── Properties/launchSettings.json  # Port allocation
    ├── Queries/                        # HTTP endpoints (composition)
    │   └── Get*View.cs                 # namespace: <ProjectName>.Api.Queries
    ├── Clients/                        # HTTP client implementations
    │   └── *Client.cs                  # namespace: <ProjectName>.Api.Clients
    └── *Hub.cs                         # SSE endpoint (namespace: <ProjectName>.Api)
```

**Example: Customer Experience BFF (Storefront)**

```
src/Customer Experience/
├── Storefront/                         # Domain project
│   ├── Clients/                        # Interfaces for Shopping, Orders, Catalog, etc.
│   ├── Composition/                    # CartView, CheckoutView, ProductListingView
│   └── Notifications/                  # ItemAddedHandler, OrderPlacedHandler, EventBroadcaster
│
└── Storefront.Api/                     # API project
    ├── Program.cs                      # Wolverine handler discovery for both assemblies
    ├── Queries/                        # GetCartView, GetCheckoutView, GetProductListing
    ├── Clients/                        # ShoppingClient, OrdersClient, CatalogClient
    └── StorefrontHub.cs                # SSE endpoint at /sse/storefront
```

**Key Configuration (Program.cs):**

```csharp
// Discover handlers in both API and Domain assemblies
builder.Host.UseWolverine(opts =>
{
    // API assembly (Queries)
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

    // Domain assembly (Integration message handlers)
    opts.Discovery.IncludeAssembly(typeof(Storefront.Notifications.IEventBroadcaster).Assembly);
});
```

**Why This Pattern:**
- **Separation of Concerns:** Domain logic (composition, notification handling) separate from infrastructure (HTTP, DI)
- **Testability:** Test project references API project (brings in domain transitively)
- **Consistency:** BFF follows same pattern as Orders, Shopping, Payments, etc.
- **Namespace Clarity:** `<ProjectName>.*` for domain, `<ProjectName>.Api.*` for infrastructure

**Common Mistakes to Avoid:**
- ❌ Single Web SDK project combining domain + infrastructure
- ❌ Domain project referencing Wolverine packages (not needed - handlers discovered via API assembly reference)
- ❌ Forgetting to include domain assembly in `opts.Discovery.IncludeAssembly()`

---

## Skill Invocation Guide

Skills provide detailed patterns and examples. Read the appropriate skill **before** implementing.

### When Implementing Wolverine Handlers

For message handlers, HTTP endpoints, compound handlers, or aggregate workflows:

**Read:** `skills/wolverine-message-handlers.md`

Covers:
- Compound handler lifecycle (`Before`, `Validate`, `Load`, `Handle`)
- Return patterns (`Events`, `OutgoingMessages`, `IStartStream`, `UpdatedAggregate<T>`)
- Aggregate loading patterns (`[ReadAggregate]`, `[WriteAggregate]`, `Load()`)
- HTTP endpoint attributes and URL conventions

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
- Cross-context refactoring checklist

For TestContainers setup and infrastructure testing:

**Read:** `skills/testcontainers-integration-tests.md`

Covers:
- Why TestContainers over mocks
- TestFixture patterns for Marten and EF Core
- Container lifecycle management
- Performance tips and best practices

For BDD testing with Gherkin and Reqnroll:

**Read:** `skills/reqnroll-bdd-testing.md`

Covers:
- When to use BDD vs Alba-only tests
- Writing Gherkin feature files
- Step definition patterns
- Integration with TestFixture and Alba

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
| `testcontainers-integration-tests.md` | TestContainers setup, patterns for Marten and EF Core |
| `reqnroll-bdd-testing.md` | BDD testing with Gherkin and Reqnroll |
