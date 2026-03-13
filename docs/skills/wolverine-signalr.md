# Wolverine + SignalR: Real-Time Transport Patterns

Comprehensive guide for using Wolverine's native SignalR transport in CritterSupply — covering server setup, hub design, authentication, client integration, the SignalR Client transport, group-based routing, Marten side-effect pipelines, and lessons learned from the Storefront and Vendor Portal bounded contexts.

## When to Use This Skill

**Use Wolverine's SignalR transport when:**
- You need **bidirectional** real-time communication (client→server commands + server→client push)
- A bounded context needs live analytics, notifications, or confirmations without HTTP round trips
- The Vendor Portal or any multi-tenant hub requires group-scoped delivery
- You are building a new BFF from scratch — adopt SignalR from day one, never defer it

**Do not use Wolverine's SignalR transport when:**
- Strict unidirectional push is sufficient *and* the BC will never need client→server messages (rare — most UIs evolve toward bidirectionality)
- You need server-to-server messaging — that is RabbitMQ's job

**CritterSupply Usage:**
- **Storefront BC** — `StorefrontHub` at `/hub/storefront`, session-cookie auth, customer-scoped groups, carries `CartUpdated`, `OrderStatusChanged`, `ShipmentStatusChanged`
- **Vendor Portal BC** *(planned)* — `VendorPortalHub` at `/hub/vendor-portal`, JWT auth, dual groups (`vendor:{tenantId}` + `user:{userId}`)

---

## Core Concepts

### The CloudEvents Envelope

Wolverine's SignalR transport wraps every message in a lightweight [CloudEvents](https://cloudevents.io/) JSON envelope before sending it to the client. This is the **contract** between your server and any JavaScript or Blazor client:

```json
{
  "specversion": "1.0",
  "type": "CritterSupply.Storefront.RealTime.CartUpdated",
  "source": "storefront-api",
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "time": "2026-03-07T06:00:00Z",
  "datacontenttype": "application/json",
  "data": {
    "cartId": "...",
    "customerId": "...",
    "itemCount": 3,
    "totalAmount": 42.99,
    "occurredAt": "2026-03-07T06:00:00Z"
  }
}
```

The `type` field is the fully-qualified .NET type name. The `data` field is the serialized message payload. You **must** unwrap `data` in your JavaScript client before processing — the payload is not the top-level object.

> **Kebab-case alias:** When your message implements `WebSocketMessage` (the Wolverine base marker), Wolverine uses a kebab-cased alias as the `type` (e.g., `cart-updated` instead of the full CLR name). CritterSupply uses custom marker interfaces instead — see the Marker Interfaces section.

### The WolverineHub

`WolverineHub` is the single pre-built hub type that Wolverine registers. It has one hard-coded client method: **`ReceiveMessage`**. All outgoing messages from the server go through this method name. Your JavaScript client must listen on `"ReceiveMessage"`:

```javascript
connection.on("ReceiveMessage", (cloudEvent) => { /* ... */ });
```

Incoming messages from the client are sent via `connection.invoke("ReceiveMessage", JSON.stringify(message))` — yes, the same method name is used in both directions.

### Custom Hubs

If `WolverineHub` is not sufficient (e.g., you need `[Authorize]`, custom `OnConnectedAsync` group management, or specific hub options), subclass it:

```csharp
// Must inherit from WolverineHub (not plain Hub)
// Can override OnConnectedAsync / OnDisconnectedAsync
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class VendorPortalHub : WolverineHub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User!.FindFirst("VendorTenantId")?.Value;
        var userId = Context.User!.FindFirst("VendorUserId")?.Value;

        if (tenantId is not null && userId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"vendor:{tenantId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        }

        await base.OnConnectedAsync();
    }
}
```

> **Inheritance requirement depends on your use case:**
> - **Client→server Wolverine routing** (clients send messages **via WebSocket** that Wolverine dispatches to handlers): your custom hub **must** inherit from `WolverineHub`, not plain `Hub`. The `ReceiveMessage` override that feeds the Wolverine pipeline lives in `WolverineHub`.
> - **Server→client push only** (Wolverine publishes to the hub; clients only receive): a plain `Hub` subclass with `app.MapHub<T>()` is sufficient. Wolverine delivers via `IHubContext<T>` and does not require `WolverineHub` for outbound-only flows.
>
> **Clarification from Cycle 22 (Vendor Portal):** "Bidirectional workflow" does NOT automatically mean `WolverineHub`. The Vendor Portal change request workflow (Phase 4) is end-to-end bidirectional — vendors submit requests and receive decisions. But `VendorPortalHub` remained a plain `Hub` because client→server actions (submit, withdraw, provide info) are **HTTP endpoints**. SignalR is only used for server→client notifications (decision toasts, status badge updates). `WolverineHub` is needed only when the **client sends messages via the WebSocket connection** itself (not via HTTP) and expects Wolverine to route those messages to handlers.
>
> - **Storefront `StorefrontHub : Hub`** — server→client push only. Handlers return messages; Wolverine routes via `IHubContext`.
> - **Vendor Portal `VendorPortalHub : Hub`** — server→client push only, even for a bidirectional workflow. HTTP handles client→server; SignalR handles server→client.
>
> Always call `await base.OnConnectedAsync()` — skipping it will break group tracking.

---

## Server Configuration

### Package Reference

```bash
dotnet add package WolverineFx.SignalR
```

This also pulls in the ASP.NET Core SignalR infrastructure — you do not need a separate `Microsoft.AspNetCore.SignalR` package reference.

### Program.cs Setup

```csharp
// ── 1. Wolverine configuration (in UseWolverine block) ──────────────────────
builder.Host.UseWolverine(opts =>
{
    // Single call wires up Wolverine's SignalR transport AND calls
    // IServiceCollection.AddSignalR() on your behalf
    opts.UseSignalR();

    // Custom hub variant:
    // opts.UseSignalR<VendorPortalHub>();

    // Publish rule: route all IStorefrontWebSocketMessage to the SignalR hub
    opts.Publish(x =>
    {
        x.MessagesImplementing<IStorefrontWebSocketMessage>();
        x.ToSignalR();
    });

    // ... RabbitMQ, handler discovery, etc.
});

// ── 2. Map the hub route ──────────────────────────────────────────────────────
var app = builder.Build();

// Default hub (WolverineHub):
app.MapWolverineSignalRHub("/hub/storefront")
   .DisableAntiforgery();  // Required in ASP.NET Core 10+ (see Anti-Forgery section)

// Custom hub:
// app.MapWolverineSignalRHub<VendorPortalHub>("/hub/vendor-portal")
//    .DisableAntiforgery();
```

> **`AddSignalR()` and `opts.UseSignalR()` together:** `opts.UseSignalR()` calls `IServiceCollection.AddSignalR()` internally, so you do not need an explicit `builder.Services.AddSignalR()` call *unless* you need additional configuration such as a Redis backplane or custom `HubOptions`. Calling both is safe because `AddSignalR()` uses `TryAdd` internally and is idempotent — the Storefront.Api does this today to keep CORS and backplane options co-located. The important rule is: **don't register the full SignalR services twice with conflicting options**.

### Anti-Forgery on Hub Routes (ASP.NET Core 10+)

ASP.NET Core 10 enables anti-forgery protection on SignalR hub endpoints by default. Whether to disable it depends on your authentication strategy:

**For hubs using JWT/bearer-only authentication** (e.g., Vendor Portal), anti-forgery protection can safely be disabled. JWT tokens are not sent automatically by browsers, so there is no ambient credential for a cross-site request to exploit:

```csharp
// ✅ JWT-authenticated hub — safe to disable antiforgery (no ambient browser credentials)
app.MapWolverineSignalRHub<VendorPortalHub>("/hub/vendor-portal")
   .DisableAntiforgery();
```

**For hubs using session-cookie authentication** (e.g., Storefront), consider the tradeoffs carefully. WebSocket upgrade requests are CSRF-safe in most modern browsers (browsers enforce same-origin restrictions on WebSocket upgrades), but the SignalR negotiation handshake involves an initial HTTP POST that *can* carry cookies cross-site. Disabling anti-forgery on the negotiation endpoint is what ASP.NET Core requires in many cross-origin dev setups; however, if you rely on cookies, ensure you have cross-origin restrictions in place (see the CORS configuration above):

```csharp
// ✅ Cookie-authenticated hub — disabling antiforgery is required in cross-origin dev/E2E setups
// Mitigate CSRF risk with strict CORS policy (AllowedOrigins, not AllowAnyOrigin)
app.MapHub<StorefrontHub>("/hub/storefront")
   .DisableAntiforgery();
```

The bottom line: **do not leave this decision on autopilot**. Pair `.DisableAntiforgery()` with the appropriate authentication mechanism and CORS policy for your hub.

---

## Marker Interfaces and Message Routing

### Defining Marker Interfaces

Marker interfaces live in the **domain project** (e.g., `Storefront/`, `VendorPortal/`), not in the API project. They express routing intent at the domain level.

**Single-group example (Storefront):**

```csharp
// Storefront/RealTime/IStorefrontWebSocketMessage.cs
namespace Storefront.RealTime;

/// <summary>
/// Marker interface for messages routed to SignalR hub via Wolverine.
/// Enables: opts.Publish(x => x.MessagesImplementing<IStorefrontWebSocketMessage>().ToSignalR())
/// </summary>
public interface IStorefrontWebSocketMessage
{
    /// <summary>
    /// Customer ID — used to target the "customer:{customerId}" hub group.
    /// </summary>
    Guid CustomerId { get; }
}
```

**Dual-group example (Vendor Portal):**

```csharp
// VendorPortal/RealTime/IVendorTenantMessage.cs
// Routes to "vendor:{tenantId}" — all users in a tenant receive this
public interface IVendorTenantMessage
{
    Guid VendorTenantId { get; }
}

// VendorPortal/RealTime/IVendorUserMessage.cs
// Routes to "user:{userId}" — only the individual user receives this
public interface IVendorUserMessage
{
    Guid VendorUserId { get; }
}
```

### Message Type Definitions

Messages are **sealed records** implementing the appropriate marker interface:

```csharp
// Storefront/RealTime/StorefrontEvent.cs
public sealed record CartUpdated(
    Guid CartId,
    Guid CustomerId,
    int ItemCount,
    decimal TotalAmount,
    DateTimeOffset OccurredAt) : IStorefrontWebSocketMessage;

public sealed record OrderStatusChanged(
    Guid OrderId,
    Guid CustomerId,
    string NewStatus,
    DateTimeOffset OccurredAt) : IStorefrontWebSocketMessage;
```

### Notification Handlers — Pure Function Return Pattern

Handlers that translate integration messages into hub messages are thin and pure:

```csharp
// Storefront/Notifications/OrderPlacedHandler.cs
public static class OrderPlacedHandler
{
    // Return a SignalR message — Wolverine routes it via the publish rule.
    // No IHubContext injection required. No EventBroadcaster. No channels.
    public static OrderStatusChanged Handle(Messages.Contracts.Orders.OrderPlaced message)
    {
        return new OrderStatusChanged(
            message.OrderId,
            message.CustomerId,
            "Placed",
            DateTimeOffset.UtcNow);
    }
}
```

The handler returns the message; Wolverine sees it implements `IStorefrontWebSocketMessage`, applies the publish rule, and routes it to the SignalR hub. Zero additional wiring.

---

## Hub Group Management

### Server-Side Group Enrollment

Groups are enrolled in `OnConnectedAsync`. Group names follow a `{scope}:{id}` convention:

| Scope | Pattern | When to Use |
|-------|---------|-------------|
| Single-tenant user | `customer:{customerId}` | Storefront — one customer per group |
| Tenant-wide | `vendor:{tenantId}` | Vendor Portal — all users in a tenant |
| Per-user | `user:{userId}` | Vendor Portal — individual notifications |

**Storefront example (server→client push only — uses plain `Hub`, identity from session):**

```csharp
// StorefrontHub uses plain Hub (not WolverineHub) because it only needs server→client push.
// Wolverine delivers outbound messages via IHubContext<StorefrontHub>.
// Note: customerId in query string is acceptable here because the Storefront Blazor app
// derives it from the server-side session cookie — the value is tied to the authenticated
// session, not accepted as a trust anchor in its own right. For vendor-facing BCs that hold
// commercially sensitive data, always use JWT claims (see VendorPortalHub below).
public sealed class StorefrontHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var customerId = Context.GetHttpContext()?.Request.Query["customerId"].ToString();

        if (!string.IsNullOrEmpty(customerId) && Guid.TryParse(customerId, out var customerGuid))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"customer:{customerGuid}");
        }

        await base.OnConnectedAsync();
    }
}
```

**Vendor Portal example (tenantId from JWT claims — never query string):**

```csharp
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class VendorPortalHub : WolverineHub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User!.FindFirst("VendorUserId")?.Value;
        var tenantId = Context.User!.FindFirst("VendorTenantId")?.Value;
        var tenantStatus = Context.User!.FindFirst("VendorTenantStatus")?.Value;

        // Reject suspended/terminated tenants at connection time
        if (tenantStatus is "Suspended" or "Terminated")
        {
            Context.Abort();
            return;
        }

        if (userId is not null && tenantId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, $"vendor:{tenantId}");
        }

        await base.OnConnectedAsync();
    }
}
```

### Handler-Driven Group Management

For groups that clients join/leave dynamically, Wolverine provides side-effect types:

```csharp
public record EnrollInChannel(string GroupName) : WebSocketMessage;
public record LeaveChannel(string GroupName) : WebSocketMessage;

public static class ChannelHandlers
{
    // AddConnectionToGroup is a Wolverine side effect — adds the originating connection
    public static AddConnectionToGroup Handle(EnrollInChannel msg)
        => new(msg.GroupName);

    // RemoveConnectionToGroup removes the originating connection from the group
    public static RemoveConnectionToGroup Handle(LeaveChannel msg)
        => new(msg.GroupName);
}
```

### Targeted Group Publishing (ToWebSocketGroup)

To send from server code to a named group:

```csharp
public sealed record LowStockAlertRaised(
    string Sku,
    int CurrentQty,
    Guid VendorTenantId,
    DateTimeOffset OccurredAt) : IVendorTenantMessage;

public static class LowStockHandler
{
    public static SignalRMessage<LowStockAlertRaised> Handle(LowStockDetected message)
    {
        var alert = new LowStockAlertRaised(message.Sku, message.CurrentQty,
            message.VendorTenantId, DateTimeOffset.UtcNow);

        // .ToWebSocketGroup() sends ONLY to the named group
        return alert.ToWebSocketGroup($"vendor:{message.VendorTenantId}");
    }
}
```

### Responding to the Originating Connection

To reply specifically to the connection that sent a message:

```csharp
public record ApplyCoupon(string Code) : WebSocketMessage;
public record CouponApplied(string Code, decimal Discount) : WebSocketMessage;
public record CouponRejected(string Code, string Reason) : WebSocketMessage;

public static class CouponHandler
{
    public static ResponseToCallingWebSocket<CouponApplied> Handle(ApplyCoupon message)
    {
        // RespondToCallingWebSocket() sends back to ONLY the connection that sent this message
        return new CouponApplied(message.Code, 0.10m).RespondToCallingWebSocket();
    }
}
```

---

## Authentication Patterns

CritterSupply uses two different authentication mechanisms for SignalR hubs, deliberately demonstrating both approaches.

### Pattern A: Session Cookies (Storefront BC)

The Storefront uses session-cookie auth. The hub does **not** use `[Authorize]`. The `customerId` is passed as a query string parameter on the WebSocket upgrade request and used to enroll the connection in the correct group. This is acceptable because:
- The Blazor app sets `customerId` from the server-side session (a claim the user cannot forge)
- Session-backed identity provides the actual trust; the GUID is an identifier, not a secret

```csharp
// Plain Hub (not WolverineHub) — server→client push only.
// No [Authorize] attribute — hub is open, but group enrollment is conditional on a valid
// customerId being present.
public sealed class StorefrontHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var customerId = Context.GetHttpContext()?.Request.Query["customerId"].ToString();

        if (!string.IsNullOrEmpty(customerId) && Guid.TryParse(customerId, out var customerGuid))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"customer:{customerGuid}");
        }

        await base.OnConnectedAsync();
    }
}
```

> **Security note:** Passing `customerId` in the query string is only acceptable when the *source* of that value is server-side authenticated identity (e.g., a claim extracted from the session cookie before the Blazor page renders). GUIDs are identifiers, not secrets — do not rely on "hard to guess" as a security property. For vendor-facing contexts where the `VendorTenantId` must be cryptographically verified, always derive group keys from JWT claims server-side (Pattern B).

### Pattern B: JWT Bearer (Vendor Portal BC)

The Vendor Portal uses JWT auth (ADR 0028). WebSocket upgrade requests cannot carry an `Authorization` header in all browsers, so the JWT is extracted from the query string (`?access_token=...`) via `JwtBearerEvents.OnMessageReceived`:

```csharp
// VendorPortal.Api/Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "vendor-identity",
            ValidateAudience = true,
            ValidAudience = "vendor-portal",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes)
        };

        // Extract JWT from query string for SignalR WebSocket upgrades
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Query["access_token"] returns StringValues — call .ToString() to get a
                // single string value (empty string if absent, not null).
                var accessToken = context.Request.Query["access_token"].ToString();
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hub/vendor-portal"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
```

The hub then carries `[Authorize]` and reads all identity from `Context.User` (backed by verified JWT claims):

```csharp
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class VendorPortalHub : WolverineHub
{
    public override async Task OnConnectedAsync()
    {
        // VendorTenantId comes ONLY from cryptographically-verified JWT claims.
        // NEVER from query string, NEVER from request body.
        var tenantId = Context.User!.FindFirst("VendorTenantId")?.Value;
        var userId = Context.User!.FindFirst("VendorUserId")?.Value;
        // ...
    }
}
```

### Wolverine Client Transport Authentication

When using the Wolverine SignalR Client transport (see [SignalR Client Transport](#signalr-client-transport)) to connect to an `[Authorize]`-protected hub, provide an `accessTokenProvider`:

```csharp
opts.UseClientToSignalR(Port, accessTokenProvider: (sp) => () =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var token = config.GetValue<string?>("SignalR:AccessToken");
    return Task.FromResult<string?>(token);
});

// Or per-publish rule:
opts.Publish(x =>
{
    x.MessagesImplementing<IVendorPortalMessage>();
    x.ToSignalRWithClient(Port, accessTokenProvider: (sp) => () =>
    {
        // Last configuration wins — applies to the client URL, not the message type
        return Task.FromResult<string?>(GetToken(sp));
    });
});
```

---

## Marten Projection Side Effects Pipeline

One of the most powerful Wolverine + SignalR patterns: Marten projection side effects publish messages that Wolverine routes directly to the SignalR hub. Zero manual bridging code.

```csharp
// Full reactive pipeline:
// Domain Event → Marten projection → side effect message → Wolverine → SignalR hub → Client

public sealed class CartSummaryProjection : SingleStreamProjection<CartSummary>
{
    // ... Apply methods ...

    public override ValueTask RaiseSideEffects(
        IDocumentOperations ops,
        IEventSlice<CartSummary> slice)
    {
        if (slice.Snapshot is not null)
        {
            // Publish message — Wolverine sees it implements IStorefrontWebSocketMessage
            // and routes it to the SignalR hub automatically
            slice.PublishMessage(new CartUpdated(
                slice.Snapshot.Id,
                slice.Snapshot.CustomerId,
                slice.Snapshot.Items.Count,
                slice.Snapshot.TotalAmount,
                DateTimeOffset.UtcNow));
        }

        return ValueTask.CompletedTask;
    }
}
```

This pipeline enables:
- **Cart projections** → live cart totals in the browser
- **Order saga state** → step-by-step order progress UI
- **Vendor analytics projections** → live dashboard updates as orders flow in

See `docs/skills/marten-event-sourcing.md` for projection patterns. Note that `RaiseSideEffects` runs *after* projection state is committed, so the message reflects confirmed state.

---

## Client-Side Integration

### JavaScript Client (Non-Blazor or JS-Heavy Pages)

CritterSupply's Storefront uses a vanilla JavaScript wrapper (`signalr-client.js`) that abstracts connection lifecycle and CloudEvents unwrapping:

```javascript
// wwwroot/js/signalr-client.js
window.signalrClient = {
    connection: null,
    dotNetHelper: null,

    subscribe: async function(customerId, dotNetHelper, hubUrl) {
        this.dotNetHelper = dotNetHelper;

        if (this.connection) {
            await this.connection.stop();
        }

        // customerId is passed in query string for session-cookie auth
        const url = `${hubUrl}?customerId=${customerId}`;

        this.connection = new signalR.HubConnectionBuilder()
            .withUrl(url, {
                transport: signalR.HttpTransportType.WebSockets,
                // Skip the HTTP negotiate POST and connect directly via WebSocket.
                // Required for cross-origin setups (Blazor.Web on port 5238, API on port 5237).
                // Safe because transport is already locked to WebSockets — no fallback needed.
                skipNegotiation: true
            })
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: retryContext => {
                    if (retryContext.previousRetryCount === 0) return 0;
                    if (retryContext.previousRetryCount === 1) return 2000;
                    if (retryContext.previousRetryCount === 2) return 10000;
                    return 30000;  // 30s cap
                }
            })
            .configureLogging(signalR.LogLevel.Information)
            .build();

        // WolverineHub sends all messages to the "ReceiveMessage" client method
        this.connection.on("ReceiveMessage", (cloudEvent) => {
            try {
                // Unwrap the CloudEvents envelope — data is the actual payload.
                // Use explicit type mapping (not a generic kebab-case converter) so that
                // the Blazor components receive a consistent eventType discriminator.
                const messageType = cloudEvent.type || "";
                const typeName = messageType.split(".").pop(); // e.g. "CartUpdated"

                let eventType = "";
                if (typeName === "CartUpdated") {
                    eventType = "cart-updated";
                } else if (typeName === "OrderStatusChanged") {
                    eventType = "order-status-changed";
                } else if (typeName === "ShipmentStatusChanged") {
                    eventType = "shipment-status-changed";
                }

                const unwrapped = { eventType: eventType, ...cloudEvent.data };
                this.dotNetHelper.invokeMethodAsync("OnSseEvent", unwrapped);
            } catch (err) {
                console.error("Failed to process SignalR message:", err);
            }
        });

        this.connection.onreconnecting(err => console.warn("SignalR reconnecting:", err));
        this.connection.onreconnected(id => console.log("SignalR reconnected:", id));
        this.connection.onclose(err => console.error("SignalR closed:", err));

        await this.connection.start();
    },

    unsubscribe: async function() {
        if (this.connection) {
            await this.connection.stop();
            this.connection = null;
        }
        this.dotNetHelper = null;
    }
};
```

**HTML — load SignalR before your client script:**

```html
<!-- App.razor or _Layout.cshtml -->
<!-- Pin the version — avoid unpinned CDN references in production -->
<script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js"></script>
<script src="js/signalr-client.js"></script>
```

### Blazor Component Integration (JS Interop Pattern)

CritterSupply's Blazor pages use JS interop to drive the JavaScript client. The component exposes a `[JSInvokable]` callback that JavaScript calls when a CloudEvents message arrives:

```csharp
// Components/Pages/Cart.razor (relevant lifecycle + SignalR code)
@inject IJSRuntime JS
@inject NavigationManager Navigation
@inject IConfiguration Configuration
@implements IAsyncDisposable

@code {
    private DotNetObjectReference<Cart>? _dotNetHelper;
    private Guid? _customerId;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        _dotNetHelper = DotNetObjectReference.Create(this);

        // For cross-origin deployments (Blazor.Web on a different port than Storefront.Api),
        // read the hub URL from configuration instead of using NavigationManager, which
        // would resolve to the Blazor host origin, not the API origin.
        var hubUrl = Configuration["ApiClients:StorefrontApiUrl"] is { } apiUrl
            ? $"{apiUrl.TrimEnd('/')}/hub/storefront"
            : Navigation.ToAbsoluteUri("/hub/storefront").ToString();

        if (!_customerId.HasValue)
        {
            Console.WriteLine("Cannot subscribe to SignalR: customerId not yet resolved.");
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("signalrClient.subscribe",
                _customerId.Value.ToString(), _dotNetHelper, hubUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to subscribe to SignalR: {ex.Message}");
        }
    }

    // Called by JavaScript when a CloudEvents message arrives
    [JSInvokable]
    public async Task OnSseEvent(JsonElement eventData)
    {
        var eventType = eventData.TryGetProperty("eventType", out var et)
            ? et.GetString() : null;

        switch (eventType)
        {
            case "cart-updated":
                await LoadCart();
                break;
            case "order-status-changed":
                // handle order update
                break;
        }

        await InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("signalrClient.unsubscribe");
        }
        catch { /* component may be disposed during page navigation */ }

        _dotNetHelper?.Dispose();
    }
}
```

> **Important:** Always implement `IAsyncDisposable` on components that subscribe to SignalR. Failing to stop the connection on dispose causes memory leaks and ghost connections that continue receiving messages for disconnected sessions.

### JWT-Authenticated JavaScript Client (Vendor Portal Pattern)

For JWT-authenticated hubs, pass the access token via `accessTokenFactory` in the `HubConnectionBuilder`:

```javascript
// For Vendor Portal — JWT is stored in memory (not localStorage, not cookies)
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hub/vendor-portal", {
        // SignalR client appends this as ?access_token=... on the WebSocket upgrade
        accessTokenFactory: () => getAccessTokenFromMemory()
    })
    .withAutomaticReconnect()
    .build();

// On token refresh, the next reconnect attempt will pick up the new token
// automatically via the factory function
```

**Token refresh on reconnect:** The `accessTokenFactory` is called on every connection attempt, including automatic reconnects. Store the current JWT in a module-level variable and update it when the refresh endpoint returns a new token — the reconnect will automatically use the fresh token.

---

## SignalR Client Transport

The `WolverineFx.SignalR` package includes a second transport: the **.NET SignalR Client transport**. This acts as a full Wolverine messaging endpoint built on top of the SignalR .NET client SDK. It is primarily designed for **integration testing** against a real Wolverine SignalR server — but it is a legitimate messaging transport in its own right.

### Integration Testing with the SignalR Client Transport

The key constraint (from official Wolverine docs):

> If you want to use the .NET SignalR Client for test automation, you will need to bootstrap the service that actually hosts SignalR with **full Kestrel** — `WebApplicationFactory` will not work.

```csharp
// In your test fixture — start the app with real Kestrel on a dynamically assigned port
public class StorefrontSignalRTestFixture : IAsyncLifetime
{
    private WebApplication? _app;
    protected IHost? ClientHost;
    private int _port;  // assigned after Kestrel starts

    public async Task InitializeAsync()
    {
        // Boot the real app and let the OS pick an available port (port 0)
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(opts => opts.ListenLocalhost(0));

        // ... Full app configuration ...

        _app = builder.Build();
        await _app.StartAsync();

        // Discover the actual port Kestrel bound to
        var url = _app.Urls.Single();
        _port = new Uri(url
            .Replace("//[::]:","//localhost:")      // IPv6 wildcard → localhost
            .Replace("//0.0.0.0:","//localhost:")   // IPv4 wildcard → localhost
        ).Port;

        // Boot a Wolverine host with the SignalR Client transport
        ClientHost = await Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                opts.UseClientToSignalR(_port);

                // Publish messages to the server via SignalR client
                opts.Publish(x =>
                {
                    x.MessagesImplementing<IStorefrontWebSocketMessage>();
                    x.ToSignalRWithClient(_port);
                });
            }).StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (ClientHost is not null) await ClientHost.StopAsync();
        if (_app is not null) await _app.StopAsync();
    }
}
```

### Verifying End-to-End SignalR Delivery

Use Wolverine's [tracked sessions](https://wolverinefx.net/guide/testing#integration-testing-with-tracked-sessions) to assert that a message travels end-to-end through the SignalR transport:

```csharp
[Fact]
public async Task order_placed_sends_order_status_changed_to_hub()
{
    var tracked = await ClientHost
        .TrackActivity()
        .IncludeExternalTransports()
        .AlsoTrack(_app)  // Track the server-side app too
        .Timeout(TimeSpan.FromSeconds(10))
        .ExecuteAndWaitAsync(c =>
            c.PublishAsync(new OrderPlaced(OrderId: Guid.NewGuid(), CustomerId: Guid.NewGuid())));

    var received = tracked.Received.SingleRecord<OrderStatusChanged>();
    received.ServiceName.ShouldBe("StorefrontApi");
    received.Envelope.Destination.ShouldBe(new Uri("signalr://wolverine"));
}
```

### Absolute URL vs Port Number

Two equivalent ways to configure the SignalR Client transport:

```csharp
// By absolute URL (useful in remote environments or Aspire service discovery)
var url = config.GetValue<string>("signalr.url");
opts.UseClientToSignalR(url);
opts.Publish(x => x.MessagesImplementing<WebSocketMessage>().ToSignalRWithClient(url));

// By port number (useful for local/test scenarios)
int port = 5555;
opts.UseClientToSignalR(port);
opts.Publish(x => x.MessagesImplementing<WebSocketMessage>().ToSignalRWithClient(port));
```

---

## WebSocket Sagas

Wolverine supports a "scatter/gather" pattern for long-running WebSocket workflows:

1. Browser sends a WebSocket message to the server
2. Server processes several messages or calls other Wolverine services
3. Server sends the final result back to the **originating SignalR connection** — even if intermediate steps traversed other services

Use `[EnlistInCurrentConnectionSaga]` on commands that participate in this workflow. This correlates intermediate messages back to the original WebSocket connection without additional tracking code.

This pattern is specifically useful for vendor change requests — the vendor submits a request, the Catalog BC processes it across multiple async steps, and the final approval/rejection is routed back to the vendor's specific connection, not just the tenant group.

---

## Lessons Learned in CritterSupply

### What We Got Right

**1. Return typed messages from handlers — let Wolverine route them**

```csharp
// ✅ Clean: return a typed message, Wolverine handles routing
public static OrderStatusChanged Handle(OrderPlaced message)
    => new OrderStatusChanged(message.OrderId, message.CustomerId, "Placed", DateTimeOffset.UtcNow);
```

**2. Marker interfaces in the domain project**

`IStorefrontWebSocketMessage` lives in `Storefront/` (domain), not `Storefront.Api/`. This keeps routing intent at the domain level and keeps the API project as thin infrastructure wiring.

**3. Dual hub groups for multi-tenant portals**

`vendor:{tenantId}` for shared tenant notifications (stock alerts, analytics) + `user:{userId}` for individual notifications (change request decisions, force-logout). Clear, predictable routing.

**4. `.DisableAntiforgery()` on hub routes**

A one-liner that prevents confusing test failures and cross-origin breakage in ASP.NET Core 10+.

**5. Exponential backoff in the JavaScript client**

`withAutomaticReconnect` with custom retry delays (0ms → 2s → 10s → 30s) gives a good balance between reconnect speed and server load during outages.

---

### What We Got Wrong (Don't Repeat These)

**❌ Hand-rolling a broadcaster (`EventBroadcaster.cs` / `IEventBroadcaster.cs`)**

Before Wolverine's SignalR transport was adopted, CritterSupply had a 70-line `EventBroadcaster.cs` with `ConcurrentDictionary<Guid, List<Channel<StorefrontEvent>>>`, cleanup logic, and thread-safety bookkeeping. It reimplemented what SignalR already provides. This was entirely replaced by:

```csharp
opts.UseSignalR();
opts.Publish(x => x.MessagesImplementing<IStorefrontWebSocketMessage>().ToSignalR());
```

**Never hand-roll a broadcaster when `opts.UseSignalR()` is available.**

**❌ Using SSE when bidirectional was the real requirement**

The Storefront began with Server-Sent Events (SSE) — an intentional initial simplification. When checkout interactions, vendor dashboards, and interactive UI flows emerged, SSE's unidirectional constraint required an awkward "POST + wait for matching SSE event" ceremony. The migration to SignalR cost a full cycle.

**Build with SignalR from the start when any client→server messaging is foreseeable.**

**❌ Passing identity in query strings for vendor-grade contexts**

The `StorefrontHub` reads `customerId` from the query string — acceptable for a customer-facing site where session auth backs the identity. The Vendor Portal explicitly rejects this:

```csharp
// ❌ Do not do this for commercially sensitive, tenant-isolated data:
var tenantId = Context.GetHttpContext()?.Request.Query["tenantId"].ToString();

// ✅ Always use JWT claims for vendor identity:
var tenantId = Context.User!.FindFirst("VendorTenantId")?.Value;
```

`VendorTenantId` must **always** come from cryptographically-verified JWT claims. A malicious client could pass any `tenantId` in the query string, gaining access to another tenant's data.

**❌ Deferring CONTEXTS.md updates**

After the SSE→SignalR migration in the Storefront, CONTEXTS.md still contained SSE references for several cycles. This caused confusion when planning the Vendor Portal. Keep CONTEXTS.md accurate and current — it is the architectural source of truth.

**❌ Forgetting `IAsyncDisposable` on Blazor components with hub connections**

Blazor components that subscribe to SignalR must implement `IAsyncDisposable` and call `signalrClient.unsubscribe()` (or `hubConnection.DisposeAsync()`) in `DisposeAsync`. Without this, JavaScript connections persist after the component is removed, callbacks fire on disposed objects, and memory accumulates.

**❌ Not pinning the `@microsoft/signalr` CDN version**

Storefront.Web pins to `@microsoft/signalr@8.0.0`. Unpinned CDN references (`@latest`) can break silently when the SignalR client's message format changes between major versions. Always pin the CDN version and update intentionally.

---

## Scaling Considerations

### Backplane Requirement

SignalR connections are instance-affine — a message published to a hub group must be delivered by the instance that holds the group's connections. In a single-instance deployment this is transparent. With horizontal scaling (multiple `Storefront.Api` or `VendorPortal.Api` instances), you need a **backplane**:

```csharp
// Redis backplane (recommended — Redis is already planned for CritterSupply cache needs)
builder.Services.AddSignalR()
    .AddStackExchangeRedis(connectionString, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("storefront");
    });

// Azure SignalR Service (fully managed, scales to millions of connections)
opts.UseAzureSignalR(hub => { /* hub options */ }, service =>
{
    service.ApplicationName = "critter-supply";
    service.ConnectionString = config["AzureSignalR:ConnectionString"];
});
```

> **Note:** The hand-rolled `EventBroadcaster` singleton had the *same* scaling problem — it was in-memory only. Adopting SignalR makes this explicit and forces it to be properly addressed.

### Connection Lifecycle for Long-Lived Sessions

Vendor Portal sessions may run for 8+ hours. Plan for:

- **Visual "Live" indicator** — show connection status in the portal header. Vendors should know if they are receiving real-time data.
- **Reconnect-and-catch-up** — on reconnect, query for missed alerts since `lastSeenAt` timestamp. SignalR's auto-reconnect handles the WebSocket; your application must handle the data gap.
- **JWT token refresh** — 15-minute access tokens expire during long sessions. The JavaScript `accessTokenFactory` is re-called on each reconnect, so storing the latest token in memory and refreshing it proactively keeps connections alive without forcing re-login.

---

## Diagnostics

Inspect Wolverine's message routing for SignalR (including message type aliases) with:

```bash
dotnet run -- describe
```

Look for the "Message Routing" table in the output. SignalR-routed messages appear with destination `signalr://wolverine`.

---

## Common Pitfalls Summary

| Pitfall | Symptom | Fix |
|---------|---------|-----|
| Forgot `.DisableAntiforgery()` | Hub negotiation fails with 400/403 in ASP.NET Core 10+ | Add `.DisableAntiforgery()` to `MapWolverineSignalRHub()` |
| Custom hub inherits `Hub` not `WolverineHub` | Client→server messages not received / Wolverine routing breaks | Inherit `WolverineHub` — but only if clients send via WebSocket, not HTTP |
| Assuming bidirectional workflow → WolverineHub | Over-engineered hub; plain Hub works for HTTP commands + SignalR notifications | Use plain `Hub` when client→server is HTTP; `WolverineHub` only for WebSocket→Wolverine dispatch |
| Called `base.OnConnectedAsync()` wrong | Group enrollment partially works | Always call `await base.OnConnectedAsync()` last |
| Using `WebApplicationFactory` for SignalR integration tests | Tests hang / SignalR handshake fails | Use real Kestrel (`WebApplication` on a port) |
| Passing tenant identity in query string | Security vulnerability: tenant spoofing | Use JWT claims only for vendor-grade contexts |
| Component missing `IAsyncDisposable` | Memory leaks, ghost connections | Always implement `DisposeAsync` with `unsubscribe` call |
| Missing reconnect handler | Users lose live data silently | Register `onreconnecting` / `onreconnected` with UI feedback |
| Not pinning CDN version | Silent breaks on `@latest` updates | Pin to specific version (`@8.0.0`) |

---

## See Also

- **[BFF Real-Time Patterns](./bff-realtime-patterns.md)** — BFF project structure, view composition, HTTP client patterns
- **[Wolverine Message Handlers](./wolverine-message-handlers.md)** — Handler patterns, return types, compound handlers
- **[Marten Event Sourcing](./marten-event-sourcing.md)** — Projection side effects, RaiseSideEffects hook
- **[E2E Testing with Playwright](./e2e-playwright-testing.md)** — SignalR in browser E2E tests, antiforgery configuration
- **[ADR 0013: SignalR Migration from SSE](../decisions/0013-signalr-migration-from-sse.md)** — Full cost/benefit analysis of the SSE→SignalR migration
- **[ADR 0028: JWT for Vendor Identity](../decisions/0028-jwt-for-vendor-identity.md)** — JWT Bearer auth for SignalR hubs, VendorTenantId claim invariant
- **[Vendor Portal Event Modeling](../planning/vendor-portal-event-modeling.md)** — Dual hub group design, `VendorPortalHub` specification
- **[CONTEXTS.md — Customer Experience](../../CONTEXTS.md#customer-experience)** — Storefront BC SignalR integration contract
- **[CONTEXTS.md — Vendor Portal](../../CONTEXTS.md#vendor-portal)** — Vendor Portal BC SignalR integration contract

**External References:**
- [Wolverine SignalR Transport Docs](https://wolverinefx.io/guide/messaging/transports/signalr.html)
- [WolverineChat Sample App](https://github.com/JasperFx/wolverine/tree/main/src/Samples/WolverineChat)
- [ASP.NET Core SignalR Introduction](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0)
- [ASP.NET Core SignalR Authentication & Authorization](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz)
- [CloudEvents Specification](https://cloudevents.io/)
