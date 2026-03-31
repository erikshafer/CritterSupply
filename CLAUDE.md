# Development Guidelines for CritterSupply with Claude

This repository demonstrates how to build robust, production-ready, event-driven systems using a realistic e-commerce domain — a reference architecture for the "Critter Stack" (Wolverine + Marten) in .NET.

> **Universal Applicability**: While demonstrated in C#/.NET, these patterns apply to any language. Concepts are influenced by pragmatic Domain-Driven Design (DDD) and CQRS.

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

| # | Task | Skill | Example |
|---|------|-------|---------|
| 1 | Add a command handler | `docs/skills/wolverine-message-handlers.md` | `src/Shopping/Shopping/Cart/AddItemToCart.cs` |
| 2 | Create an event-sourced aggregate | `docs/skills/marten-event-sourcing.md` | `src/Orders/Orders/Order/Order.cs` |
| 3 | Build a saga (multi-BC orchestration) | `docs/skills/wolverine-sagas.md` | `src/Orders/Orders/Order/OrderSaga.cs` |
| 4 | Add a new bounded context | `docs/skills/adding-new-bounded-context.md` | Projects → Docker → Postgres → Aspire → CONTEXTS.md → Tests |
| 5 | Write an integration test | `docs/skills/critterstack-testing-patterns.md` | `tests/Orders/Orders.IntegrationTests/OrdersTestFixture.cs` |
| 6 | Add an HTTP endpoint | `docs/skills/wolverine-message-handlers.md` | `src/Shopping/Shopping.Api/Queries/GetCartQuery.cs` |
| 7 | Integrate an external service | `docs/skills/external-service-integration.md` | `src/Payments/Payments/PaymentGateway/` |
| 8 | Add real-time SignalR updates | `docs/skills/wolverine-signalr.md` | `src/Customer Experience/Storefront.Api/StorefrontHub.cs` |
| 9 | Create a Marten projection | `docs/skills/marten-event-sourcing.md` | `src/Orders/Orders.Api/Program.cs` |
| 10 | Add a BDD feature test | `docs/skills/reqnroll-bdd-testing.md` | `docs/features/orders/order-placement.feature` |

---

## Quick References

### Preferred Tools

- **Language**: C# 14+ (.NET 10+)
- **Testing**: xUnit, Testcontainers, Alba, Shouldly
- **Validation**: FluentValidation
- **Serialization**: System.Text.Json
- **Database**: Postgres
- **Marten 8+** — Event sourcing + document store for Postgres
- **Wolverine 5+** — Message handling, HTTP endpoints, inbox/outbox orchestration
- **RabbitMQ (AMQP)** — Async messaging between BCs
- **Alba** — Integration testing via ASP.NET Core's TestServer
- **Reqnroll** — BDD testing (SpecFlow successor); runs Gherkin `.feature` files
- **TestContainers** — Spins up Docker containers (Postgres, RabbitMQ) in tests
- **Local Orchestration**: Docker Compose (primary), .NET Aspire 13.1+ (optional)
- **Observability**: OpenTelemetry, Jaeger

For manual API testing using `.http` files in JetBrains IDEs, see **[docs/HTTP-FILES-GUIDE.md](./docs/HTTP-FILES-GUIDE.md)**.

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

## Local Development

**Recommended:** Docker Compose + native `dotnet run` — fastest hot reload, best debugging, lowest memory.

```bash
# Start infrastructure (Postgres, RabbitMQ, Jaeger)
docker-compose --profile infrastructure up -d

# Run a service natively
dotnet run --project "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"

# OR: start all services via Aspire (unified dashboard — see docs/ASPIRE-GUIDE.md)
dotnet run --project src/CritterSupply.AppHost

# OR: full stack in containers (demos/onboarding)
docker-compose --profile all up --build
```

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

### Connection String Differences

**IMPORTANT:** Connection strings differ between native and containerized development.

| Environment | Postgres Host | Postgres Port | RabbitMQ Host | RabbitMQ Port |
|-------------|---------------|---------------|---------------|---------------|
| **Native Development** | `localhost` | `5433` | `localhost` | `5672` |
| **Containerized** | `postgres` | `5432` | `rabbitmq` | `5672` |

- `appsettings.json` uses `localhost:5433` (native default)
- `docker-compose.yml` overrides with environment variables (`postgres:5432`)

---

## Documentation Structure

### Planning & Progress

**Active Milestone Tracking (GitHub-First):**
- **GitHub Issues** — One Issue per task; label with `bc:*`, `type:*`, `priority:*`, `status:*`
- **GitHub Milestones** — One Milestone per deliverable (e.g., `M37.0: Feature Name`)
- **GitHub Project Board** — Kanban: Backlog → In Progress → In Review → Done
- **[docs/planning/CURRENT-CYCLE.md](./docs/planning/CURRENT-CYCLE.md)** — AI-readable fallback when GitHub MCP unavailable

**When to use what:**
- New milestone: Create GitHub Milestone + Issues; write `docs/planning/milestones/mNN.M-name.md`
- Tracking: Close Issues via `Fixes #XX`; board auto-updates
- Checking state: `list_issues(milestone="M37.0")` OR read `CURRENT-CYCLE.md`
- Retrospective: `docs/planning/milestones/mNN.M-retrospective.md`; update `CURRENT-CYCLE.md`

### Architectural Decision Records (ADRs)

**Location:** `docs/decisions/NNNN-title.md` — See the [full ADR index](./docs/decisions/) (50+ records).

**When to create:** Technology selection, pattern choices, BC boundary changes, integration approach decisions.

**Template:** See any existing ADR for the standard Status / Date / Context / Decision / Rationale / Consequences / Alternatives format.

### BDD Feature Specifications

**Location:** `docs/features/<bc-name>/*.feature` — Written before implementation as acceptance criteria.

See `docs/skills/reqnroll-bdd-testing.md` for Gherkin format and step definition patterns.

---

## How Skill Files Work

This `CLAUDE.md` is automatically loaded. Skill files in `docs/skills/` are **not** auto-loaded — fetch them on demand per the Skill Invocation Guide below.

See `docs/skills/README.md` for the full skill index.

---

## Skill Invocation Guide

Read the appropriate skill **before** implementing.

### When Building Wolverine Sagas

**Read:** `docs/skills/wolverine-sagas.md`

Covers: `Saga` base class, `Guid Id` convention, `MarkCompleted()`, Marten document config (`UseNumericRevisions`), decider pattern, idempotency guards, scheduling delayed messages, multi-SKU race conditions, common pitfalls (`IncludeType` vs `IncludeAssembly`).

### When Implementing Wolverine Handlers

**Read:** `docs/skills/wolverine-message-handlers.md`

Covers: Compound handler lifecycle (`Before`/`Validate`/`Load`/`Handle`), return patterns (`Events`, `OutgoingMessages`, `IStartStream`, `UpdatedAggregate<T>`), aggregate loading patterns, HTTP endpoint attributes.

### When Wiring Integration Messages Between Services

**Read:** `docs/skills/integration-messaging.md`

Covers: Integration vs domain events, `Messages.Contracts/` namespace conventions, `PublishMessage<T>()`, `ListenToRabbitQueue()`, queue naming, choreography vs orchestration patterns, critical warnings (silent failures, queue name mismatches).

### When Working with Event Sourcing

**Read:** `docs/skills/marten-event-sourcing.md`

Covers: Immutable aggregate design (`Create()`/`Apply()`), domain event structure, status enums, projection configuration.

### When Using Marten as Document Store

**Read:** `docs/skills/marten-document-store.md`

Covers: Document model design, CRUD handler patterns, query filtering/pagination, soft delete.

### When Using Entity Framework Core (EF Core)

**Read:** `docs/skills/efcore-wolverine-integration.md`

Covers: Entity model design, DbContext configuration and migrations, Wolverine handler patterns with EF Core, testing with Alba + TestContainers.

### When Projecting Events to Relational Tables

**Read:** `docs/skills/efcore-marten-projections.md`

Covers: Three projection base classes, DbContext with Weasel migration, multi-tenancy limitations (`ITenanted` requirement), when to use EF Core vs native Marten projections.

### When Building Marten Projections

**Read:** `docs/skills/event-sourcing-projections.md`

Covers: Single-stream / multi-stream / live aggregation, `MultiStreamProjection<TDoc, TId>`, `Identity<T>()`, `FetchForWriting()`, snapshot projections, inline vs async vs live lifecycles.

### When Integrating External Services

**Read:** `docs/skills/external-service-integration.md`

Covers: Strategy pattern with DI, stub vs production implementations, graceful degradation, configuration management.

### When Building BFF Layers

**Read:** `docs/skills/bff-realtime-patterns.md`

Covers: View composition from multiple BCs, SignalR real-time updates, Blazor + Wolverine integration, when not to use BFF.

### When Using Wolverine's SignalR Transport

**Read:** `docs/skills/wolverine-signalr.md`

Covers: `opts.UseSignalR()`, marker interface routing, group management (`vendor:{id}`, `user:{id}`), JWT vs session cookie auth, SignalR Client transport for integration testing.

### When Building Blazor WASM Frontends with JWT

**Read:** `docs/skills/blazor-wasm-jwt.md`

Covers: Named `HttpClient` with explicit `BaseAddress`, `AddAuthorizationCore()`, in-memory JWT storage, background token refresh, `AccessTokenProvider` delegate, CORS for HttpOnly refresh token cookies.

### When Adding a New Bounded Context

**Read:** `docs/skills/adding-new-bounded-context.md`

Covers: Complete project creation checklist, `.sln`/`.slnx` registration, Docker Compose profile, port allocation, integration test fixture wiring.

### When Organizing Code

**Read:** `docs/skills/vertical-slice-organization.md`

Covers: Feature-oriented folder structure, file naming conventions, command/handler/validator colocation, good vs anti-pattern examples.

### For C# Standards

**Read:** `docs/skills/modern-csharp-coding-standards.md`

Covers: Records and immutability, `IReadOnlyList<T>`, value object patterns with JSON converters, FluentValidation nested validators.

### For Testing

**Read:** `docs/skills/critterstack-testing-patterns.md` — Alba fixtures, pure function handler testing, cross-context refactoring checklist.

**Read:** `docs/skills/testcontainers-integration-tests.md` — TestFixture patterns for Marten and EF Core, container lifecycle, performance tips.

**Read:** `docs/skills/reqnroll-bdd-testing.md` — Gherkin feature files, step definitions, integration with TestFixture and Alba.

**Read:** `docs/skills/e2e-playwright-testing.md` — Real Kestrel via WebApplicationFactory, Page Object Model, MudBlazor patterns, Playwright tracing for CI.

**Read:** `docs/skills/bunit-component-testing.md` — MudBlazor v9+ setup, `BunitTestBase`, auth state emulation, `WaitForAssertion`.

### When Running an Event Modeling Workshop

**Read:** `docs/skills/event-modeling-workshop.md`

Covers: Four building blocks, five workshop phases, multi-persona facilitation, connecting outputs to GitHub Issues / Gherkin / ADRs / Messages.Contracts.

### When Writing a Copilot Session Prompt

**Read:** `docs/skills/copilot-session-prompt.md`

Covers: Agent roster (`@psa`, `@qae`, `@uxe`), nine-section prompt structure, session types, retrospective protocol, anti-patterns.

### When Wrapping Up an Implementation Session

**Read:** `docs/skills/final-qa-ux-review.md`

Required after any session producing implementation artifacts (code, tests, config, docs). Invoke `@qa-engineer` and `@ux-engineer`, synthesize into Blocking / Should-Fix / Deferred before finalizing. Skip only for strictly planning-only sessions.

---

## Testing Strategy

CritterSupply uses four test types — reach for them in this order of preference:

- **Integration** (Alba + TestContainers) — Full vertical slices with real Postgres/RabbitMQ. Every BC has an integration test project. `tests/*/IntegrationTests/`
- **Unit** (xUnit) — Pure function handlers and domain logic only; no infrastructure. `tests/*/UnitTests/` (optional)
- **BDD** (Reqnroll) — Gherkin-driven scenarios backed by the same Alba + TestContainers stack. Step defs alongside integration tests; features in `docs/features/`
- **E2E** (Playwright) — Browser tests against real Kestrel servers for critical user journeys. `tests/Storefront.E2ETests/`, `tests/VendorPortal.E2ETests/`

**Target ratios for a mature BC:** ~60% integration, ~20% unit, ~15% BDD, ~5% E2E.

---

## Integration Patterns

CritterSupply uses three integration patterns. See [CONTEXTS.md](./CONTEXTS.md) for which pattern each BC uses.

**Choreography** — BCs autonomously react to RabbitMQ events from other BCs. Use when loose coupling matters more than strict ordering and no compensation is needed. Example: Correspondence reacts to `OrderPlaced`.

**Orchestration (Saga)** — One BC coordinates others via commands over time, with a stateful saga holding progress. Use when strict ordering, timeouts, or compensation logic are required. Example: `OrderSaga` → Payments → Inventory → Fulfillment.

**Query-Only (BFF)** — BFF composes views by querying BCs synchronously via HTTP. No state, no domain logic. Use for read-only UI composition.

### Decision Matrix

| Scenario | Pattern | Rationale |
|----------|---------|-----------|
| Order placement requires payment, stock reservation, and fulfillment | **Orchestration (Saga)** | Strict ordering, compensation needed |
| Send email when order is placed | **Choreography** | Loose coupling, no compensation needed |
| Show customer their order history | **Query-Only (BFF)** | Read-only, no domain logic |
| Return approval triggers refund and replacement order | **Orchestration (Saga)** | Multi-step workflow with compensation |
| Update inventory when shipment is dispatched | **Choreography** | Autonomous reaction, eventual consistency OK |

---

## Critical Rules

Violations caught repeatedly — avoid without exception:

- `sealed record` for all commands, events, queries, and models — no exceptions
- `IReadOnlyList<T>` not `List<T>` for collections on records and aggregates
- `with` expressions for immutable updates — never `.Add()` directly on a record's list
- Handlers return events/messages — never call `session.Store()` directly (Wolverine manages persistence)
- All saga terminal paths must call `MarkCompleted()` — orphaned sagas accumulate in the database
- Domain logic in the domain project; API project is infrastructure only (`Program.cs`, `Queries/`)
- Always `dotnet sln add` AND update `.slnx` when creating a project — both are required
- Read the relevant skill file before implementing — they encode hard-won lessons from years of iteration

---

## Project Creation Workflow

When creating new projects (APIs, test projects, Blazor apps, etc.), follow this checklist:

1. **Create the project:** `dotnet new <template> -n <ProjectName> -o "<path>"`

2. **Add to solution files:**
   ```bash
   dotnet sln add "<path>/<ProjectName>.csproj"
   # Also manually edit CritterSupply.slnx — add to the appropriate <Folder> section
   ```
   Both are required. Adding to `.sln` only means the project builds but won't appear in IDE Solution Explorer.

3. **Add package references:** Use `<PackageReference Include="PackageName" />` (no version — Central Package Management). Add new packages to `Directory.Packages.props` first with a version.

4. **Configure launchSettings.json** (API/Web projects): use the next available port from the port allocation table, then update the table in this file.

5. **Build and verify:** `dotnet build`

6. **Update documentation:** README.md run instructions; port allocation table above if a port was assigned.

---

## Cross-Context Refactoring

After changes affecting multiple bounded contexts, always run:

```bash
dotnet build
dotnet test
```

**When to run:** Adding/removing project references between BCs, moving code, updating namespaces, refactoring handlers or sagas, modifying `Messages.Contracts`.

**Exit criteria:** 0 build errors · all unit tests pass · all integration tests pass.

---

## When to Use Context7

For exploring Wolverine/Marten capabilities beyond the established patterns in skill files:

- `@context7 wolverine saga` — evaluating saga patterns
- `@context7 marten projections` — advanced projection types
- `@context7 wolverine http` — HTTP endpoint features

---

## Development Progress

See [docs/planning/CURRENT-CYCLE.md](./docs/planning/CURRENT-CYCLE.md) for active milestone tracking.

---

## Available Skills

See [`docs/skills/README.md`](./docs/skills/README.md) for the full skill index organized by use case, technology, and development phase.
