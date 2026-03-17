# ADR 0034: Backoffice BFF Architecture

**Status:** ✅ Accepted

**Date:** 2026-03-17

**Context:**

The Backoffice BC serves as the internal operations gateway for 7 distinct personas (CustomerService, WarehouseClerk, PricingManager, CopyWriter, OperationsManager, Executive, SystemAdmin). Unlike customer-facing (Storefront) and partner-facing (Vendor Portal) BFFs that serve 1-2 personas, the Backoffice must compose data from 10+ domain BCs (Orders, Returns, Inventory, Fulfillment, Payments, Pricing, Product Catalog, Customer Identity, Promotions, Correspondence) and expose it through role-based views.

**Key Requirements:**

1. **Multi-BC Composition:** Dashboard metrics require aggregating events from Orders, Payments, Fulfillment
2. **Role-Based Authorization:** 7 distinct personas with non-overlapping access patterns
3. **Real-Time Updates:** Operations alerts, low-stock warnings, dashboard KPIs must push to UI
4. **Internal Context Tracking:** OrderNote aggregate captures CS agent comments (not owned by Orders BC)
5. **Audit Trail:** All write operations must record `adminUserId` from JWT

**Decision Trigger:**

M32.0 (Backoffice Phase 1) implemented read-only dashboards and customer service tooling. This ADR documents the **BFF architectural pattern** validated during implementation: Backend-for-Frontend (BFF) with BFF-owned aggregates and projections.

---

## Decision

Backoffice adopts the **BFF pattern with selective BC ownership** for aggregates that represent operational metadata.

### 1. BFF Project Structure Pattern

```
src/Backoffice/
├── Backoffice/                         # Domain project (regular SDK)
│   ├── Backoffice.csproj               # References: Messages.Contracts only
│   ├── Clients/                        # HTTP client interfaces (domain)
│   │   ├── IOrdersClient.cs
│   │   ├── ICustomerIdentityClient.cs
│   │   └── IInventoryClient.cs
│   ├── Composition/                    # View models composed from multiple BCs
│   │   ├── CustomerServiceView.cs      # Orders + CustomerIdentity + Fulfillment
│   │   └── OrderDetailView.cs          # Orders + Payments + Returns
│   ├── Notifications/                  # Integration message handlers (choreography)
│   │   ├── OrderPlacedHandler.cs       # Updates AdminDailyMetrics projection
│   │   └── LowStockDetectedHandler.cs  # Creates AlertFeedView entry
│   ├── OrderNote/                      # BFF-owned aggregate (NOT owned by Orders BC)
│   │   ├── OrderNote.cs                # Event-sourced aggregate
│   │   ├── AddOrderNote.cs             # Command + handler
│   │   └── OrderNoteEvents.cs          # Domain events
│   ├── Projections/                    # BFF-owned Marten projections
│   │   ├── AdminDailyMetrics.cs        # Dashboard KPIs (from Orders, Payments events)
│   │   └── AlertFeedView.cs            # Operations alert feed (from Inventory, Fulfillment)
│   └── RealTime/                       # SignalR transport types
│       ├── IBackofficeWebSocketMessage.cs  # Marker interface for SignalR routing
│       └── BackofficeEvent.cs          # Discriminated union for real-time events
│
└── Backoffice.Api/                     # API project (Web SDK)
    ├── Backoffice.Api.csproj           # References: Backoffice, Messages.Contracts
    ├── Program.cs                      # Wolverine + Marten + SignalR + JWT Bearer auth
    ├── appsettings.json                # Connection strings, JWT config
    ├── Properties/launchSettings.json  # Port 5243
    ├── Queries/                        # HTTP endpoints (read operations)
    │   ├── GetCustomerServiceView.cs   # Fan-out to Orders, CustomerIdentity, Fulfillment
    │   └── GetDashboardMetrics.cs      # Query AdminDailyMetrics projection
    ├── Commands/                       # HTTP endpoints (write operations)
    │   ├── AddOrderNoteEndpoint.cs     # BFF-owned aggregate mutation
    │   ├── CancelOrder.cs              # Proxy to Orders BC with audit context
    │   └── ApproveReturn.cs            # Proxy to Returns BC with audit context
    ├── Clients/                        # HTTP client implementations
    │   ├── OrdersClient.cs             # IOrdersClient → http://localhost:5231
    │   └── CustomerIdentityClient.cs   # ICustomerIdentityClient → http://localhost:5235
    └── BackofficeHub.cs                # SignalR hub (server→client push only)
```

**Why This Pattern:**

- **Separation of Concerns:** Domain logic (composition, notification handling) separate from infrastructure (HTTP, DI)
- **Testability:** Test project references API project (brings in domain transitively)
- **Consistency:** BFF follows same pattern as Storefront and Vendor Portal (see ADR 0021)
- **Namespace Clarity:** `Backoffice.*` for domain, `Backoffice.Api.*` for infrastructure

### 2. BFF-Owned vs Domain BC-Owned Entities

**Pattern: BFF owns operational metadata, domain BCs own business entities.**

| Entity Type | Example | Owner | Rationale |
|-------------|---------|-------|-----------|
| **Business Entities** | Order, Return, Payment | Domain BC | Core domain logic, saga coordination, lifecycle management |
| **Operational Metadata** | OrderNote (CS internal comments) | BFF | Internal context not relevant to domain BC workflows |
| **Dashboards / Reports** | AdminDailyMetrics (KPIs) | BFF (projection) | Composition of events from multiple BCs |
| **Alert Feed** | AlertFeedView (low-stock warnings) | BFF (projection) | Operations-specific view, not domain state |

**Key Decision: OrderNote Lives in Backoffice BC (Not Orders BC)**

See ADR 0037 for full rationale. Summary:

- **Why BFF:** CS agents add internal notes that are never exposed to customers or other BCs. This is operational metadata, not order lifecycle logic.
- **Why NOT Orders BC:** Orders BC already manages order saga (Payments, Inventory, Fulfillment coordination). Adding CS-specific notes would pollute its domain model.
- **Alternative Rejected:** Separate "Annotations BC" was considered but rejected (over-engineering for a single aggregate).

### 3. Multi-BC Composition Pattern

**Pattern: Fan-out HTTP queries from BFF.**

**Example: GetCustomerServiceView (composes 4 domain BCs)**

```csharp
public static class GetCustomerServiceView
{
    [WolverineGet("/api/backoffice/customer-service")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<CustomerServiceView> Handle(
        string email,
        ICustomerIdentityClient customerIdentity,
        IOrdersClient orders,
        IFulfillmentClient fulfillment,
        IReturnsClient returns)
    {
        // Fan-out queries (parallel)
        var customerTask = customerIdentity.GetCustomerByEmail(email);
        var ordersTask = orders.ListOrdersForCustomer(email);

        await Task.WhenAll(customerTask, ordersTask);

        var customer = await customerTask;
        var orderList = await ordersTask;

        // Compose view model
        return new CustomerServiceView(
            customer.Id,
            customer.Email,
            customer.Name,
            orderList.Select(o => new OrderSummary(o.Id, o.PlacedAt, o.Status, o.TotalAmount))
        );
    }
}
```

**Why Fan-Out at BFF (Not Domain BC):**

- **Separation of Concerns:** Domain BCs remain focused on their bounded context. BFF handles cross-BC composition.
- **Latency Control:** BFF can parallelize queries (`Task.WhenAll`) or apply timeouts without affecting domain BC logic.
- **Authorization Context:** BFF extracts `adminUserId` from JWT once, passes to all downstream BC calls.

### 4. Write Operation Proxying Pattern

**Pattern: BFF extracts audit context from JWT, proxies commands to domain BCs.**

**Example: CancelOrder (proxies to Orders BC)**

```csharp
public static class CancelOrder
{
    [WolverinePost("/api/backoffice/orders/{orderId}/cancel")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<IResult> Handle(
        Guid orderId,
        string reason,
        HttpContext httpContext,
        IOrdersClient orders)
    {
        // Extract audit context from JWT
        var adminUserId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Proxy to Orders BC with audit context
        await orders.CancelOrder(orderId, reason, adminUserId);

        return Results.NoContent();
    }
}
```

**Why BFF Proxies (Not Direct Domain BC Access):**

- **Centralized Authorization:** All RBAC policies enforced at BFF layer (see ADR 0031)
- **Audit Trail Injection:** BFF extracts `adminUserId` from JWT, domain BCs trust BFF-provided values
- **Error Translation:** BFF can translate domain BC error responses into user-friendly problem details
- **Rate Limiting / Throttling:** Future phase can add BFF-level throttling without changing domain BCs

### 5. Integration Message Handling (Choreography)

**Pattern: BFF subscribes to domain BC events to update BFF-owned projections.**

**Example: OrderPlacedHandler (updates AdminDailyMetrics projection)**

```csharp
public static class OrderPlacedHandler
{
    public static async Task Handle(OrderPlaced evt, IDocumentSession session)
    {
        // Append event to Backoffice stream (will trigger AdminDailyMetricsProjection)
        session.Events.Append(Guid.NewGuid(), evt);
        await session.SaveChangesAsync();
    }
}
```

**RabbitMQ Queue Wiring (Program.cs):**

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseRabbitMq(rabbit =>
    {
        // Subscribe to Orders BC events
        rabbit.DeclareQueue("backoffice-orders-events", q => q.IsDurable = true);
        rabbit.BindQueue("backoffice-orders-events", "orders-events");

        // Subscribe to Payments BC events
        rabbit.DeclareQueue("backoffice-payments-events", q => q.IsDurable = true);
        rabbit.BindQueue("backoffice-payments-events", "payments-events");

        // ... (14+ queue subscriptions total)
    });

    opts.ListenToRabbitQueue("backoffice-orders-events");
    opts.ListenToRabbitQueue("backoffice-payments-events");
    // ...
});
```

**Why Choreography (Not Orchestration):**

- **Loose Coupling:** BFF reacts autonomously to domain BC events. Domain BCs don't know about Backoffice.
- **Scalability:** Adding new dashboard metrics doesn't require domain BC changes (just subscribe to more events).
- **Resilience:** BFF downtime doesn't block domain BC workflows.

---

## Rationale

**Why BFF Pattern Over Direct Domain BC Access:**

1. **Multi-BC Composition:** Dashboard metrics require data from Orders, Payments, Fulfillment, Inventory. Composing at BFF avoids domain BC circular dependencies.
2. **Role-Based Authorization:** Centralized RBAC at BFF layer (7 distinct personas) is simpler than replicating authorization logic across 10+ domain BCs.
3. **Audit Trail Management:** BFF extracts `adminUserId` from JWT once, passes to all domain BC calls. Domain BCs trust BFF-provided audit context.
4. **Real-Time Updates:** SignalR hub at BFF layer can push updates to UI from multiple domain BC event streams.
5. **Internal Context:** BFF-owned aggregates (OrderNote) isolate operational metadata from domain BC event streams.

**Why BFF Owns Some Aggregates (OrderNote):**

- **Domain Isolation:** CS agent comments are operational metadata, not part of Order lifecycle saga
- **Simplicity:** Single BFF-owned aggregate avoids creating a separate "Annotations BC"
- **Precedent:** Storefront BFF owns CartView (composed from Shopping + Pricing), Vendor Portal owns VendorAccount (notification preferences)

**Why BFF Owns Projections (AdminDailyMetrics):**

- **Composition from Multiple BCs:** Dashboard metrics aggregate events from Orders, Payments, Fulfillment
- **Performance:** Inline projections provide zero-lag queries for dashboard KPIs
- **Alternative Rejected:** Separate Analytics BC would require duplicating all event subscriptions (over-engineering for Phase 1)

---

## Consequences

**Positive:**

- ✅ **Consistent BFF pattern** — Backoffice follows same architecture as Storefront (ADR 0004) and Vendor Portal (ADR 0021)
- ✅ **Centralized authorization** — All RBAC policies enforced at BFF layer (7 personas, 10+ domain BCs)
- ✅ **Simplified testing** — BFF integration tests use stub HTTP clients (no real domain BC dependencies)
- ✅ **Real-time updates** — SignalR hub at BFF layer receives events from 14+ RabbitMQ queues, pushes to UI
- ✅ **BFF-owned aggregates** — OrderNote isolated from Orders BC, simplifies domain model

**Negative:**

- ⚠️ **Additional deployment unit** — Backoffice.Api adds infrastructure (port 5243, Docker service, health checks)
- ⚠️ **Fan-out latency** — Multi-BC queries (GetCustomerServiceView) have higher latency than single-BC queries
- ⚠️ **RabbitMQ queue proliferation** — BFF subscribes to 14+ domain BC queues (1 per domain BC integration)

**Mitigation:**

- **Deployment complexity:** Backoffice.Api reuses existing Aspire + Docker Compose patterns (low marginal cost)
- **Fan-out latency:** Use `Task.WhenAll` for parallel queries, HTTP client timeouts for circuit breaking
- **Queue proliferation:** RabbitMQ handles 1000s of queues; 14 queues is not a scaling concern

---

## Alternatives Considered

### Alternative A: Direct Domain BC Access (No BFF)

UI calls domain BCs directly (e.g., Blazor WASM → Orders.Api, Payments.Api).

**Rejected because:**
- Requires replicating authorization logic across 10+ domain BCs
- No centralized audit trail injection (`adminUserId` from JWT)
- Multi-BC composition becomes UI responsibility (slower, more complex)
- Domain BCs must expose admin-specific endpoints (violates BC boundaries)

---

### Alternative B: Separate Analytics BC for Dashboards

Create a dedicated Analytics BC that owns AdminDailyMetrics projection (not Backoffice BFF).

**Rejected because:**
- Over-engineering for Phase 1 (single dashboard with 5 KPIs)
- Analytics BC would need to subscribe to same 14+ domain BC queues as Backoffice
- Backoffice would still need to query Analytics BC for dashboard data (adds HTTP hop)
- Phase 3+ can introduce Analytics BC if reporting requirements grow significantly

---

### Alternative C: Domain BCs Own All Aggregates (No BFF-Owned Entities)

OrderNote aggregate lives in Orders BC (not Backoffice BFF).

**Rejected because:**
- Orders BC already manages order saga (Payments, Inventory, Fulfillment coordination)
- CS agent comments are operational metadata, not order lifecycle logic
- Adding OrderNote to Orders BC pollutes its domain model with BFF concerns
- See ADR 0037 for full rationale

---

## References

- **BFF Pattern Precedents:**
  - [ADR 0004: SSE Over SignalR (Storefront BFF)](./0004-sse-over-signalr.md)
  - [ADR 0021: Blazor WASM for Vendor Portal](./0021-blazor-wasm-vendor-portal.md)
- **RBAC Model:** [ADR 0031: Backoffice RBAC Model](./0031-admin-portal-rbac-model.md) (7 personas, policy-based authorization)
- **BFF-Owned Aggregates:** [ADR 0037: OrderNote Aggregate Ownership](./0037-ordernote-aggregate-ownership.md) (why OrderNote lives in Backoffice BC)
- **BFF-Owned Projections:** [ADR 0036: BFF-Owned Projections Strategy](./0036-bff-projections-strategy.md) (AdminDailyMetrics, AlertFeedView)
- **SignalR Hub Design:** [ADR 0035: Backoffice SignalR Hub Design](./0035-backoffice-signalr-hub-design.md) (role-based groups, server→client push only)
- **Skills:**
  - [BFF Real-Time Patterns](../skills/bff-realtime-patterns.md)
  - [Wolverine Message Handlers](../skills/wolverine-message-handlers.md)
  - [Integration Messaging](../skills/integration-messaging.md)

---

**Implementation Milestone:**

- **M32.0 (Backoffice Phase 1):** BFF architecture validated — 75 integration tests passing, 14+ RabbitMQ queue subscriptions, OrderNote aggregate, AdminDailyMetrics + AlertFeedView projections, SignalR hub

---

**Status:** ✅ **Accepted** — 2026-03-17

*This ADR documents the BFF architectural pattern validated during M32.0 implementation.*
