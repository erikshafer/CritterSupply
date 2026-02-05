# ADR 0004: Use Server-Sent Events (SSE) over SignalR for Real-Time Updates

**Status:** ✅ Accepted

**Date:** 2026-02-04

**Context:**

Customer Experience BC (Cycle 16) requires real-time push notifications to the Blazor frontend:
- Cart updates when items added/removed (Shopping BC events)
- Order status changes (Orders BC saga state transitions)
- Shipment tracking updates (Fulfillment BC events)

Two primary options for real-time server→client push in .NET:
1. **SignalR** - ASP.NET Core library for real-time web functionality (WebSockets, Server-Sent Events, Long Polling fallback)
2. **Server-Sent Events (SSE)** - HTTP-based protocol for one-way server→client push (native support in .NET 10)

---

## Decision

**Use Server-Sent Events (SSE)** for real-time notifications in Customer Experience BC.

Implement using .NET 10's native `IAsyncEnumerable<T>` pattern for SSE endpoints.

---

## Rationale

### Why SSE?

**1. Protocol Simplicity**
- SSE is one-way server→client push (matches our use case exactly)
- We don't need client→server push beyond standard HTTP POST commands
- Simpler mental model: "HTTP endpoint that streams events"

**2. Native .NET 10 Support**
```csharp
// SSE endpoint using IAsyncEnumerable<T> (built-in)
[WolverineGet("/sse/storefront")]
public static async IAsyncEnumerable<CartUpdate> SubscribeToCartUpdates(
    Guid cartId,
    [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var update in _cartUpdateStream.Where(u => u.CartId == cartId))
    {
        yield return update;
        if (ct.IsCancellationRequested) yield break;
    }
}
```

**3. HTTP/2 Efficiency**
- SSE works over HTTP/2 with multiplexing
- Single TCP connection can handle multiple SSE streams
- Standard HTTP semantics (headers, status codes, caching)

**4. Browser DevTools Support**
- SSE events visible in Network tab as `text/event-stream`
- Easier debugging vs. WebSocket binary frames
- Standard `EventSource` API in JavaScript (no library needed)

**5. Reference Architecture Value**
- Shows modern .NET 10 capabilities (native SSE support is new)
- Demonstrates when to choose SSE over SignalR (pattern recognition)
- Simpler example for developers learning from CritterSupply

---

## Why Not SignalR?

SignalR is excellent but **overkill for this use case**:

| Feature | SSE | SignalR | Our Need |
|---------|-----|---------|----------|
| Server→Client Push | ✅ Built-in | ✅ Built-in | ✅ Required |
| Client→Server Push | ❌ Not supported | ✅ Built-in | ❌ Not needed (use HTTP POST) |
| Automatic Reconnect | ✅ Browser handles | ✅ Library handles | ✅ Either works |
| Protocol Negotiation | ❌ SSE only | ✅ WebSocket, SSE, Long Polling | ❌ Not needed |
| Client Library | ❌ Not needed (`EventSource`) | ✅ Required (`@microsoft/signalr`) | ➖ Prefer simpler |

**SignalR Adds Complexity:**
- Requires client library (`@microsoft/signalr` in Blazor)
- Hub classes with method routing (`Clients.Group("cart:123").SendAsync("CartUpdated")`)
- Connection lifetime management (heartbeats, timeouts, reconnection logic)
- Protocol negotiation (WebSocket preferred, fallback to Long Polling)

**SSE is Sufficient:**
- Standard browser `EventSource` API (no library needed)
- HTTP GET request with `Accept: text/event-stream`
- Events arrive as `data: {...}` lines
- Browser handles reconnection automatically

---

## Consequences

### Positive

✅ **Simpler Implementation**
- No SignalR hub classes, connection IDs, or group management
- Standard HTTP endpoint returning `IAsyncEnumerable<T>`
- Blazor components use `HttpClient` for SSE subscription

✅ **Standard HTTP Semantics**
- SSE uses HTTP/1.1 or HTTP/2 (no WebSocket upgrade)
- Works through HTTP proxies and firewalls
- Compatible with standard load balancers

✅ **Easier Debugging**
- SSE events visible in browser Network tab
- Plain-text `text/event-stream` format
- No binary WebSocket frames to decode

✅ **Reference Architecture Clarity**
- Shows when to use SSE vs. SignalR (pattern recognition)
- Demonstrates .NET 10 native SSE capabilities
- Simpler example for developers learning from CritterSupply

### Negative

⚠️ **One-Way Communication Only**
- If we need client→server push in the future, must use separate HTTP POST endpoints
- **Mitigation:** Our current use case is server→client push only (cart updates, order status)

⚠️ **No Built-In Presence Detection**
- SignalR tracks connected clients automatically (`Clients.All`, `Clients.Group("cart:123")`)
- SSE requires manual tracking of active subscriptions
- **Mitigation:** Store `CartId → ConnectionId` mapping in Redis/in-memory cache

⚠️ **Browser Limit (HTTP/1.1)**
- HTTP/1.1 has 6 concurrent connections per domain limit
- Multiple SSE streams count toward limit
- **Mitigation:** Use HTTP/2 (no connection limit) or multiplex events over single stream

### Trade-Offs Accepted

We accept the following limitations in exchange for simplicity:
1. No bidirectional push (use HTTP POST for commands)
2. Manual connection tracking (store active subscriptions in cache)
3. Single SSE stream per client (multiplex cart + order + shipment events over one connection)

---

## Alternatives Considered

### Alternative 1: SignalR with WebSockets

**Pros:**
- Built-in connection management, automatic reconnection
- Supports bidirectional push (client→server without HTTP POST)
- Protocol negotiation (fallback to Long Polling if WebSockets blocked)

**Cons:**
- Requires client library (`@microsoft/signalr`)
- More complex implementation (hubs, groups, connection IDs)
- Overkill for one-way server→client push

**Verdict:** ❌ Rejected - Unnecessary complexity for our use case

---

### Alternative 2: WebSockets (Manual Implementation)

**Pros:**
- Full-duplex communication (bidirectional)
- No SSE browser connection limit

**Cons:**
- Manual connection management (heartbeats, reconnection, timeouts)
- Binary protocol (harder debugging than SSE text-based)
- Not idiomatic .NET (no built-in `IAsyncEnumerable<T>` pattern)

**Verdict:** ❌ Rejected - Too low-level, reinventing SignalR/SSE abstractions

---

### Alternative 3: Long Polling

**Pros:**
- Works everywhere (no WebSocket/SSE support needed)
- Simple HTTP polling (client requests updates every N seconds)

**Cons:**
- Higher latency (poll interval delay)
- Increased server load (constant HTTP requests)
- Inefficient vs. SSE (requires new HTTP request for each poll)

**Verdict:** ❌ Rejected - SSE is more efficient and lower latency

---

## Implementation Notes

### SSE Endpoint Pattern

```csharp
// BFF: Storefront/Notifications/StorefrontHub.cs
public static class StorefrontHub
{
    [WolverineGet("/sse/storefront")]
    public static async IAsyncEnumerable<StorefrontEvent> SubscribeToUpdates(
        Guid customerId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Subscribe to multiple event types over single SSE stream
        await foreach (var @event in GetEventStream(customerId).WithCancellation(ct))
        {
            yield return @event;
        }
    }

    private static async IAsyncEnumerable<StorefrontEvent> GetEventStream(Guid customerId)
    {
        // Multiplex cart updates, order status, shipment tracking over one stream
        // Listen to RabbitMQ integration messages, push to clients
    }
}
```

### Blazor Client Pattern

```csharp
// Storefront.Web/Pages/Cart.razor
@inject HttpClient Http
@implements IAsyncDisposable

<h1>Shopping Cart</h1>
@if (cart is not null)
{
    <CartSummary Cart="@cart" />
}

@code {
    private CartView? cart;
    private CancellationTokenSource? cts;

    protected override async Task OnInitializedAsync()
    {
        // Initial cart load
        cart = await Http.GetFromJsonAsync<CartView>($"/api/carts/{CartId}");

        // Subscribe to SSE updates
        cts = new CancellationTokenSource();
        _ = Task.Run(() => SubscribeToCartUpdates(cts.Token));
    }

    private async Task SubscribeToCartUpdates(CancellationToken ct)
    {
        await foreach (var update in Http.GetFromJsonAsAsyncEnumerable<CartUpdate>(
            $"/sse/storefront?customerId={CustomerId}", ct))
        {
            if (update.CartId == CartId)
            {
                // Refresh cart state from BFF
                cart = await Http.GetFromJsonAsync<CartView>($"/api/carts/{CartId}");
                await InvokeAsync(StateHasChanged); // Re-render Blazor component
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        cts?.Cancel();
        cts?.Dispose();
    }
}
```

---

## Success Criteria

✅ SSE endpoint accepts client connections at `/sse/storefront`
✅ Integration messages from domain BCs trigger SSE push to connected clients
✅ Blazor cart page updates in real-time when items added/removed
✅ Integration tests verify SSE delivery (Alba + TestContainers)

---

## References

- [.NET 10 IAsyncEnumerable SSE Support](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses#stream-json-using-iasyncenumerablet)
- [MDN: Server-Sent Events](https://developer.mozilla.org/en-US/docs/Web/API/Server-sent_events)
- [SignalR vs SSE Comparison (Stack Overflow)](https://stackoverflow.com/questions/28582935/what-is-the-difference-between-websocket-and-server-sent-events)
- [Cycle 16 Plan](../planning/cycles/cycle-16-customer-experience.md)
- [CONTEXTS.md - Customer Experience](../../CONTEXTS.md#customer-experience)

---

**Decision Made By:** Erik Shafer / Claude AI Assistant
**Approved By:** [To be updated after implementation review]
