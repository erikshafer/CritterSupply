# ADR 0013: Migrate from SSE to SignalR for Real-Time Communication

**Status:** ⚠️ Proposed

**Date:** 2026-03-04

**Supersedes:** [ADR 0004: Use Server-Sent Events (SSE) over SignalR](./0004-sse-over-signalr.md)

**Related Issues:** [ADR 0004 GitHub Issue #82](https://github.com/erikshafer/CritterSupply/issues/82) — SSE over SignalR discussion

---

## Context

ADR 0004 (accepted 2026-02-04, Cycle 16) established **Server-Sent Events (SSE)** as the real-time push mechanism for the Customer Experience BC. That decision was sound at the time: the only requirement was unidirectional server→client push (cart updates, order status, shipment tracking), and SSE's simplicity and native .NET 10 support made it the right fit.

Three developments now challenge that original rationale:

### 1. Growing Bidirectional Communication Requirements

The system's interactive surface has expanded beyond simple cart notifications:

- **Storefront checkout flow** — Users update quantities, apply coupons, and select shipping options while the server validates inventory, recalculates totals, and checks promotion eligibility in real time. The client must send intent; the server must confirm or reject it with live feedback.
- **Vendor Portal (planned)** — Vendors submit product change requests and expect to see live approval/rejection status, live inventory level updates, and real-time analytics as orders flow in. This is inherently bidirectional: the vendor *acts*, and the portal *reacts* with confirmations and live context.
- **Storefront AppBar** — The `InteractiveAppBar.razor` component today only subscribes to a single SSE stream but needs to reflect multi-source state changes that originate from multiple user sessions (shared carts, wish lists).

SSE was designed for server→client only. Every client→server interaction today requires a separate HTTP POST, with the client then waiting on a subsequent SSE event to confirm the result. As interaction density increases, this becomes an awkward choreography of fire-and-forget POSTs paired with "matching" incoming SSE events.

### 2. Wolverine's Mature SignalR Transport (as of Wolverine 5.x)

Jeremy Miller published a detailed walkthrough of Wolverine's SignalR integration on 2026-03-01:  
https://jeremydmiller.com/2026/03/01/signalr-the-critter-stack/

The Wolverine SignalR transport, built specifically for CritterWatch (JasperFx's own management console), offers:

```csharp
// Program.cs — Single call to enable the entire transport
opts.UseSignalR();

// Publish any message implementing a marker interface to all connected clients
opts.Publish(x =>
{
    x.MessagesImplementing<IStorefrontWebSocketMessage>();
    x.ToSignalR();
});
```

This means the `IEventBroadcaster` abstraction we wrote by hand — the `Channel<T>` map, cleanup logic, thread-safe `ConcurrentDictionary`, etc. — can be replaced almost entirely by Wolverine's routing engine. Our notification handlers would simply return messages or publish through Wolverine, and the framework handles delivery to the correct hub.

Additionally, Marten 8 projection **side effects** can push directly into this pipeline:

```csharp
// Inside a SingleStreamProjection
public override ValueTask RaiseSideEffects(IDocumentOperations ops, IEventSlice<CartSummary> slice)
{
    slice.PublishMessage(new CartUpdated(slice.Snapshot));
    return ValueTask.CompletedTask;
}
```

Wolverine sees `CartUpdated` implements `IStorefrontWebSocketMessage`, routes it to SignalR, and pushes it to the correct hub group — zero manual plumbing.

### 3. CritterStack Alignment and Reference Architecture Value

CritterSupply is a reference architecture for idiomatic Critter Stack usage. Using Wolverine's official SignalR transport instead of a hand-rolled SSE broadcaster better demonstrates the framework's capabilities, and keeps CritterSupply aligned with JasperFx's own production usage (CritterWatch).

---

## Decision

**Proposed: Migrate the Customer Experience BC (and future Vendor Portal BC) from SSE to SignalR, leveraging Wolverine's native SignalR transport.**

This document serves as the structured analysis to support that decision. Implementation would follow in a dedicated cycle.

---

## Detailed Analysis

### Strengths of Migrating to SignalR

#### 1. Native Bidirectional Communication (WebSockets)

SignalR's WebSocket transport provides full-duplex communication over a single persistent connection. The client can both receive server-initiated messages *and* send messages to the server without additional HTTP round trips.

For the checkout flow, this enables patterns like:
- Client sends `ApplyCoupon { Code: "SAVE10" }` over WebSocket
- Server validates, applies, and sends back `CouponApplied { Discount: 10% }` or `CouponRejected { Reason: "Expired" }` — all in the same persistent connection
- Zero extra HTTP POST + SSE event matching ceremony

#### 2. Wolverine Publisher/Subscriber Integration

With `opts.UseSignalR()`, Wolverine treats the SignalR hub as just another messaging endpoint. All existing Wolverine patterns apply:

- **Routing rules** via `opts.Publish(...)` with marker interfaces
- **Message partitioning** (`opts.MessagePartitioning.UseInferredMessageGrouping()`) for ordered delivery per customer/tenant
- **Inbox/Outbox** semantics if needed
- **Handlers** receive messages from clients the same way they receive RabbitMQ messages

This means notification handlers can simply return or publish typed messages; Wolverine handles routing to SignalR. The `IEventBroadcaster` custom abstraction and all its `Channel<T>` bookkeeping disappears.

#### 3. Marten Projection Side Effects Pipeline

Marten 8's `RaiseSideEffects()` hook in projections can publish messages that Wolverine routes to SignalR. This is particularly powerful for:

- **Cart projections** updating the live cart view in response to domain events
- **Order saga state** being projected and pushed to the UI as the saga advances
- **Vendor analytics projections** delivering live updates as orders arrive

The result is a clean, end-to-end reactive pipeline:
```
Domain Event → Marten projection → side effect message → Wolverine → SignalR hub → Client
```

#### 4. Built-in Group and Connection Management

SignalR manages connection IDs, group membership (`Groups.AddToGroupAsync`), and presence. Today's hand-rolled `ConcurrentDictionary<Guid, List<Channel<StorefrontEvent>>>` in `EventBroadcaster.cs` is a re-implementation of what SignalR provides natively, including:

- Multi-tab support (one customer → many connections, all receive events)
- Automatic cleanup on disconnect
- Group-based targeting (`Clients.Group("customer:guid")`)

#### 5. Type-Safe CloudEvents Message Contract

Wolverine's SignalR transport wraps messages in a [CloudEvents](https://cloudevents.io/) JSON envelope:
```json
{
  "specversion": "1.0",
  "type": "CritterSupply.CartUpdated",
  "source": "storefront-api",
  "id": "uuid-here",
  "time": "2026-03-04T06:10:14Z",
  "datacontenttype": "application/json",
  "data": { "cartId": "...", "totalAmount": 42.99 }
}
```

This brings message typing and versioning discipline to the real-time layer, and enables TypeScript type generation from .NET types (as Jeremy Miller demonstrates with NJsonSchema in CritterWatch).

#### 6. Protocol Fallback Resilience

SignalR negotiates the best transport:
1. WebSockets (preferred)
2. Server-Sent Events (fallback)
3. Long Polling (last resort)

SSE has no fallback. In environments where WebSocket upgrades are blocked (some corporate proxies, load balancers with aggressive timeout rules), the current SSE implementation degrades silently. SignalR's negotiation provides resilience.

#### 7. Blazor's Existing SignalR Dependency

Blazor Server *already* uses SignalR internally for its UI diff transport. Adding `@microsoft/signalr` (or using Blazor's built-in hub connection builder) is not adding a new dependency category — it's leveraging infrastructure that's already present.

---

### Weaknesses and Risks of Migrating to SignalR

#### 1. Migration Cost and Scope

The current SSE implementation is woven through multiple layers:

| File | Change Required |
|------|----------------|
| `Storefront.Api/StorefrontHub.cs` | Replace SSE endpoint with `Hub<T>` class |
| `Storefront.Api/Program.cs` | Add `opts.UseSignalR()`, `MapHub<T>()`, remove `IEventBroadcaster` singleton |
| `Storefront/Notifications/IEventBroadcaster.cs` | Delete (replaced by Wolverine routing) |
| `Storefront/Notifications/EventBroadcaster.cs` | Delete (replaced by Wolverine routing) |
| `Storefront/Notifications/*.cs` (8 handler files) | Update to publish typed messages via Wolverine instead of calling `broadcaster.BroadcastAsync` |
| `Storefront.Web/wwwroot/js/sse-client.js` | Replace with SignalR client (`HubConnectionBuilder`) |
| `Storefront.Web/Components/Pages/Cart.razor` | Replace SSE subscription with SignalR hub connection |
| `Storefront.Web/Components/Pages/OrderConfirmation.razor` | Replace SSE subscription with SignalR hub connection |
| `Storefront.Web/Components/Layout/InteractiveAppBar.razor` | Replace SSE subscription with SignalR hub connection |
| `Storefront.csproj` | No change (domain project, Wolverine routing handles delivery) |
| `Storefront.Api.csproj` | Add `WolverineFx.Http.SignalR` or equivalent package |
| Integration tests | Update to test SignalR delivery instead of SSE stream |

This is a focused but non-trivial refactor — roughly a full cycle of work if done correctly with tests.

#### 2. Connection Lifecycle Complexity

SSE connections are stateless HTTP GET requests. SignalR WebSocket connections are stateful and long-lived:

- **Heartbeats** — SignalR has a configurable ping/pong mechanism; misconfigured timeouts can cause unexpected disconnects
- **Sticky sessions** — In a multi-instance deployment (horizontally scaled), all messages for a SignalR group must reach the instance holding that group's connections, or a backplane (Redis, Azure SignalR Service) is required
- **Reconnection logic** — The SignalR client SDK handles this, but component lifecycle management in Blazor requires careful `DisposeAsync` implementation to avoid connection leaks

The current SSE implementation avoids all of this because HTTP connections are stateless and Kestrel manages them transparently.

#### 3. Backplane Requirement for Horizontal Scaling

The current `EventBroadcaster` is an in-memory singleton — it works only when all Storefront.Api instances share memory (i.e., single instance). SignalR has the same problem: without a backplane, only the instance holding a client's connection can push to it.

For SignalR, the backplane options are:
- **Redis** (via `AddStackExchangeRedisBackplane`) — simple, already planned for CritterSupply cache needs
- **Azure SignalR Service** — fully managed, scales to millions of connections; more cost at scale

> **Note:** The current SSE implementation *also* has this scaling problem — it's just less visible because it's in-memory. Migrating to SignalR forces this problem to be properly addressed.

#### 4. Debugging Becomes Harder

One legitimate advantage of SSE over SignalR is debuggability:
- SSE events appear as plain text `data: {...}` in the browser Network tab
- WebSocket frames are binary, harder to inspect (though browser DevTools now shows WebSocket message logs)

This is a developer experience cost, not a production concern. The CloudEvents wrapper does provide a compensating benefit: structured typing makes messages self-describing.

#### 5. Wolverine SignalR Package Maturity

Wolverine's SignalR transport is described by Jeremy Miller as still in its early stages — CritterWatch is its primary test bed. Adopting a transport that is actively being built out means:

- API surface may change between Wolverine minor versions
- Some edge cases (e.g., message partitioning with multi-instance hubs) may not yet be fully documented
- Community support is newer than the battle-tested RabbitMQ transport

This is a manageable risk given the Critter Stack's track record, but it warrants starting with a targeted pilot rather than a big-bang migration.

---

### Cost Estimate

| Category | Estimate | Notes |
|----------|----------|-------|
| **Implementation** | 1 full cycle (~2 weeks) | Replace SSE with SignalR hub, update all client components |
| **Package additions** | Minimal | `WolverineFx.Http.SignalR` (or equivalent); Blazor already has SignalR client |
| **Infrastructure** | Redis backplane or Azure SignalR Service | Redis already planned; ~$20–50/month additional if using managed service |
| **Testing** | Included in cycle | Integration tests for SignalR delivery, Blazor component tests |
| **Learning curve** | Low-Medium | Team familiar with Wolverine; SignalR hub pattern is well-documented |
| **Risk** | Low-Medium | Wolverine SignalR transport is newer; pilot on Customer Experience BC first |

---

## Alternatives Considered

### Keep SSE for Storefront, Add SignalR for Vendor Portal Only

**Rationale:** Vendor Portal's bidirectional needs are clear; Storefront's are emerging.

**Pros:**
- Smaller immediate scope
- Storefront SSE keeps working during transition

**Cons:**
- Maintains two different real-time technologies in the same system
- Storefront checkout will need SignalR soon anyway (quantity updates, coupon validation, shipping option real-time pricing)
- Two different mental models for developers

**Verdict:** ⚠️ Deferred — viable short-term, but incurs tech debt. Storefront migration should follow closely.

---

### Hybrid: SSE for read, WebSocket channel for write

**Rationale:** Keep SSE for server→client notifications, add a separate WebSocket endpoint for client→server commands.

**Pros:**
- Minimal change to existing SSE infrastructure
- Explicit separation of concerns

**Cons:**
- Two persistent connections per client (SSE + WebSocket)
- Duplicates infrastructure; no Wolverine integration for the WebSocket side
- Reinvents exactly what SignalR + Wolverine already provides

**Verdict:** ❌ Rejected — unnecessary complexity.

---

### Stay with SSE (Keep ADR 0004)

**Pros:**
- Zero migration cost
- Simpler debugging
- Current implementation is working

**Cons:**
- Cannot support Vendor Portal's bidirectional requirements without awkward workarounds
- Checkout real-time validation requires client→server push
- Misses the Wolverine SignalR transport capabilities now available at the framework level
- Diverges from CritterStack's own production direction (CritterWatch uses SignalR)

**Verdict:** ❌ Not recommended long-term — acceptable short-term while migration is planned.

---

## Proposed Migration Path

This is not a single-step refactor. The recommended sequence:

### Phase 1: Pilot — Customer Experience BC (1 Cycle)
1. Add `WolverineFx.Http.SignalR` (or equivalent) package to `Storefront.Api`
2. Create `StorefrontSignalRHub : Hub` replacing the SSE endpoint
3. Configure `opts.UseSignalR()` and publish rules in `Program.cs`
4. Define `IStorefrontWebSocketMessage` marker interface in `Storefront` domain project
5. Remove `IEventBroadcaster` / `EventBroadcaster` — update all notification handlers to publish typed messages via Wolverine
6. Replace `sse-client.js` with SignalR `HubConnectionBuilder` in Blazor components
7. Implement customer group management (`Groups.AddToGroupAsync("customer:{id}")`)
8. Add Redis backplane configuration (even for single-instance, validates the pattern)
9. Update integration tests to verify hub message delivery

### Phase 2: Vendor Portal BC (future cycle, when Portal is built)
1. Create `VendorPortalHub : Hub` with tenant-scoped groups
2. Implement bidirectional: vendor submits change request over WebSocket → Wolverine handler → confirmation pushed back via hub
3. Marten analytics projection side effects → Wolverine → SignalR → live dashboard updates

### Phase 3: Update CONTEXTS.md and Skills Documentation
1. Update `CONTEXTS.md` to replace SSE references with SignalR
2. Update `docs/skills/bff-realtime-patterns.md` with SignalR hub patterns
3. Archive SSE patterns in skills doc as historical reference

---

## Consequences

### Positive

✅ **Enables bidirectional communication** for Storefront checkout and Vendor Portal  
✅ **Eliminates hand-rolled `EventBroadcaster`** — replaced by Wolverine's routing engine  
✅ **Marten → Wolverine → SignalR pipeline** for reactive projection-driven UI updates  
✅ **Aligns with CritterStack direction** (CritterWatch production usage)  
✅ **Protocol resilience** — SignalR falls back to SSE or Long Polling if WebSockets unavailable  
✅ **Type-safe CloudEvents contracts** — messages are self-describing and version-trackable  
✅ **Reference architecture value** — demonstrates Wolverine's SignalR transport to learners  

### Negative

⚠️ **Migration cost** — ~1 full cycle of work to replace all SSE components  
⚠️ **Backplane required** for horizontal scaling (Redis or Azure SignalR Service)  
⚠️ **Wolverine SignalR transport is newer** — less battle-tested than RabbitMQ transport  
⚠️ **WebSocket debugging harder** than SSE's plain-text stream  

---

## Recommendation

**Proceed with migration.** The original ADR 0004 decision was correct at the time: SSE was the right tool for the unidirectional use case present in Cycle 16. That use case has grown.

The Vendor Portal's bidirectional requirements are not a future concern — they are a core part of the planned Vendor Portal BC definition in CONTEXTS.md. The Storefront checkout experience will increasingly need real-time server confirmations as inventory validation, coupon checks, and dynamic shipping pricing are added.

Most importantly: Wolverine's native SignalR transport makes the migration *cheaper* than building the equivalent SSE infrastructure. `EventBroadcaster.cs` — 70 lines of carefully managed channel bookkeeping — is replaced by two lines of Wolverine configuration and a publish rule. This aligns with the project's core principle: *let the Critter Stack handle the plumbing.*

ADR 0004 should be marked as superseded once Phase 1 implementation is complete and validated.

---

## References

- [Wolverine SignalR Transport Documentation](https://wolverinefx.io/guide/messaging/transports/signalr.html)  
- [Jeremy Miller – "SignalR & The Critter Stack" (2026-03-01)](https://jeremydmiller.com/2026/03/01/signalr-the-critter-stack/)  
- [ADR 0004: SSE over SignalR (Superseded)](./0004-sse-over-signalr.md)  
- [Marten Projection Side Effects](https://martendb.io/events/projections/side-effects.html)  
- [CloudEvents Specification](https://cloudevents.io/)  
- [CONTEXTS.md – Customer Experience BC](../../CONTEXTS.md#customer-experience)  
- [CONTEXTS.md – Vendor Portal BC](../../CONTEXTS.md#vendor-portal)  
- [docs/skills/bff-realtime-patterns.md](../skills/bff-realtime-patterns.md)  

---

**Decision Made By:** Erik Shafer / GitHub Copilot (Principal Architect Review)  
**Approved By:** [To be updated after team review]
