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

**⚠️ Migration in Progress (2026-02-23):** CritterSupply is moving from markdown-based planning to **GitHub Projects + Issues**. See [GITHUB-MIGRATION-PLAN.md](./docs/planning/GITHUB-MIGRATION-PLAN.md) and [ADR 0011](./docs/decisions/0011-github-projects-issues-migration.md).

**Active Cycle Tracking (GitHub-First):**
- **GitHub Issues** — One Issue per task; label with `bc:*`, `type:*`, `priority:*`, `status:*`
- **GitHub Milestones** — One Milestone per cycle (e.g., `Cycle 19: Authentication & Authorization`)
- **GitHub Project Board** — Kanban board view with columns: Backlog → In Progress → In Review → Done
- **[docs/planning/CURRENT-CYCLE.md](./docs/planning/CURRENT-CYCLE.md)** — Lightweight AI-readable summary (fallback when GitHub MCP not available)

**Why GitHub-first works across any machine (MacBook, Windows, Linux):**
Project state lives in GitHub's cloud, not in local files. Any machine with the GitHub MCP server configured and GitHub auth completed gets the same authoritative view of open issues, active milestones, and backlog — no stale markdown, no sync needed.

**Prerequisites per machine:**
- ✅ **GitHub MCP server** — configured in your AI tool's MCP settings (VS Code, Cursor, Claude Desktop, etc.)
- ✅ **GitHub auth** — personal access token with `repo` + `project` scopes
- See [GITHUB-ACCESS-GUIDE.md](./docs/planning/GITHUB-ACCESS-GUIDE.md) for complete setup instructions (PAT creation, MCP config JSON, domain allowlist, verification checklist)

**Legacy Markdown (Read-Only Archives):**
- **[docs/planning/CYCLES.md](./docs/planning/CYCLES.md)** — Historical cycle records (Cycles 1–18); **deprecated for new cycles**
- **[docs/planning/BACKLOG.md](./docs/planning/BACKLOG.md)** — Historical backlog; **deprecated** (items migrated to GitHub Issues)
- **[docs/planning/cycles/](./docs/planning/cycles/)** — Per-cycle retrospective docs; still created as markdown after each cycle

**When to Use What:**
- Starting a new cycle: Create GitHub Milestone + Issues; write `docs/planning/cycles/cycle-NN-name.md` for detailed plan
- Tracking progress: Close GitHub Issues (use `Fixes #XX` in commits); GitHub Project board auto-updates
- Checking current state: Call `list_issues(milestone="Cycle NN")` OR read `docs/planning/CURRENT-CYCLE.md`
- Retrospective: Create markdown doc in `cycles/`; update `CURRENT-CYCLE.md`

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
1. Create GitHub Milestone: `Cycle NN: <Name>` with target due date
2. Create parent "Cycle Epic" Issue linked to Milestone
3. Create individual task Issues (linked to Milestone) using labels: `bc:*`, `type:feature`, `priority:*`
4. Write 2-3 Gherkin `.feature` files for key user stories
5. Create ADR markdown file + companion GitHub Issue for architectural decisions
6. Review `CONTEXTS.md` for integration requirements
7. Update `docs/planning/CURRENT-CYCLE.md` with new cycle info

**During a Cycle (Implementation Phase):**
1. Implement features using `.feature` files as acceptance criteria
2. Write integration tests verifying Gherkin scenarios
3. Close Issues via `Fixes #XX` in commit messages (or PR description)
4. Comment on Issues with implementation notes / blockers
5. Create ADR markdown file when making architectural decisions

**After Completing a Cycle (Retrospective Phase):**
1. Close GitHub Milestone (records completion date)
2. Export closed Issues to markdown for fork compatibility:
   `bash scripts/github-migration/04-export-cycle.sh "Cycle NN: <Name>"`
   Then commit the exported file (`docs/planning/cycles/cycle-NN-issues-export.md`)
3. Update `CONTEXTS.md` with new integration flows
4. Create retrospective doc: `docs/planning/cycles/cycle-NN-retrospective.md`
5. Update `docs/planning/CURRENT-CYCLE.md` to next cycle

### Legacy Documentation

**DEVPROGRESS.md** — Deprecated as of 2026-02-04. Kept for historical reference only.

**docs/planning/CYCLES.md** — Deprecated for new cycles as of 2026-02-23 (migration to GitHub Issues). Kept as historical archive (Cycles 1–18).

**docs/planning/BACKLOG.md** — Deprecated as of 2026-02-23 (migrated to GitHub Issues). Kept as historical reference.

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

| BC                            | Port     | Status      | Folder Name                    |
|-------------------------------|----------|-------------|--------------------------------|
| Orders                        | 5231     | ✅ Assigned  | Orders/                        |
| Payments                      | 5232     | ✅ Assigned  | Payments/                      |
| Inventory                     | 5233     | ✅ Assigned  | Inventory/                     |
| Fulfillment                   | 5234     | ✅ Assigned  | Fulfillment/                   |
| Customer Identity             | 5235     | ✅ Assigned  | Customer Identity/             |
| Shopping                      | 5236     | ✅ Assigned  | Shopping/                      |
| Product Catalog               | 5133     | ✅ Assigned  | Product Catalog/               |
| **Customer Experience (BFF)** | **5237** | ✅ Assigned  | Customer Experience/Storefront.Api/ |
| **Customer Experience (Web)** | **5238** | ✅ Assigned  | Customer Experience/Storefront.Web/ |
| Vendor Portal                 | 5239     | 📋 Reserved | (future)                       |
| Vendor Identity               | 5240     | 📋 Reserved | (future)                       |

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

| Profile | Services Started | Use Case |
|---------|------------------|----------|
| `infrastructure` | Postgres + RabbitMQ | Native development (default) |
| `all` | All infrastructure + 8 APIs + Blazor web | Full system demo, onboarding |
| `orders` | Orders.Api | Selective service testing |
| `payments` | Payments.Api | Selective service testing |
| `inventory` | Inventory.Api | Selective service testing |
| `fulfillment` | Fulfillment.Api | Selective service testing |
| `customeridentity` | CustomerIdentity.Api | Selective service testing |
| `shopping` | Shopping.Api | Selective service testing |
| `catalog` | ProductCatalog.Api | Selective service testing |
| `storefront` | Storefront.Api | Selective service testing |
| `ci` | Infrastructure only | CI/CD pipelines |

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

## Skill Invocation Guide

Skills provide detailed patterns and examples. Read the appropriate skill **before** implementing.

### When Implementing Wolverine Handlers

For message handlers, HTTP endpoints, compound handlers, or aggregate workflows:

**Read:** `docs/skills/wolverine-message-handlers.md`

Covers:
- Compound handler lifecycle (`Before`, `Validate`, `Load`, `Handle`)
- Return patterns (`Events`, `OutgoingMessages`, `IStartStream`, `UpdatedAggregate<T>`)
- Aggregate loading patterns (`[ReadAggregate]`, `[WriteAggregate]`, `Load()`)
- HTTP endpoint attributes and URL conventions

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

### When Organizing Code

For file structure and vertical slice organization:

**Read:** `docs/skills/vertical-slice-organization.md`

Covers:
- Command/Handler/Validator colocation
- File and folder naming conventions
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
| `bff-realtime-patterns.md` | Backend-for-Frontend, real-time updates (SSE + SignalR) |
| `vertical-slice-organization.md` | File structure, colocation patterns |
| `modern-csharp-coding-standards.md` | C# language features, immutability |
| `critterstack-testing-patterns.md` | Unit and integration testing |
| `testcontainers-integration-tests.md` | TestContainers setup, patterns for Marten and EF Core |
| `reqnroll-bdd-testing.md` | BDD testing with Gherkin and Reqnroll |
