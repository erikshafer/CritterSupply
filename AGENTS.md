# AGENTS — CritterSupply (quick guide)

## 1) Big-picture architecture
- Monorepo of bounded contexts under `src/` (one domain project + optional `.Api` per BC). See `src/Customer Experience/`, `src/Orders/`, `src/Shopping/`, `src/Product Catalog/`, etc.
- BFF pattern: domain project + `*.Api` project. Example: `src/Customer Experience/Storefront/` (domain) and `src/Customer Experience/Storefront.Api/` (BFF). See `Storefront.Api/Program.cs` for the canonical wiring.
- Event-driven core: event-sourced BCs (Orders, Inventory, Payments, Fulfillment, Returns, Promotions, Pricing) use Marten + Wolverine sagas/handlers. Examples: `src/Orders/Orders.Api/Program.cs`, `src/Returns/Returns.Api/Program.cs`, `src/Promotions/Promotions.Api/Program.cs`, `src/Pricing/Pricing.Api/Program.cs`.

## 2) Critical developer workflows (exact commands)
- Build solution: `dotnet build` (run at repo root containing `CritterSupply.slnx`).
- Run tests: `dotnet test` (root). Use target project paths for focused runs.
- Start infrastructure only (recommended):

```bash
docker-compose --profile infrastructure up -d
```

- Quick start (infrastructure + Storefront API in one command):

```bash
./scripts/dev-start.sh quick-start
```

- Run a single service natively (example — Orders):

```bash
dotnet run --project "src/Orders/Orders.Api/Orders.Api.csproj"
```

- Start full stack in containers:

```bash
docker-compose --profile all up --build
```

- Stop/cleanup containers:

```bash
docker-compose --profile all down
```

- Optional: start all .NET services via Aspire (optional workflow):
See `CLAUDE.md` for more information.

- Convenience script: run the repository helper script to start the full stack via Docker Compose:

```bash
./scripts/start-all.sh
```

- Aspire AppHost (optional): the project `src/CritterSupply.AppHost/` can start all .NET services with the Aspire dashboard. Run:

```bash
dotnet run --project "src/CritterSupply.AppHost"
# Dashboard URL is defined by the active launch profile; check `src/CritterSupply.AppHost/Properties/launchSettings.json`
```

## 3) Project-specific conventions & patterns (where to look + examples)
- Immutability & types: domain types prefer `record`, `IReadOnlyList<T>`, and `sealed` for commands/events. See `docs/skills/modern-csharp-coding-standards.md` referenced from `CLAUDE.md`.
- Handler discovery: API `Program.cs` includes API and domain assemblies in Wolverine discovery. Example: `opts.Discovery.IncludeAssembly(typeof(Program).Assembly);` and `opts.Discovery.IncludeAssembly(typeof(Storefront.RealTime.IStorefrontWebSocketMessage).Assembly);` in `src/Customer Experience/Storefront.Api/Program.cs`.
- Wolverine policies: transactional inbox/outbox & durable queues are used. See `src/Orders/Orders.Api/Program.cs` for `opts.Policies.AutoApplyTransactions();`, `opts.Policies.UseDurableLocalQueues();`, and `opts.Policies.UseDurableOutboxOnAllSendingEndpoints();`.
- Marten patterns: event-sourced aggregates, snapshots, numeric revisions for saga documents. See `src/Orders/Orders.Api/Program.cs` where projections and numeric revisions are configured.
- BFF composition: HTTP queries live under `*.Api/Queries/`, client implementations under `*.Api/Clients/`, domain composition under `*/Composition/` (example: `src/Customer Experience/Storefront/Composition`).

## 4) Integration points & cross-component communication
- Message broker: RabbitMQ (AMQP). Compose sets service name `rabbitmq`; native development uses `localhost:5672`. Check `docker-compose.yml` for env mappings. Program.cs files read configuration for RabbitMQ host/credentials.
- Persistence: Marten + PostgreSQL. Compose maps host port `5433` → container `5432`. Look at `docker-compose.yml` and `CLAUDE.md` "Connection String Differences". appsettings.json keys used: `marten` or `postgres` depending on project.
- Local vs container connection strings:
  - Native development: Postgres host `localhost` port `5433`, RabbitMQ `localhost:5672`.
  - Containerized: Postgres host `postgres:5432`, RabbitMQ host `rabbitmq:5672`.
- Tracing/OTLP: Jaeger runs in `infrastructure` profile; compose sets `OTEL_EXPORTER_OTLP_ENDPOINT` (e.g., `http://jaeger:4317`).
- Real-time: Storefront uses SignalR + Wolverine SignalR transport. Look for `app.MapHub<Storefront.Api.StorefrontHub>("/hub/storefront").DisableAntiforgery();` in `src/Customer Experience/Storefront.Api/Program.cs` and marker interface `src/Customer Experience/Storefront/RealTime/IStorefrontWebSocketMessage.cs`.

## Quick developer facts (from CLAUDE.md)
- Connection strings (native vs container):
  - Native development: Postgres host `localhost` port `5433`, RabbitMQ `localhost:5672`. Appsettings typically use the `postgres` connection key (some older BCs use `marten`).
  - Containerized: Postgres host `postgres` port `5432`, RabbitMQ host `rabbitmq` port `5672`. Docker Compose overrides these via environment variables in `docker-compose.yml`.

- Docker Compose profiles (common):
  - `infrastructure` — Postgres + RabbitMQ + Jaeger (recommended when running services natively).
  - `all` — all infrastructure + APIs + web frontend (use for full-stack demos / CI).
  - Per-BC profiles (e.g., `orders`, `storefront`, `payments`, `shopping`) — start only the BCs you need.

- Port allocation excerpt (see `CLAUDE.md` for the full table):
  - Orders API: `5231`
  - Storefront API (Customer Experience): `5237`
  - Storefront Web: `5238`
  - Note: these are example values — the authoritative, up-to-date port allocation table lives in `CLAUDE.md` and may change. Check `CLAUDE.md` before hard-coding ports.


## 5) Examples (copy-paste snippets)
These snippets are intentionally small extracts from canonical `Program.cs` wiring found in the repo. Use them as copy/paste starting points.

- Handler discovery (BFF + domain assembly):

```csharp
builder.Host.UseWolverine(opts =>
{
    // API assembly (Queries)
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);

    // Domain assembly (Integration message handlers)
    opts.Discovery.IncludeAssembly(typeof(Storefront.RealTime.IStorefrontWebSocketMessage).Assembly);
});
```

- Marten + projections + numeric revisions (Orders.Api example):

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("postgres"));
    opts.Projections.Snapshot<Checkout>(SnapshotLifecycle.Inline);
    opts.Schema.For<Order>().UseNumericRevisions(true);
});
```

- Wolverine policies (transactional inbox/outbox):

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
});
```

- SignalR registration (Storefront.Api example):

```csharp
builder.Services.AddSignalR();
app.MapHub<Storefront.Api.StorefrontHub>("/hub/storefront")
    .DisableAntiforgery();
```

- Connection string detection (native vs container):

```csharp
var conn = builder.Configuration.GetConnectionString("postgres")
    ?? builder.Configuration.GetConnectionString("marten");
```

## 6) Key files to read first for orientation (exact paths)
1. `CONTEXTS.md` — architectural source of truth for bounded contexts and message contracts.
2. `CLAUDE.md` — development guidelines, port allocation table, BFF pattern, and workflows.
3. `docker-compose.yml` — infrastructure & per-service env examples (Postgres, RabbitMQ, Jaeger, port mappings).
4. `CritterSupply.slnx` — solution layout and project mapping.
5. `Directory.Packages.props` — centralized package versions (Marten, Wolverine, OpenTelemetry, test libs).
6. Representative Program.cs files:
   - `src/Orders/Orders.Api/Program.cs`
   - `src/Customer Experience/Storefront.Api/Program.cs`
7. `docs/skills/` — start with `wolverine-message-handlers.md`, `marten-event-sourcing.md`, `bff-realtime-patterns.md`, `vertical-slice-organization.md`.

## Quick navigation examples (paths)
- Port allocations & policies: `CLAUDE.md`.
- Wolverine + RabbitMQ examples: `src/Orders/Orders.Api/Program.cs`, `src/Customer Experience/Storefront.Api/Program.cs`.
- Marten config & projections: `src/Orders/Orders.Api/Program.cs`.
- BFF composition + SignalR hub: `src/Customer Experience/Storefront.Api/` and `src/Customer Experience/Storefront/Notifications/` (handlers), `src/Customer Experience/Storefront/RealTime/` (SignalR transport types).

---
Agents should use these exact files and commands as the source of truth; prefer repository examples over generic patterns.

