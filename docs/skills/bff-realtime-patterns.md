# Backend-for-Frontend (BFF) & Real-Time Patterns

**Purpose:** Guide for building customer-facing frontends in CritterSupply that aggregate data from multiple bounded contexts (BCs) with real-time updates using the Wolverine SignalR transport.

**Audience:** Human developers and AI agents implementing or extending BFF layers in CritterSupply.

**Status:** Reflects Cycles 16–22 implementation (as of 2026-03-13). SignalR is the current standard.

---

## Table of Contents

- [Foundational Concept: What is a BFF?](#foundational-concept-what-is-a-bff)
- [When to Use a BFF](#when-to-use-a-bff)
- [Project Structure (CritterSupply Pattern)](#project-structure-crittersupply-pattern)
- [BFF Responsibilities and Boundaries](#bff-responsibilities-and-boundaries)
- [View Model Composition](#view-model-composition)
- [Cross-BC Query Orchestration](#cross-bc-query-orchestration)
- [Real-Time Patterns: SignalR](#real-time-patterns-signalr)
- [SignalR Implementation Guide](#signalr-implementation-guide)
- [Domain Event Aggregation](#domain-event-aggregation)
- [Client-Side Integration](#client-side-integration)
- [Testing BFF Components](#testing-bff-components)
- [Lessons Learned](#lessons-learned)
- [Key Principles Summary](#key-principles-summary)

---

## Foundational Concept: What is a BFF?

A **Backend-for-Frontend (BFF)** is an intermediate layer between the frontend and domain BCs that:

- **Composes** data from multiple BCs into frontend-optimized view models
- **Orchestrates** queries and commands across BCs for UI workflows
- **Aggregates** domain events into real-time notifications for the client
- **Does NOT contain domain logic** — it delegates to domain BCs

**Critical boundary:** The BFF is a **composition and translation layer**, not a domain layer. Any business rules, validation, or state transitions belong in domain BCs.

**Example violation:**
```csharp
// ❌ BAD — BFF calculating cart totals (domain logic)
var total = items.Sum(i => i.Quantity * i.UnitPrice);
var tax = total * 0.08m;
```

**Correct delegation:**
```csharp
// ✅ GOOD — BFF queries Orders BC for pre-calculated totals
var checkout = await ordersClient.GetCheckoutAsync(checkoutId);
return new CheckoutView(..., Total: checkout.Total);
```

---

## When to Use a BFF

**Use a BFF for:**
- Customer-facing web/mobile apps querying multiple BCs (e.g., Storefront, Backoffice)
- Real-time notification requirements (cart updates, order status, live analytics)
- Different client types with different composition needs (web vs. mobile)
- Complex UI workflows spanning multiple BCs (checkout wizard, vendor dashboards)

**Avoid a BFF for:**
- Internal admin tools with simple CRUD operations directly against a single BC
- APIs consumed by other backend services (use direct BC-to-BC messaging)
- Simple single-BC UIs with no real-time requirements

**CritterSupply Examples:**
- **Storefront BC** (Customer Experience) — BFF aggregating Shopping, Orders, Catalog, Fulfillment, Returns
- **Vendor Portal BC** — BFF aggregating Catalog, Inventory, Vendor Identity, with live analytics and change request notifications

---

## Project Structure (CritterSupply Pattern)

CritterSupply uses a **3-project structure** for BFF bounded contexts:

```
src/<BC Name>/
├── <ProjectName>/                      # Domain project (regular SDK)
│   ├── Clients/                        # HTTP client interfaces (domain)
│   ├── Composition/                    # View models for UI
│   ├── Notifications/                  # Integration message handlers
│   └── RealTime/                       # SignalR transport types
│
├── <ProjectName>.Api/                  # API project (Web SDK)
│   ├── Program.cs                      # Wolverine + Marten + SignalR + DI setup
│   ├── Queries/                        # HTTP endpoints (composition)
│   ├── Clients/                        # HTTP client implementations
│   └── *Hub.cs                         # SignalR hub
│
└── <ProjectName>.Web/                  # Blazor frontend
    ├── Components/Pages/*.razor
    └── wwwroot/js/signalr-client.js    # (Optional) JavaScript SignalR wrapper
```

**Key Configuration (Program.cs):**

```csharp
builder.Host.UseWolverine(opts =>
{
    // Discover handlers in BOTH API and Domain assemblies
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);
    opts.Discovery.IncludeAssembly(typeof(Storefront.RealTime.IStorefrontWebSocketMessage).Assembly);

    opts.UseSignalR();
    opts.Publish(x =>
    {
        x.MessagesImplementing<IStorefrontWebSocketMessage>();
        x.ToSignalR();
    });
});

app.MapWolverineSignalRHub<StorefrontHub>("/hub/storefront")
   .DisableAntiforgery(); // Required in ASP.NET Core 10+
```

**Common Mistakes to Avoid:**
- ❌ Single Web SDK project combining domain + infrastructure
- ❌ Domain project referencing Wolverine packages
- ❌ Forgetting to include domain assembly in `opts.Discovery.IncludeAssembly()`

---

## BFF Responsibilities and Boundaries

### What Belongs in the BFF

**1. View Model Composition**
- Aggregate data from multiple BCs into UI-optimized shapes
- Join data (e.g., cart items + product details)
- Pre-calculate display values (subtotals, line totals)
- Pre-format strings (address display lines, status labels)

**2. Query Orchestration**
- Coordinate parallel queries to multiple BCs
- Handle partial failures gracefully
- Cache frequently-accessed reference data

**3. Real-Time Notification Aggregation**
- Receive integration events from domain BCs
- Translate domain events into client-friendly notifications
- Route notifications to correct clients via SignalR groups

**4. Command Delegation**
- Forward commands to domain BCs (no validation or business logic)
- Translate HTTP 4xx/5xx responses into user-friendly error messages

### What Does NOT Belong in the BFF

**❌ Business Rules**
```csharp
// ❌ BAD — inventory availability check is domain logic
if (product.StockLevel < command.Quantity)
    return Results.BadRequest("Insufficient stock");
```

**❌ Validation, State Transitions, Calculations**
All of these belong in domain BCs. The BFF only delegates.

**✅ Correct Delegation Pattern:**
```csharp
// ✅ GOOD — forward command to domain BC
public static async Task<IResult> Handle(
    PlaceOrder command, IOrdersClient ordersClient, CancellationToken ct)
{
    try
    {
        var result = await ordersClient.PlaceOrderAsync(command, ct);
        return Results.Ok(result);
    }
    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
    {
        return Results.BadRequest("Unable to place order. Please verify your cart and payment details.");
    }
}
```

### BFF-Owned Projections vs Separate Analytics BC

**Key Decision from M32.0:** Backoffice BFF owns lightweight projections (`AdminDailyMetrics`) instead of creating a separate Analytics BC.

**Decision Tree:**

```
Should this be a BFF-owned projection or a separate BC?

├─ Q1: Is this projection used ONLY by this BFF?
│  ├─ YES → Q2
│  └─ NO (multiple consumers) → ❌ Separate BC required
│
├─ Q2: Is the projection logic simple aggregation (count, sum, average)?
│  ├─ YES → Q3
│  └─ NO (complex rules, ML models) → ❌ Separate BC required
│
├─ Q3: Does the projection require <10 domain events to build?
│  ├─ YES → ✅ BFF-owned projection
│  └─ NO (needs 20+ event types) → ⚠️  Consider separate BC
```

**When to use BFF-owned projections:**
- ✅ Operational dashboards (real-time KPIs, alert feeds, executive summary)
- ✅ Cross-BC aggregation for UI composition
- ✅ Inline lifecycle required (zero-lag updates)
- ✅ Queries are simple (load by ID, basic filtering)

**When to create separate Analytics BC:**
- ❌ Complex analytics (time-series analysis, forecasting, ML models)
- ❌ Long-term data warehousing (years of historical data)
- ❌ Heavy BI tooling integration (Tableau, Power BI, etc.)
- ❌ Async/batch processing (nightly aggregations, reports)

**BFF-Owned Projection Pattern:**

```csharp
// Integration Message Handler → Projection Query → SignalR
public static class OrderPlacedHandler
{
    public static async Task<LiveMetricUpdated> Handle(
        Orders.OrderPlaced message,
        IDocumentSession session)
    {
        // 1. Append event (triggers inline projection)
        session.Events.Append(Guid.NewGuid(), message);

        // 2. Commit transaction (projection updates here)
        await session.SaveChangesAsync();

        // 3. Query updated projection
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var metrics = await session.LoadAsync<AdminDailyMetrics>(today);

        // 4. Return SignalR event (Wolverine routes to SignalR hub)
        return new LiveMetricUpdated(
            metrics.TodaysOrders,
            metrics.TodaysRevenue,
            DateTimeOffset.UtcNow);
    }
}
```

---

## View Model Composition

BFF view models prioritize **frontend usability** over domain purity.

### Design Principles

**1. Flatten nested structures** — UI doesn't need deep hierarchies
```csharp
// ✅ BFF view model (flattened)
public record OrderView(
    Guid OrderId,
    string CustomerName,
    string CustomerEmail,
    string ShippingAddressLine, // Pre-formatted: "123 Main St, Seattle, WA 98101"
    IReadOnlyList<OrderLineItemView> Items);
```

**2. Pre-calculate values** — Don't make UI do math
```csharp
public sealed record CartLineItemView(
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);        // Pre-calculated: Quantity * UnitPrice
```

**3. Pre-format strings** — Ready for display
```csharp
public sealed record AddressSummary(
    Guid AddressId,
    string Nickname,           // "Home", "Work"
    string DisplayLine);       // "123 Main St, Seattle, WA 98101"
```

**4. Include UI state** — Flags for conditional rendering
```csharp
public sealed record CheckoutView(
    Guid CheckoutId,
    CheckoutStep CurrentStep,
    bool CanProceedToNextStep, // Server-side validation result
    bool IsPaymentRequired,
    IReadOnlyList<string> ValidationErrors);
```

### Streaming Projection Read Models

BFF endpoints that expose upstream projection read models as pass-through reads (no transformation, no composition) are prime candidates for `StreamMany<T>` and `StreamOne<T>` from `Marten.AspNetCore`. These types stream raw Marten JSON directly from Postgres to the HTTP response, eliminating the deserialize/serialize allocation that `Task<IReadOnlyList<T>>` endpoints incur. The same applies to endpoints in domain BCs that BFF clients call via `IHttpClientFactory` — streaming at the source removes one round-trip of JSON work for every BFF fan-out. Use the traditional return type when the endpoint shapes or composes data; use streaming when it's a direct read. See `marten-document-store.md §5.5` for the full reference.

---

## Cross-BC Query Orchestration

### Parallel Query Pattern

```csharp
[WolverineGet("/api/storefront/checkouts/{checkoutId}")]
public static async Task<CheckoutView> Handle(
    Guid checkoutId,
    IOrdersClient ordersClient,
    ICustomerIdentityClient identityClient,
    ICatalogClient catalogClient,
    CancellationToken ct)
{
    // 1. Query Orders BC for checkout state
    var checkout = await ordersClient.GetCheckoutAsync(checkoutId, ct);

    // 2. Query other BCs in parallel
    var (addresses, products) = await (
        identityClient.GetCustomerAddressesAsync(checkout.CustomerId, AddressType.Shipping, ct),
        catalogClient.GetProductsBySkusAsync(checkout.Items.Select(i => i.Sku).ToList(), ct)
    );

    // 3. Compose view model
    return new CheckoutView(
        checkout.CheckoutId,
        checkout.CustomerId,
        checkout.CurrentStep,
        EnrichLineItems(checkout.Items, products),
        addresses.Select(FormatAddress).ToList(),
        checkout.Subtotal,
        checkout.Total);
}
```

### Partial Failure Handling

```csharp
// Graceful degradation: show cart even if Inventory BC is down
var inventoryData = await TryGetInventoryDataAsync(ordersClient, ct);
if (inventoryData == null)
{
    return new CartView(
        cart.CartId,
        cart.Items,
        InventoryAvailabilityStatus: "Unavailable",
        ShowInventoryWarning: true);
}
```

---

## Real-Time Patterns: SignalR

**Why SignalR over SSE?** (From Cycle 16-18 evaluation)

| Feature | SignalR | SSE |
|---------|---------|-----|
| **Bidirectional** | ✅ Yes | ❌ Server → Client only |
| **Wolverine Integration** | ✅ Native transport | ❌ Manual adapter required |
| **Group Management** | ✅ Built-in | ❌ Manual tracking |
| **Reconnection** | ✅ Automatic | ⚠️ Manual with Last-Event-ID |
| **Binary Support** | ✅ Yes | ❌ Text only |

**Architectural Pattern:**

```
Domain BC → RabbitMQ → BFF Integration Handler → Marten Projection → SignalR Hub → Client
```

---

## SignalR Implementation Guide

### Server Configuration

```csharp
// Program.cs
builder.Services.AddSignalR();

builder.Host.UseWolverine(opts =>
{
    opts.UseSignalR();
    opts.Publish(x =>
    {
        x.MessagesImplementing<IStorefrontWebSocketMessage>();
        x.ToSignalR();
    });
});

app.MapWolverineSignalRHub<StorefrontHub>("/hub/storefront")
   .DisableAntiforgery(); // Required in ASP.NET Core 10+
```

### Marker Interfaces and Routing

```csharp
// Marker interface (domain project)
namespace Storefront.RealTime;
public interface IStorefrontWebSocketMessage { }

// Discriminated union for type-safe events
[JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
[JsonDerivedType(typeof(CartUpdated), typeDiscriminator: "cart-updated")]
[JsonDerivedType(typeof(OrderPlaced), typeDiscriminator: "order-placed")]
public abstract record StorefrontEvent(DateTimeOffset OccurredAt) : IStorefrontWebSocketMessage;

public sealed record CartUpdated(
    Guid CartId,
    int ItemCount,
    decimal Total,
    DateTimeOffset OccurredAt) : StorefrontEvent(OccurredAt);
```

### Hub Design and Group Management

```csharp
public sealed class StorefrontHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var customerId = Context.User?.FindFirst("CustomerId")?.Value;
        if (!string.IsNullOrEmpty(customerId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"customer:{customerId}");
        }
        await base.OnConnectedAsync();
    }
}
```

**Group Naming Conventions:**
- `customer:{customerId}` — Customer-specific notifications
- `vendor:{vendorId}` — Vendor-specific notifications
- `admin:{adminId}` — Admin-specific notifications

### Authentication Patterns

**Blazor Server (Storefront):**
```csharp
// Session cookie authentication (automatic)
app.MapWolverineSignalRHub<StorefrontHub>("/hub/storefront")
   .DisableAntiforgery();
```

**Blazor WASM (Vendor Portal):**
```csharp
// JWT Bearer authentication
builder.Services.AddAuthentication("VendorBearer")
    .AddJwtBearer("VendorBearer", opts => { /* JWT config */ });

// SignalR client with AccessTokenProvider
var hubConnection = new HubConnectionBuilder()
    .WithUrl("https://api/hub/vendor", options =>
    {
        options.AccessTokenProvider = async () =>
        {
            var authState = await authStateProvider.GetAuthenticationStateAsync();
            return authState.FindFirst("access_token")?.Value;
        };
    })
    .Build();
```

---

## Domain Event Aggregation

**Pattern:** BFF subscribes to integration messages from domain BCs, appends to BFF's event store, inline projections aggregate into queryable views.

**Example: Backoffice BFF aggregates events from 7 domain BCs:**

```csharp
// Backoffice/Backoffice/Notifications/OrderPlacedHandler.cs
public static class OrderPlacedHandler
{
    public static async Task<LiveMetricUpdated> Handle(
        Orders.OrderPlaced message,
        IDocumentSession session)
    {
        // Append to BFF event store
        session.Events.Append(Guid.NewGuid(), message);

        // Commit (inline projection updates)
        await session.SaveChangesAsync();

        // Query updated projection
        var metrics = await session.LoadAsync<AdminDailyMetrics>(today);

        // Return SignalR event
        return new LiveMetricUpdated(
            metrics.TodaysOrders,
            metrics.TodaysRevenue,
            DateTimeOffset.UtcNow);
    }
}
```

**Integration Message → SignalR Event Flow:**

1. Domain BC publishes integration message to RabbitMQ
2. BFF handler receives message (Wolverine inbox)
3. Handler appends event to BFF event store
4. Inline projection updates (same transaction)
5. Handler queries projection for fresh data
6. Handler returns SignalR event (Wolverine routes to hub)
7. Hub broadcasts to clients via groups

---

## Client-Side Integration

### JavaScript SignalR Client

```javascript
// wwwroot/js/signalr-client.js
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hub/storefront")
    .withAutomaticReconnect()
    .build();

connection.on("CartUpdated", (event) => {
    console.log("Cart updated:", event);
    updateCartUI(event.cartId, event.itemCount, event.total);
});

await connection.start();
```

### Blazor Component Integration

```razor
@inject IHubContext<StorefrontHub> HubContext

@code {
    private HubConnection? _hubConnection;

    protected override async Task OnInitializedAsync()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(Navigation.ToAbsoluteUri("/hub/storefront"))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<CartUpdated>("CartUpdated", HandleCartUpdated);

        await _hubConnection.StartAsync();
    }

    private void HandleCartUpdated(CartUpdated evt)
    {
        // Update UI state
        InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
```

---

## Testing BFF Components

### Integration Test with Stub Clients

```csharp
public class StorefrontTestFixture : IAsyncLifetime
{
    public IAlbaHost Host { get; private set; } = null!;
    public StubOrdersClient OrdersClient { get; } = new();

    public async Task InitializeAsync()
    {
        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IOrdersClient>(_ => OrdersClient);
            });
        });
    }
}

[Fact]
public async Task GetCheckoutView_ReturnsComposedView()
{
    // Arrange
    _fixture.OrdersClient.AddCheckout(new CheckoutDto(...));

    // Act
    var result = await _fixture.Host.GetAsJson<CheckoutView>(
        $"/api/storefront/checkouts/{checkoutId}");

    // Assert
    result.CheckoutId.ShouldBe(checkoutId);
    result.Items.Count.ShouldBe(2);
}
```

### SignalR Client Transport Testing

```csharp
// Use Wolverine SignalR Client transport for integration tests
[Fact]
public async Task OrderPlacedHandler_BroadcastsToCustomer()
{
    var customerId = Guid.NewGuid();
    var client = await _fixture.ConnectSignalRClientAsync(
        groupName: $"customer:{customerId}");

    // Trigger integration message
    await _fixture.PublishAsync(new OrderPlaced(
        orderId: Guid.NewGuid(),
        customerId: customerId,
        totalAmount: 99.99m));

    // Assert SignalR event received
    var evt = await client.ReceiveAsync<OrderPlaced>(TimeSpan.FromSeconds(5));
    evt.CustomerId.ShouldBe(customerId);
}
```

---

## Lessons Learned

### Lesson 1: BFF-Owned Projections Reduce Infrastructure Overhead

**From:** Backoffice BC (M32.0)

**Decision:** Own lightweight projections instead of creating separate Analytics BC.

**Benefits:**
- ✅ No extra service to deploy/monitor
- ✅ Zero latency (inline projection updates)
- ✅ Simpler testing (BFF tests cover projection + SignalR)

**When to reconsider:** If projections become complex (ML models, 20+ event types).

### Lesson 2: Wolverine SignalR Transport Simplifies Integration

**From:** Storefront BC (Cycle 16-18)

**Reality:** Hand-rolled SignalR broadcasters were fragile and verbose.

**Decision:** Use Wolverine SignalR transport — marker interface + `.ToSignalR()` configuration.

**Impact:** 80% reduction in SignalR plumbing code.

### Lesson 3: Inline Projections Are Essential for Real-Time Updates

**From:** Backoffice BC (M32.0)

**Problem:** Async projections caused 1-5 second lag in dashboard metrics.

**Decision:** Inline projections for all BFF-owned projections powering real-time updates.

**Takeaway:** Real-time UIs require zero-lag projections. Don't compromise.

### Lesson 4: Stub All Domain BC Clients in BFF Tests

**From:** Storefront BC (Cycle 17-18)

**Problem:** Integration tests were flaky due to external BC dependencies.

**Solution:** Stub `IOrdersClient`, `ICatalogClient`, etc. with in-memory implementations.

**Pattern:** `TestFixture` owns stub clients, registers via DI overrides.

### Lesson 5: DisableAntiforgery Required for SignalR in ASP.NET Core 10+

**From:** Storefront BC (Cycle 19)

**Problem:** SignalR connections failed with 400 errors after ASP.NET Core 10 upgrade.

**Root Cause:** ASP.NET Core 10+ enables antiforgery validation by default for all endpoints, including SignalR.

**Fix:** Call `.DisableAntiforgery()` on SignalR hub endpoint registration.

---

## Key Principles Summary

1. **BFF = Composition Only** — No business logic, validation, or calculations
2. **Delegate to Domain BCs** — BFF forwards commands; domain BCs execute
3. **View Models for UI** — Flatten, pre-calculate, pre-format
4. **Inline Projections for Real-Time** — Zero-lag updates for hot paths
5. **BFF-Owned Projections for Simple Aggregates** — Defer separate Analytics BC until complexity demands it
6. **Wolverine SignalR Transport** — Use marker interfaces + `.ToSignalR()` for automatic routing
7. **Stub Domain Clients in Tests** — Isolate BFF tests from external dependencies
8. **Group Management for Routing** — `customer:{id}`, `vendor:{id}`, `admin:{id}`

---

**Next steps:**
- For SignalR implementation details, read `wolverine-signalr.md`
- For projection patterns, read `event-sourcing-projections.md`
- For Blazor WASM + JWT patterns, read `blazor-wasm-jwt.md`
- For testing patterns, read `critterstack-testing-patterns.md`
