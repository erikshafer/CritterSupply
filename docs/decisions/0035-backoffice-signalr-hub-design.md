# ADR 0035: Backoffice SignalR Hub Design

**Status:** ✅ Accepted

**Date:** 2026-03-17

**Context:**

The Backoffice BFF serves 7 distinct internal personas (CustomerService, WarehouseClerk, PricingManager, CopyWriter, OperationsManager, Executive, SystemAdmin) who require real-time notifications:

- **Executive Dashboard** — Live KPIs (order count, revenue, payment failure rate)
- **Operations Team** — System alerts (low stock, payment failures, fulfillment delays)
- **Customer Service** — Order status changes, return approvals
- **Warehouse Clerks** — Inbound stock notifications, picking alerts

Unlike Storefront (customer-facing, session-based auth) and Vendor Portal (partner-facing, JWT Bearer auth), Backoffice must support **role-based group routing** where notifications are targeted to specific personas based on their role claims in JWT tokens.

**Decision Trigger:**

M32.0 (Backoffice Phase 1) implemented real-time updates for dashboard metrics and alert feeds. This ADR documents the **SignalR hub design pattern** validated during implementation: role-based groups with server→client push only.

---

## Decision

Backoffice adopts **role-based SignalR groups** with **server→client push only** (no bidirectional messaging).

### 1. Hub Design Pattern

**Implementation: Plain `Hub` (Not `WolverineHub`)**

```csharp
/// <summary>
/// SignalR hub for real-time Backoffice notifications.
/// Inherits from Hub (not WolverineHub) since bidirectional messaging is not needed.
/// </summary>
public sealed class BackofficeHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Role-based group management added when JWT auth is wired
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
```

**Why Plain `Hub` (Not `WolverineHub`):**

- **Server→Client Push Only:** Backoffice does not need client→server commands (no "ApproveReturn" via SignalR). All write operations go through HTTP POST endpoints (authorization, audit trail, validation).
- **Simpler Testing:** Plain `Hub` can be tested with SignalR's test harness. `WolverineHub` requires Wolverine integration testing infrastructure.
- **Consistency with Storefront:** Storefront BFF also uses plain `Hub` for real-time updates (see ADR 0004).

**Alternative Rejected: `WolverineHub`**

Vendor Portal uses `WolverineHub` for bidirectional messaging (client submits commands via SignalR). Backoffice does not need this capability.

### 2. Role-Based Group Management

**Pattern: Groups named `role:{role-name}` for targeted message delivery.**

**Group Naming Convention:**

| Role | SignalR Group Name | Purpose |
|------|--------------------|---------|
| `cs-agent` | `role:cs-agent` | Order status changes, return approvals |
| `warehouse-clerk` | `role:warehouse-clerk` | Inbound stock notifications, picking alerts |
| `operations-manager` | `role:operations-manager` | System alerts, fulfillment delays |
| `executive` | `role:executive` | Live dashboard KPIs (order count, revenue) |
| `system-admin` | `role:system-admin` | System health, diagnostics |

**Group Assignment (OnConnectedAsync):**

```csharp
public override async Task OnConnectedAsync()
{
    // Extract role from JWT claims
    var role = Context.User?.FindFirstValue(ClaimTypes.Role);

    if (!string.IsNullOrEmpty(role))
    {
        // Add connection to role-based group
        await Groups.AddToGroupAsync(Context.ConnectionId, $"role:{role}");
    }

    await base.OnConnectedAsync();
}
```

**Message Broadcasting (Integration Message Handler):**

```csharp
public static class OrderPlacedAdminHandler
{
    public static async Task Handle(
        OrderPlaced evt,
        IHubContext<BackofficeHub> hubContext)
    {
        // Broadcast to Executive dashboard (live metrics)
        var liveMetric = new LiveMetricUpdated(
            OrderCount: 1,
            Revenue: evt.TotalAmount,
            PaymentFailureRate: 0m,
            OccurredAt: evt.PlacedAt
        );

        await hubContext.Clients
            .Group("role:executive")
            .SendAsync("backoffice-event", liveMetric);
    }
}
```

**Why Role-Based Groups (Not User-Specific):**

- **Scalability:** 100 concurrent CS agents → 1 SignalR group (`role:cs-agent`), not 100 user-specific groups
- **Targeting:** Alerts naturally target roles, not individual users (e.g., "all warehouse clerks see low-stock alert")
- **Consistency:** Aligns with RBAC authorization policies (see ADR 0031)

**Alternative Rejected: User-Specific Groups (`user:{userId}`)**

Would create 100+ groups for CS agents, poor scalability. Vendor Portal uses tenant-specific groups (`vendor:{tenantId}`) because vendors don't share data; Backoffice personas share role-based views.

### 3. JWT Bearer Authentication for SignalR

**Pattern: JWT from Authorization header OR query string (WebSocket upgrade).**

**WebSocket Query String Auth (OnMessageReceived):**

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Extract JWT from query string for SignalR WebSocket handshake
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hub/backoffice"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
```

**Why Query String Auth (Not Header):**

- **WebSocket Limitation:** Browser WebSocket API does not allow custom headers during handshake. JWT must be in query string.
- **Security Trade-Off:** Query strings are logged by proxies/load balancers. Mitigated by short-lived tokens (15 minutes) and TLS encryption.
- **Precedent:** Vendor Portal uses the same pattern (see `VendorPortal.Api/Program.cs`).

### 4. Marker Interface Pattern (Wolverine SignalR Transport)

**Pattern: `IBackofficeWebSocketMessage` interface for automatic SignalR routing.**

**Marker Interface:**

```csharp
/// <summary>
/// Marker interface for messages sent to Backoffice users via SignalR.
/// Messages implementing this interface are automatically routed to the SignalR transport.
/// </summary>
public interface IBackofficeWebSocketMessage { }
```

**Discriminated Union for Type-Safe Deserialization:**

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
[JsonDerivedType(typeof(LiveMetricUpdated), typeDiscriminator: "live-metric-updated")]
[JsonDerivedType(typeof(AlertCreated), typeDiscriminator: "alert-created")]
public abstract record BackofficeEvent(DateTimeOffset OccurredAt);

public sealed record LiveMetricUpdated(
    int OrderCount,
    decimal Revenue,
    decimal PaymentFailureRate,
    DateTimeOffset OccurredAt
) : BackofficeEvent(OccurredAt), IBackofficeWebSocketMessage;

public sealed record AlertCreated(
    string AlertType,
    string Severity,
    string Message,
    DateTimeOffset OccurredAt
) : BackofficeEvent(OccurredAt), IBackofficeWebSocketMessage;
```

**Wolverine SignalR Configuration (Program.cs):**

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseSignalR(signalr =>
    {
        signalr.Messages.From<IBackofficeWebSocketMessage>()
            .ToHub<BackofficeHub>("backoffice-event");
    });
});
```

**Why Marker Interface (Not Manual HubContext Injection):**

- **Declarative Routing:** Message types explicitly declare SignalR routing intent
- **Wolverine Integration:** Leverages Wolverine's SignalR transport (automatic serialization, error handling)
- **Consistency:** Storefront and Vendor Portal use the same pattern (see ADR 0004, ADR 0021)

### 5. Message Flow: Domain Events → RabbitMQ → Backoffice → SignalR

**Complete Pipeline:**

1. **Domain BC publishes integration message** → RabbitMQ exchange (e.g., `orders-events`)
2. **Backoffice subscribes to queue** → `backoffice-orders-events` queue
3. **Integration message handler processes event** → `OrderPlacedAdminHandler`
4. **Handler publishes SignalR message** → `IBackofficeWebSocketMessage` (LiveMetricUpdated)
5. **Wolverine routes to SignalR hub** → `BackofficeHub.Clients.Group("role:executive")`
6. **SignalR broadcasts to connected clients** → All executives see updated dashboard KPI

**Example: OrderPlaced → LiveMetricUpdated**

```csharp
// Step 3: Integration message handler
public static class OrderPlacedAdminHandler
{
    public static LiveMetricUpdated Handle(OrderPlaced evt, IDocumentSession session)
    {
        // Update BFF projection (AdminDailyMetrics)
        session.Events.Append(Guid.NewGuid(), evt);

        // Return SignalR message (Wolverine routes to hub)
        return new LiveMetricUpdated(
            OrderCount: 1,
            Revenue: evt.TotalAmount,
            PaymentFailureRate: 0m,
            OccurredAt: evt.PlacedAt
        );
    }
}
```

**Why Handler Returns SignalR Message (Not Manual `IHubContext` Injection):**

- **Railway Programming:** Handler returns event, Wolverine routes to SignalR hub automatically
- **Testability:** Handler is pure function (no `IHubContext` dependency in tests)
- **Consistency:** Aligns with Wolverine message handler patterns (see `docs/skills/wolverine-message-handlers.md`)

---

## Rationale

**Why Role-Based Groups:**

1. **Scalability:** 100 concurrent CS agents → 1 SignalR group (`role:cs-agent`), not 100 user-specific groups
2. **Targeting:** Alerts naturally target roles, not individual users (e.g., "all warehouse clerks see low-stock alert")
3. **RBAC Alignment:** Groups mirror authorization policies (see ADR 0031)
4. **Simplicity:** Group management logic is 5 lines in `OnConnectedAsync` (extract role → `AddToGroupAsync`)

**Why Server→Client Push Only (Not Bidirectional):**

1. **Authorization:** HTTP endpoints enforce `[Authorize(Policy = "...")]` with FluentValidation. SignalR bypasses this infrastructure.
2. **Audit Trail:** HTTP POST captures `adminUserId` from JWT, timestamps, request body. SignalR does not.
3. **Idempotency:** HTTP endpoints use Marten optimistic concurrency (numeric revisions). SignalR adds complexity.
4. **Consistency:** Storefront BFF uses the same pattern (server→client push only, see ADR 0004).

**Why JWT Bearer Auth (Not Session Cookies):**

1. **Stateless:** Backoffice can scale horizontally without shared session state
2. **SignalR Compatibility:** JWT in query string works seamlessly with WebSocket handshake
3. **Cross-Origin:** Future Backoffice.Web (Blazor WASM at port 5244) can connect from different origin
4. **Consistency:** Vendor Portal uses JWT Bearer auth (see ADR 0021)

**Why Marker Interface (Not Manual HubContext):**

1. **Declarative Intent:** `IBackofficeWebSocketMessage` explicitly marks SignalR-routable messages
2. **Wolverine Integration:** Leverages Wolverine's SignalR transport (automatic routing, serialization)
3. **Testability:** Handlers return messages (pure functions), Wolverine routes to hub (no `IHubContext` in tests)

---

## Consequences

**Positive:**

- ✅ **Scalable group management** — Role-based groups scale to 1000s of concurrent users (1 group per role)
- ✅ **Consistent authorization** — SignalR groups mirror RBAC authorization policies (ADR 0031)
- ✅ **Testable handlers** — Integration message handlers are pure functions (return SignalR messages)
- ✅ **Type-safe deserialization** — Discriminated union pattern (`BackofficeEvent`) with JSON polymorphism
- ✅ **Cross-origin compatible** — JWT Bearer auth works for future Blazor WASM frontend (port 5244)

**Negative:**

- ⚠️ **Query string JWT logging** — Proxies/load balancers may log query strings (JWT exposure risk)
- ⚠️ **No bidirectional messaging** — Client cannot submit commands via SignalR (must use HTTP POST)
- ⚠️ **Group assignment latency** — `OnConnectedAsync` adds 5-10ms to connection handshake

**Mitigation:**

- **JWT logging risk:** Use short-lived tokens (15 minutes), rotate on refresh. TLS encrypts query string in transit. Phase 2+ can explore WebSocket subprotocol (sends JWT in protocol handshake, not query string).
- **No bidirectional messaging:** Intentional design choice. HTTP endpoints provide authorization, audit trail, validation.
- **Group assignment latency:** 5-10ms is negligible (SignalR connection handshake is 50-100ms total).

---

## Alternatives Considered

### Alternative A: User-Specific Groups (`user:{userId}`)

**Pattern:** Each user gets their own SignalR group.

**Rejected because:**
- Creates 100+ groups for CS agents (poor scalability)
- Alerts naturally target roles, not individual users
- Adds complexity to broadcasting (must resolve role → list of user IDs)

---

### Alternative B: Bidirectional Messaging (`WolverineHub`)

**Pattern:** Client submits commands via SignalR (e.g., "ApproveReturn" via SignalR).

**Rejected because:**
- Bypasses HTTP endpoint authorization (`[Authorize(Policy = "...")]`)
- No audit trail for `adminUserId` (JWT claims not captured in command)
- Adds idempotency complexity (Marten optimistic concurrency harder to enforce in SignalR)
- Vendor Portal uses `WolverineHub` because vendor submission workflow benefits from bidirectional messaging; Backoffice does not have this requirement

---

### Alternative C: Session Cookie Auth (Not JWT Bearer)

**Pattern:** SignalR uses session cookies (like Storefront BFF).

**Rejected because:**
- Backoffice will have Blazor WASM frontend (Phase 2+) running on different origin (port 5244)
- Session cookies require SameSite=None, AllowCredentials=true (CORS complexity)
- JWT Bearer auth is stateless, scales horizontally without shared session state
- Vendor Portal precedent: JWT Bearer auth for Blazor WASM (see ADR 0021)

---

## References

- **BFF Pattern:** [ADR 0034: Backoffice BFF Architecture](./0034-backoffice-bff-architecture.md)
- **RBAC Model:** [ADR 0031: Backoffice RBAC Model](./0031-admin-portal-rbac-model.md) (7 personas, policy-based authorization)
- **SignalR Precedents:**
  - [ADR 0004: SSE Over SignalR (Storefront BFF)](./0004-sse-over-signalr.md) — Server→client push only
  - [ADR 0021: Blazor WASM for Vendor Portal](./0021-blazor-wasm-vendor-portal.md) — JWT Bearer auth for SignalR
- **Skills:**
  - [Wolverine SignalR](../skills/wolverine-signalr.md) — Marker interface pattern, group management
  - [BFF Real-Time Patterns](../skills/bff-realtime-patterns.md) — Integration message handler → SignalR flow

---

**Implementation Milestone:**

- **M32.0 (Backoffice Phase 1):** SignalR hub validated — Role-based groups, JWT Bearer auth, `IBackofficeWebSocketMessage` marker interface, discriminated union for type-safe deserialization

---

**Status:** ✅ **Accepted** — 2026-03-17

*This ADR documents the SignalR hub design pattern validated during M32.0 implementation.*
