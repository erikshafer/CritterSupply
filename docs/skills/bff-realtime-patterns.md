# Backend-for-Frontend (BFF) & Real-Time Patterns

**Purpose:** Comprehensive guide for building customer-facing frontends in CritterSupply that aggregate data from multiple bounded contexts (BCs) with real-time updates using the Wolverine SignalR transport.

**Audience:** Human developers and AI agents implementing or extending BFF layers in CritterSupply.

**Status:** Reflects Cycles 16–22 implementation (as of 2026-03-13). SignalR is the current standard; SSE is historical reference only.

---

## Table of Contents

- [Foundational Concept: What is a BFF?](#foundational-concept-what-is-a-bff)
- [When to Use a BFF](#when-to-use-a-bff)
- [Project Structure (CritterSupply Pattern)](#project-structure-crittersupply-pattern)
- [BFF Responsibilities and Boundaries](#bff-responsibilities-and-boundaries)
  - [What Belongs in the BFF](#what-belongs-in-the-bff)
  - [What Does NOT Belong in the BFF](#what-does-not-belong-in-the-bff)
  - [BFF-Owned Projections vs Separate Analytics BC](#bff-owned-projections-vs-separate-analytics-bc-m320-decision) ⭐ *M32 Addition*
- [View Model Composition](#view-model-composition)
- [Cross-BC Query Orchestration](#cross-bc-query-orchestration)
- [Real-Time Patterns: SignalR (Current Standard)](#real-time-patterns-signalr-current-standard)
- [SignalR Implementation Guide](#signalr-implementation-guide)
  - [Server Configuration](#server-configuration)
  - [Marker Interfaces and Routing](#marker-interfaces-and-routing)
  - [Hub Design and Group Management](#hub-design-and-group-management)
  - [Authentication Patterns](#authentication-patterns)
- [Domain Event Aggregation](#domain-event-aggregation)
- [Marten Projection Side Effects Pipeline](#marten-projection-side-effects-pipeline)
- [Client-Side Integration](#client-side-integration)
  - [JavaScript SignalR Client](#javascript-signalr-client)
  - [Blazor Component Integration](#blazor-component-integration)
- [Testing BFF Components](#testing-bff-components)
- [Lessons Learned](#lessons-learned)
- [Scaling Considerations](#scaling-considerations)
- [Key Principles Summary](#key-principles-summary)
- [References](#references)
- [Appendix: SSE (Historical Reference)](#appendix-sse-historical-reference)

---

## Foundational Concept: What is a BFF?

A **Backend-for-Frontend (BFF)** is an intermediate layer between the frontend and domain BCs that:

- **Composes** data from multiple BCs into frontend-optimized view models
- **Orchestrates** queries and commands across BCs for UI workflows
- **Aggregates** domain events into real-time notifications for the client
- **Does NOT contain domain logic** — it delegates to domain BCs

**Critical boundary:** The BFF is a **composition and translation layer**, not a domain layer. Any business rules, validation, or state transitions belong in domain BCs. The BFF only assembles data and routes commands.

**Example violation:**
```csharp
// ❌ BAD — BFF calculating cart totals (domain logic)
public static async Task<CheckoutView> Handle(...)
{
    var total = items.Sum(i => i.Quantity * i.UnitPrice);
    var tax = total * 0.08m; // Tax calculation = domain logic
    return new CheckoutView(..., Total: total + tax);
}
```

**Correct delegation:**
```csharp
// ✅ GOOD — BFF queries Orders BC for pre-calculated totals
public static async Task<CheckoutView> Handle(
    Guid checkoutId, IOrdersClient ordersClient, ...)
{
    var checkout = await ordersClient.GetCheckoutAsync(checkoutId);
    return new CheckoutView(..., Total: checkout.Total); // Orders BC owns calculation
}
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
src/
  <BC Name>/
    <ProjectName>/                      # Domain project (regular SDK)
      <ProjectName>.csproj               # References: Messages.Contracts only
      Clients/                           # HTTP client interfaces (domain)
        I*Client.cs
      Composition/                       # View models for UI
        *View.cs
      Notifications/                     # Integration message handlers
        I*Message.cs                     # Marker interfaces for SignalR routing
        *Handler.cs                      # Integration message → SignalR event handlers

    <ProjectName>.Api/                  # API project (Web SDK)
      <ProjectName>.Api.csproj           # References: <ProjectName>, Messages.Contracts
      Program.cs                         # Wolverine + Marten + SignalR + DI setup
      appsettings.json                   # Connection strings, external API base URLs
      Properties/launchSettings.json     # Port allocation
      Queries/                           # HTTP endpoints (composition)
        Get*View.cs                      # namespace: <ProjectName>.Api.Queries
      Clients/                           # HTTP client implementations
        *Client.cs                       # namespace: <ProjectName>.Api.Clients
      *Hub.cs                            # SignalR hub (namespace: <ProjectName>.Api)

    <ProjectName>.Web/                  # Blazor frontend (Web SDK for Server, WASM SDK for WASM)
      <ProjectName>.Web.csproj
      Program.cs                         # MudBlazor + HttpClient + SignalR config
      Components/
        Layout/MainLayout.razor
        Pages/*.razor
      wwwroot/
        js/signalr-client.js             # (Optional) JavaScript SignalR wrapper
```

**Real Example: Storefront BC (Customer Experience)**

```
src/Customer Experience/
├── Storefront/                         # Domain project
│   ├── Clients/                        # IShoppingClient, IOrdersClient, ICatalogClient
│   ├── Composition/                    # CartView, CheckoutView, ProductListingView
│   └── Notifications/                  # IStorefrontWebSocketMessage, ItemAddedHandler, OrderPlacedHandler
│
├── Storefront.Api/                     # API project
│   ├── Program.cs                      # Wolverine handler discovery for both assemblies
│   ├── Queries/                        # GetCartView, GetCheckoutView, GetProductListing
│   ├── Clients/                        # ShoppingClient, OrdersClient, CatalogClient
│   └── StorefrontHub.cs                # SignalR hub at /hub/storefront
│
└── Storefront.Web/                     # Blazor Server UI
    ├── Components/Pages/               # Cart.razor, Checkout.razor, OrderHistory.razor
    └── wwwroot/js/signalr-client.js    # SignalR connection wrapper
```

**Why 3 projects?**
1. **Domain project** (`Storefront/`) — Composition logic, interfaces, integration handlers (no HTTP, no SignalR hub)
2. **API project** (`Storefront.Api/`) — HTTP endpoints, infrastructure, HTTP clients, SignalR hub
3. **Web project** (`Storefront.Web/`) — Frontend, MudBlazor components, JavaScript interop

**Key Configuration (Program.cs):**

```csharp
// Storefront.Api/Program.cs
builder.Host.UseWolverine(opts =>
{
    // Discover handlers in BOTH API and Domain assemblies
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);                        // API
    opts.Discovery.IncludeAssembly(typeof(Storefront.Notifications.IStorefrontWebSocketMessage).Assembly); // Domain

    opts.UseSignalR();
    opts.Publish(x =>
    {
        x.MessagesImplementing<IStorefrontWebSocketMessage>();
        x.ToSignalR();
    });
});

app.MapWolverineSignalRHub<StorefrontHub>("/hub/storefront")
   .DisableAntiforgery(); // Required in ASP.NET Core 10+ (see Authentication Patterns section)
```

**Common Mistakes to Avoid:**
- ❌ Single Web SDK project combining domain + infrastructure (violates separation of concerns)
- ❌ Domain project referencing Wolverine packages (not needed — handlers discovered via API assembly reference)
- ❌ Forgetting to include domain assembly in `opts.Discovery.IncludeAssembly()`

**References:**
- CLAUDE.md → "BFF Project Structure Pattern" section
- docs/skills/vertical-slice-organization.md → File organization conventions

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
- Handle partial failures gracefully (e.g., if Inventory BC is down, still show cart)
- Cache frequently-accessed reference data (product images, categories)

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

**❌ Validation**
```csharp
// ❌ BAD — validation belongs in domain BC
if (string.IsNullOrEmpty(command.PaymentToken))
    return Results.BadRequest("Payment token required");
```

**❌ State Transitions**
```csharp
// ❌ BAD — order state machine belongs in Orders BC
if (order.Status == "Pending")
    order.Status = "Processing";
```

**❌ Calculations**
```csharp
// ❌ BAD — pricing calculations belong in domain BC
var discount = order.Subtotal * (coupon.Percentage / 100m);
```

**✅ Correct Delegation Pattern:**
```csharp
// ✅ GOOD — forward command to domain BC; let it validate and execute
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

**Testing the Boundary:**
If you can write a unit test for the BFF logic without mocking domain BCs, **you've likely leaked domain logic into the BFF**. BFF handlers should be so thin that only integration tests make sense.

### BFF-Owned Projections vs Separate Analytics BC (M32.0 Decision)

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
│  └─ NO (complex rules, ML models, multi-stage pipelines) → ❌ Separate BC required
│
├─ Q3: Does the projection require <10 domain events to build?
│  ├─ YES → ✅ BFF-owned projection
│  └─ NO (needs 20+ event types) → ⚠️  Consider separate BC for maintainability
```

**Examples:**

| Use Case | Decision | Rationale |
|----------|----------|-----------|
| **Admin dashboard metrics** (M32.0: `AdminDailyMetrics`) | ✅ BFF-owned | Only Backoffice needs it; simple count/sum; 5 events |
| **Customer order history** (Storefront: `CustomerOrdersView`) | ✅ BFF-owned | Only Storefront needs it; simple list; 3 events |
| **ML-based product recommendations** | ❌ Separate BC | Complex ML pipeline; multiple consumers (Storefront, Email campaigns) |
| **Multi-tenant analytics dashboard** | ❌ Separate BC | Multiple tenants; complex aggregations; 30+ event types |
| **Live inventory dashboard** (Backoffice) | ✅ BFF-owned | Only Backoffice needs it; simple stock count; 4 events |

**BFF-Owned Projection Pattern (Backoffice Example):**

```csharp
// Backoffice/Backoffice/Metrics/AdminDailyMetrics.cs
public sealed record AdminDailyMetrics
{
    public DateOnly Date { get; init; }
    public int TodaysOrders { get; set; }
    public decimal TodaysRevenue { get; set; }
    public int TodaysReturns { get; set; }
    public int ActiveCustomers { get; set; }

    public static AdminDailyMetrics Create(DateOnly date) => new()
    {
        Date = date,
        TodaysOrders = 0,
        TodaysRevenue = 0m,
        TodaysReturns = 0,
        ActiveCustomers = 0
    };
}

// Projection: consumes integration messages from Orders, Returns, Customer Identity
public sealed class AdminDailyMetricsProjection : SingleStreamProjection<AdminDailyMetrics>
{
    public static AdminDailyMetrics Create(Messages.Contracts.Orders.OrderPlaced @event)
    {
        var date = DateOnly.FromDateTime(@event.PlacedAt.Date);
        var metrics = AdminDailyMetrics.Create(date);
        metrics.TodaysOrders = 1;
        metrics.TodaysRevenue = @event.TotalAmount;
        return metrics;
    }

    public void Apply(Messages.Contracts.Orders.OrderPlaced @event, AdminDailyMetrics metrics)
    {
        metrics.TodaysOrders++;
        metrics.TodaysRevenue += @event.TotalAmount;
    }

    public void Apply(Messages.Contracts.Returns.ReturnApproved @event, AdminDailyMetrics metrics)
    {
        metrics.TodaysReturns++;
    }

    // ... other events
}

// Register in Program.cs
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.Projections.Add<AdminDailyMetricsProjection>(ProjectionLifecycle.Inline);
});
```

**Integration Message Handler → Projection Query → SignalR (M32.0 Pattern):**

```csharp
// Backoffice/Backoffice/Notifications/OrderPlacedHandler.cs
public static class OrderPlacedHandler
{
    // Handler consumes integration message, queries updated projection, returns SignalR event
    public static async Task<LiveMetricUpdated> Handle(
        Messages.Contracts.Orders.OrderPlaced message,
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

**When NOT to Own Projections in BFF:**

1. **Multiple Consumers** — If 2+ BCs need the same projection → separate BC
2. **Complex Logic** — If projection requires ML models, multi-stage pipelines, or business rules → separate BC
3. **Large Event Surface** — If projection consumes 20+ event types → separate BC (maintainability)
4. **Different Scaling Needs** — If projection needs different compute/storage scaling than BFF → separate BC
5. **Regulatory/Compliance** — If projection data has different retention/audit requirements → separate BC

**Benefits of BFF-Owned Projections (M32.0 Wins):**

- ✅ **No Extra Service** — One less BC to deploy, monitor, and maintain
- ✅ **Inline Updates** — Zero latency between event and projection update
- ✅ **Simpler Testing** — BFF integration tests cover projection + SignalR pipeline
- ✅ **Reduced Complexity** — No cross-BC HTTP calls for simple aggregates

**Cross-Reference:** See `docs/skills/event-sourcing-projections.md` → "Lesson 6: BFF-Owned Projections vs Analytics BC" for full projection implementation patterns.

---

## View Model Composition

BFF view models prioritize **frontend usability** over domain purity.

### Design Principles

**1. Flatten nested structures** — UI doesn't need deep hierarchies
```csharp
// ❌ Domain model (deeply nested)
public record Order(Guid Id, Customer Customer, Address ShippingAddress, List<LineItem> Items);
public record Customer(Guid Id, string Name, ContactInfo Contact);
public record ContactInfo(string Email, PhoneNumber Phone);

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

### Example: CheckoutView Composition

```csharp
// Storefront.Api/Queries/GetCheckoutView.cs
public static class GetCheckoutViewHandler
{
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

        // 2. Query Customer Identity BC for saved addresses
        var addresses = await identityClient.GetCustomerAddressesAsync(
            checkout.CustomerId,
            AddressType.Shipping,
            ct);

        // 3. Query Product Catalog BC for product details
        var products = await catalogClient.GetProductsBySkusAsync(
            checkout.Items.Select(i => i.Sku).ToList(),
            ct);

        // 4. Compose view model optimized for frontend
        return new CheckoutView(
            checkout.CheckoutId,
            checkout.CustomerId,
            checkout.CurrentStep,
            EnrichLineItems(checkout.Items, products),  // Join data
            addresses.Select(a => new AddressSummary(
                a.AddressId,
                a.Nickname,
                FormatAddress(a))).ToList(), // Pre-format
            checkout.Subtotal,
            checkout.ShippingCost,
            checkout.Total,
            checkout.CanProceedToNextStep);
    }

    private static IReadOnlyList<CartLineItemView> EnrichLineItems(
        IReadOnlyList<CheckoutItemDto> items,
        IReadOnlyList<ProductDto> products)
    {
        return items.Select(item =>
        {
            var product = products.FirstOrDefault(p => p.Sku == item.Sku);
            return new CartLineItemView(
                item.Sku,
                product?.Name ?? "Unknown Product",
                product?.ImageUrl ?? "/placeholder.png",
                item.Quantity,
                item.UnitPrice,
                item.Quantity * item.UnitPrice); // Pre-calculate line total
        }).ToList();
    }

    private static string FormatAddress(AddressDto address)
        => $"{address.Street}, {address.City}, {address.State} {address.ZipCode}";
}
```

**Key Observations:**
- No business logic — just data fetching and transformation
- Parallel queries possible (not shown here for clarity)
- Null handling for missing products (graceful degradation)
- Pre-formatted address display lines

---

## Cross-BC Query Orchestration

### Parallel Queries for Performance

When composing from multiple BCs, execute queries in parallel:

```csharp
// ❌ Sequential queries — slow
var cart = await shoppingClient.GetCartAsync(cartId);
var products = await catalogClient.GetProductsBySkusAsync(cart.Items.Select(i => i.Sku).ToList());
var customer = await identityClient.GetCustomerAsync(cart.CustomerId);

// ✅ Parallel queries — fast
var (cart, customer) = await (
    shoppingClient.GetCartAsync(cartId),
    identityClient.GetCustomerAsync(customerId)
);
var products = await catalogClient.GetProductsBySkusAsync(cart.Items.Select(i => i.Sku).ToList());
```

### Graceful Degradation

Handle partial BC failures without blocking the entire view:

```csharp
// Storefront.Api/Queries/GetCartView.cs
public static async Task<CartView> Handle(...)
{
    var cart = await shoppingClient.GetCartAsync(cartId);

    // Attempt to enrich with product details, but don't fail if Catalog BC is down
    IReadOnlyList<ProductDto> products;
    try
    {
        products = await catalogClient.GetProductsBySkusAsync(
            cart.Items.Select(i => i.Sku).ToList(), ct);
    }
    catch (HttpRequestException ex)
    {
        logger.LogWarning(ex, "Product Catalog BC unavailable; using SKU-only cart items");
        products = Array.Empty<ProductDto>(); // Fallback to empty list
    }

    return new CartView(
        cart.CartId,
        cart.CustomerId,
        EnrichLineItems(cart.Items, products), // Will use "Unknown Product" for missing products
        cart.Subtotal);
}
```

### HTTP Client Configuration

```csharp
// Storefront.Api/Program.cs
builder.Services.AddHttpClient<IShoppingClient, ShoppingClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiClients:ShoppingApiUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(10); // Fail fast
});

// appsettings.json
{
  "ApiClients": {
    "ShoppingApiUrl": "http://localhost:5236",
    "OrdersApiUrl": "http://localhost:5231",
    "CatalogApiUrl": "http://localhost:5133"
  }
}
```

---

## Real-Time Patterns: SignalR (Current Standard)

### Historical Context: SSE → SignalR Migration

**Original Decision (Cycle 16, ADR 0004):** Use Server-Sent Events (SSE) for one-way server→client push.

**Why SSE was chosen:**
- Native .NET 10 support via `IAsyncEnumerable<T>`
- Simpler protocol (HTTP-based, no client library needed)
- Sufficient for cart updates, order status notifications

**Why we migrated to SignalR (Cycle 22, ADR 0013):**
1. **Bidirectional requirements emerged** — Storefront checkout needs client→server validation (coupon checks, shipping options), Vendor Portal needs vendor submissions + live approvals
2. **Wolverine 5.x ships native SignalR transport** — Replaces hand-rolled `EventBroadcaster` abstraction with framework-level routing
3. **Marten projection side effects → SignalR pipeline** — Projects can publish messages that Wolverine routes directly to hub groups
4. **CritterStack alignment** — JasperFx's own CritterWatch uses SignalR; CritterSupply should demonstrate idiomatic patterns

**Cost:** ~1 full cycle to migrate Storefront from SSE to SignalR (Cycle 22 Phase 1)

**Outcome:** SignalR is now the standard for all BFF real-time requirements. SSE patterns remain in this doc as historical reference only (see Appendix).

**When to use SignalR from day one:**
- Any BFF with foreseeable bidirectional requirements
- Multi-tenant portals requiring group-scoped delivery
- Vendor-facing UIs with live analytics or approval workflows

**Lessons learned:** Deferring SignalR incurs migration cost later. Start with SignalR unless requirements are strictly unidirectional **and** will never evolve.

**References:**
- [ADR 0004: SSE over SignalR (Superseded)](../decisions/0004-sse-over-signalr.md)
- [ADR 0013: Migrate from SSE to SignalR (Accepted)](../decisions/0013-signalr-migration-from-sse.md)

---

## SignalR Implementation Guide

### Server Configuration

**Package Reference:**
```bash
dotnet add package WolverineFx.SignalR
```

**Program.cs Setup:**
```csharp
// Storefront.Api/Program.cs
builder.Host.UseWolverine(opts =>
{
    // 1. Enable Wolverine's SignalR transport (also calls AddSignalR internally)
    opts.UseSignalR();

    // 2. Publish rule: route all IStorefrontWebSocketMessage to SignalR hub
    opts.Publish(x =>
    {
        x.MessagesImplementing<IStorefrontWebSocketMessage>();
        x.ToSignalR();
    });

    // ... RabbitMQ, handler discovery, etc.
});

var app = builder.Build();

// 3. Map the hub route
app.MapWolverineSignalRHub<StorefrontHub>("/hub/storefront")
   .DisableAntiforgery(); // Required in ASP.NET Core 10+ (see Authentication Patterns)
```

**Anti-Forgery Configuration (ASP.NET Core 10+):**

ASP.NET Core 10 enables anti-forgery protection on SignalR hub endpoints by default. Whether to disable it depends on authentication:

- **JWT-authenticated hubs** (e.g., Vendor Portal): `.DisableAntiforgery()` is safe—JWT tokens aren't sent automatically by browsers, so no CSRF risk
- **Session-cookie hubs** (e.g., Storefront): `.DisableAntiforgery()` required for cross-origin dev setups, but pair with strict CORS policy (not `AllowAnyOrigin`)

**See also:** docs/skills/wolverine-signalr.md → "Anti-Forgery on Hub Routes" section for full security analysis.

---

### Marker Interfaces and Routing

**Marker interfaces live in the domain project** (e.g., `Storefront/`, not `Storefront.Api/`):

```csharp
// Storefront/Notifications/IStorefrontWebSocketMessage.cs
namespace Storefront.Notifications;

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

**Message Type Definitions:**

```csharp
// Storefront/Notifications/StorefrontEvent.cs
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

public sealed record ShipmentStatusChanged(
    Guid ShipmentId,
    Guid CustomerId,
    string TrackingNumber,
    string NewStatus,
    DateTimeOffset OccurredAt) : IStorefrontWebSocketMessage;
```

**Notification Handlers — Pure Function Return Pattern:**

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

**Flow:**
1. Orders BC publishes `OrderPlaced` to RabbitMQ
2. Storefront BFF receives message via Wolverine subscription
3. Handler returns `OrderStatusChanged` (implements `IStorefrontWebSocketMessage`)
4. Wolverine sees marker interface, applies publish rule `.ToSignalR()`
5. SignalR hub broadcasts to `customer:{customerId}` group (via group management in `OnConnectedAsync`)
6. JavaScript client receives CloudEvents envelope via `connection.on("ReceiveMessage", ...)`
7. Blazor UI updates order status in real-time

---

### Hub Design and Group Management

**Hub with Group Enrollment:**

```csharp
// Storefront.Api/StorefrontHub.cs
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

**Group Naming Convention:**

| Scope | Pattern | When to Use |
|-------|---------|-------------|
| Single-tenant user | `customer:{customerId}` | Storefront — one customer per group |
| Tenant-wide | `vendor:{tenantId}` | Vendor Portal — all users in a tenant |
| Per-user | `user:{userId}` | Vendor Portal — individual notifications |

**Multi-Tenant Hub Example (Vendor Portal):**

```csharp
// VendorPortal.Api/VendorPortalHub.cs
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class VendorPortalHub : Hub
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

**Group-Targeted Publishing:**

For messages that should only go to a specific group (not all connected clients):

```csharp
public static SignalRMessage<CartUpdated> Handle(ItemAdded message)
{
    var cartUpdated = new CartUpdated(message.CartId, message.CustomerId, ...);
    // .ToWebSocketGroup() sends ONLY to the named group
    return cartUpdated.ToWebSocketGroup($"customer:{message.CustomerId}");
}
```

**⚠️ Security Warning:** `.ToSignalR()` in publish rules broadcasts to **ALL** connected clients. For customer-scoped or tenant-scoped messages, always use `.ToWebSocketGroup($"group-name")` in handlers.

**See also:** docs/research/storefront-ux-session/WOLVERINE-SIGNALR-OBSERVATIONS.md → "Observation 1" for security bug discovered in Cycle 19.

---

### Authentication Patterns

CritterSupply uses two different authentication mechanisms for SignalR hubs, deliberately demonstrating both approaches.

#### Pattern A: Session Cookies (Storefront BC)

The Storefront uses session-cookie auth. The hub does **not** use `[Authorize]`. The `customerId` is passed as a query string parameter on the WebSocket upgrade request and used to enroll the connection in the correct group.

**Why this is acceptable:**
- The Blazor app sets `customerId` from the server-side session (a claim the user cannot forge)
- Session-backed identity provides the actual trust; the GUID is an identifier, not a secret

```csharp
// Storefront.Api/StorefrontHub.cs
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

**⚠️ Security Note:** Passing `customerId` in the query string is only acceptable when the **source** of that value is server-side authenticated identity (e.g., a claim extracted from the session cookie before the Blazor page renders). GUIDs are identifiers, not secrets—do not rely on "hard to guess" as a security property. For vendor-facing contexts where the `VendorTenantId` must be cryptographically verified, always derive group keys from JWT claims server-side (Pattern B).

#### Pattern B: JWT Bearer (Vendor Portal BC)

The Vendor Portal uses JWT auth ([ADR 0028](../decisions/0028-jwt-for-vendor-identity.md)). WebSocket upgrade requests cannot carry an `Authorization` header in all browsers, so the JWT is extracted from the query string (`?access_token=...`) via `JwtBearerEvents.OnMessageReceived`:

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
public sealed class VendorPortalHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // VendorTenantId comes ONLY from cryptographically-verified JWT claims.
        // NEVER from query string, NEVER from request body.
        var tenantId = Context.User!.FindFirst("VendorTenantId")?.Value;
        var userId = Context.User!.FindFirst("VendorUserId")?.Value;
        // ... group enrollment
    }
}
```

**See also:** docs/skills/wolverine-signalr.md → "Authentication Patterns" section for complete implementation details, including Blazor WASM `accessTokenFactory` configuration.

---

## Domain Event Aggregation

BFF notification handlers translate integration messages from domain BCs into client-friendly SignalR events.

**Pattern:**
```
Domain BC Event → Integration Message (RabbitMQ) → BFF Handler → SignalR Event → Client
```

**Example Flow (Cart Update):**

```
[Shopping BC]
AddItemToCart (command)
  └─> AddItemToCartHandler
      ├─> ItemAdded (domain event, persisted via Marten)
      └─> Publish Shopping.ItemAdded (integration message) → RabbitMQ

[Storefront BFF]
Shopping.ItemAdded (integration message from RabbitMQ)
  └─> ItemAddedHandler
      ├─> (optionally query Shopping BC for enriched cart state)
      └─> Return CartUpdated (implements IStorefrontWebSocketMessage)
          └─> Wolverine routing rule .ToSignalR()
              └─> SignalR hub → CloudEvents envelope → JavaScript client

[Blazor Frontend]
connection.on("ReceiveMessage", (cloudEvent) => {
    // Unwrap CloudEvents envelope, extract data.eventType
    if (eventType === "cart-updated") {
        await dotNetHelper.invokeMethodAsync("OnCartUpdated", cloudEvent.data);
    }
});
```

**Handler Implementation:**

```csharp
// Storefront/Notifications/ItemAddedHandler.cs
public static class ItemAddedHandler
{
    public static async Task<CartUpdated> Handle(
        Messages.Contracts.Shopping.ItemAdded message,
        IShoppingClient shoppingClient,
        CancellationToken ct)
    {
        // Query Shopping BC for updated cart state (enrichment step)
        var cart = await shoppingClient.GetCartAsync(message.CartId, ct);

        if (cart is null)
            return null!; // Or throw; depends on error handling strategy

        // Create SignalR event
        return new CartUpdated(
            cart.CartId,
            cart.CustomerId,
            cart.Items.Count,
            cart.Subtotal,
            message.AddedAt);
    }
}
```

**RabbitMQ Queue Subscription:**

```csharp
// Storefront.Api/Program.cs
builder.Host.UseWolverine(opts =>
{
    opts.ListenToRabbitQueue("storefront-cart-events");
    opts.ListenToRabbitQueue("storefront-order-events");
    opts.ListenToRabbitQueue("storefront-shipment-events");
});
```

**Integration Message Contracts:**

Defined in `Messages.Contracts` project (shared across all BCs):

```csharp
// Messages.Contracts/Shopping/ShoppingIntegrationEvents.cs
public sealed record ItemAdded(
    Guid CartId,
    Guid CustomerId,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    DateTimeOffset AddedAt);

public sealed record ItemRemoved(
    Guid CartId,
    Guid CustomerId,
    string Sku,
    DateTimeOffset RemovedAt);
```

**See also:** CONTEXTS.md → "Customer Experience" section for complete integration contract tables.

---

## Marten Projection Side Effects Pipeline

One of the most powerful Wolverine + SignalR patterns: **Marten projection side effects publish messages that Wolverine routes directly to the SignalR hub**. Zero manual bridging code.

**Full reactive pipeline:**
```
Domain Event → Marten projection → side effect message → Wolverine → SignalR hub → Client
```

**Example: CartSummaryProjection**

```csharp
// Shopping/Projections/CartSummaryProjection.cs
public sealed class CartSummaryProjection : SingleStreamProjection<CartSummary>
{
    public void Apply(ItemAdded @event, CartSummary cart)
    {
        cart.Items.Add(new CartSummaryItem(@event.Sku, @event.Quantity, @event.UnitPrice));
        cart.TotalAmount = cart.Items.Sum(i => i.Quantity * i.UnitPrice);
        cart.LastUpdatedAt = @event.AddedAt;
    }

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

**Why this is powerful:**
- Projection maintains read model (CartSummary document)
- Side effect publishes notification message
- Wolverine routing delivers to SignalR hub
- **Zero manual wiring between projection and SignalR**

**Use cases:**
- **Cart projections** → live cart totals in the browser
- **Order saga state** → step-by-step order progress UI
- **Vendor analytics projections** → live dashboard updates as orders flow in

**Important:** `RaiseSideEffects` runs **after** projection state is committed, so the message reflects confirmed state.

**See also:** docs/skills/marten-event-sourcing.md → "Projection Side Effects" section.

---

## Client-Side Integration

### JavaScript SignalR Client

CritterSupply's Storefront uses a vanilla JavaScript wrapper (`signalr-client.js`) that abstracts connection lifecycle and CloudEvents unwrapping:

```javascript
// Storefront.Web/wwwroot/js/signalr-client.js
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
                // Safe because transport is already locked to WebSockets—no fallback needed.
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
                // Unwrap the CloudEvents envelope—data is the actual payload.
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
<!-- Pin the version—avoid unpinned CDN references in production -->
<script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js"></script>
<script src="js/signalr-client.js"></script>
```

---

### Blazor Component Integration

CritterSupply's Blazor pages use JS interop to drive the JavaScript client. The component exposes a `[JSInvokable]` callback that JavaScript calls when a CloudEvents message arrives:

```csharp
// Storefront.Web/Components/Pages/Cart.razor
@page "/cart"
@rendermode InteractiveServer
@inject IJSRuntime JS
@inject NavigationManager Navigation
@inject IConfiguration Configuration
@inject IHttpClientFactory HttpClientFactory
@implements IAsyncDisposable

<MudText Typo="Typo.h4">Shopping Cart</MudText>

@if (_cartView == null)
{
    <MudAlert Severity="Severity.Info">Your cart is empty.</MudAlert>
}
else
{
    @foreach (var item in _cartView.Items)
    {
        <MudCard Class="mb-3">
            <MudCardContent>
                <MudText Typo="Typo.h6">@item.ProductName</MudText>
                <MudText>Qty: @item.Quantity - @item.LineTotal.ToString("C")</MudText>
            </MudCardContent>
        </MudCard>
    }

    <MudText Typo="Typo.h6">Subtotal: @_cartView.Subtotal.ToString("C")</MudText>

    @if (_signalrConnected)
    {
        <MudChip T="string" Icon="@Icons.Material.Filled.Wifi" Color="Color.Success">
            Real-time updates active
        </MudChip>
    }
}

@code {
    private CartView? _cartView;
    private bool _signalrConnected = false;
    private DotNetObjectReference<Cart>? _dotNetHelper;
    private Guid? _customerId;
    private Guid? _cartId;

    protected override async Task OnInitializedAsync()
    {
        await LoadCart();
    }

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
            _signalrConnected = true;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to subscribe to SignalR: {ex.Message}");
        }
    }

    private async Task LoadCart()
    {
        var client = HttpClientFactory.CreateClient("StorefrontApi");
        _cartView = await client.GetFromJsonAsync<CartView>($"/api/storefront/carts/{_cartId}");
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

    private sealed record CartView(Guid CartId, Guid CustomerId, IReadOnlyList<CartLineItemView> Items, decimal Subtotal);
    private sealed record CartLineItemView(string Sku, string ProductName, string ProductImageUrl, int Quantity, decimal UnitPrice, decimal LineTotal);
}
```

**Key Patterns:**
- `@rendermode InteractiveServer` — Required for `OnInitializedAsync()` and `StateHasChanged()`
- `DotNetObjectReference<T>` — Allows JavaScript to call back to .NET
- `[JSInvokable]` — Marks method callable from JavaScript
- `IAsyncDisposable` — Clean up SignalR connection on navigation away

**⚠️ Important:** Always implement `IAsyncDisposable` on components that subscribe to SignalR. Failing to stop the connection on dispose causes memory leaks and ghost connections that continue receiving messages for disconnected sessions.

**See also:** docs/skills/blazor-wasm-jwt.md for JWT-authenticated Blazor WASM client patterns (Vendor Portal).

---

## Testing BFF Components

BFFs don't contain domain logic, so focus on **integration tests** over unit tests.

### Integration Test Fixture (Alba + TestContainers)

```csharp
// Storefront.IntegrationTests/StorefrontTestFixture.cs
public class StorefrontTestFixture : IAsyncLifetime
{
    private IAlbaHost? _host;
    public IAlbaHost Host => _host ?? throw new InvalidOperationException("Fixture not initialized");

    public async Task InitializeAsync()
    {
        _host = await AlbaHost.For<Program>(builder =>
        {
            // Use stub HTTP clients instead of real downstream APIs
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IShoppingClient, StubShoppingClient>();
                services.AddSingleton<IOrdersClient, StubOrdersClient>();
                services.AddSingleton<ICatalogClient, StubCatalogClient>();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }
}
```

### Composition Endpoint Test

```csharp
[Collection("Storefront")]
public class CheckoutViewCompositionTests
{
    private readonly StorefrontTestFixture _fixture;

    public CheckoutViewCompositionTests(StorefrontTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetCheckoutView_ComposesFromMultipleBCs()
    {
        // Arrange—seed data in stub clients
        var checkoutId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var stubOrdersClient = _fixture.Host.Services.GetRequiredService<IOrdersClient>() as StubOrdersClient;
        stubOrdersClient!.Checkouts[checkoutId] = new CheckoutDto(checkoutId, customerId, ...);

        var stubIdentityClient = _fixture.Host.Services.GetRequiredService<ICustomerIdentityClient>() as StubCustomerIdentityClient;
        stubIdentityClient!.Addresses[customerId] = new List<AddressDto> { new(...) };

        // Act—query BFF composition endpoint
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/storefront/checkouts/{checkoutId}");
            s.StatusCodeShouldBe(200);
        });

        var view = result.ReadAsJson<CheckoutView>();

        // Assert—view contains composed data from multiple BCs
        view.CheckoutId.ShouldBe(checkoutId);
        view.Items.ShouldNotBeEmpty();       // From Orders BC
        view.SavedAddresses.ShouldNotBeEmpty();   // From Customer Identity BC
    }
}
```

### SignalR Notification Test (Handler Only)

For SignalR message handlers, test the handler directly rather than via end-to-end SignalR transport (which requires real Kestrel):

```csharp
[Fact]
public async Task ItemAdded_ReturnsCartUpdatedEvent()
{
    // Arrange
    var cartId = Guid.NewGuid();
    var customerId = Guid.NewGuid();

    var message = new Messages.Contracts.Shopping.ItemAdded(
        cartId,
        customerId,
        "DOG-BOWL-01",
        2,
        19.99m,
        DateTimeOffset.Now);

    var stubClient = new StubShoppingClient();
    stubClient.Carts[cartId] = new CartDto(cartId, customerId, ...);

    // Act
    var result = await ItemAddedHandler.Handle(message, stubClient, CancellationToken.None);

    // Assert
    result.ShouldNotBeNull();
    result.CartId.ShouldBe(cartId);
    result.CustomerId.ShouldBe(customerId);
    result.ItemCount.ShouldBe(1);
}
```

### SignalR Integration Test with Wolverine Tracking

For end-to-end SignalR delivery tests using Wolverine's SignalR Client transport:

```csharp
[Fact]
public async Task OrderPlaced_SendsSignalRMessageToHub()
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

**Important:** SignalR Client transport requires **real Kestrel** (not `WebApplicationFactory`). See docs/skills/wolverine-signalr.md → "SignalR Client Transport" section for complete fixture setup.

### What to Test

- ✅ View composition from multiple BCs
- ✅ Integration message handlers return correct SignalR messages
- ✅ Handlers query downstream BCs correctly
- ✅ HTTP 404/500 error handling
- ❌ **NOT:** Domain logic (that's in domain BCs)
- ❌ **NOT:** UI rendering (use Playwright E2E tests for full browser validation)

**See also:**
- docs/skills/critterstack-testing-patterns.md → Alba integration test patterns
- docs/skills/e2e-playwright-testing.md → Full browser E2E tests with SignalR

---

## Lessons Learned

These are extracted from Cycles 16, 18, 22 and research observations. They represent hard-won knowledge from building two production BFFs (Storefront and Vendor Portal).

### ✅ What We Got Right

**1. Return typed messages from handlers—let Wolverine route them**
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

**6. Stub HTTP clients for integration tests**
Stub pattern (not mocks) allows test data configuration without complex mocking setup. Tests run faster and are more maintainable.

---

### ❌ What We Got Wrong (Don't Repeat These)

**❌ Hand-rolling a broadcaster (`EventBroadcaster.cs` / `IEventBroadcaster.cs`)**

Before Wolverine's SignalR transport was adopted, CritterSupply had a 70-line `EventBroadcaster.cs` with `ConcurrentDictionary<Guid, List<Channel<StorefrontEvent>>>`, cleanup logic, and thread-safety bookkeeping. It reimplemented what SignalR already provides. This was entirely replaced by:

```csharp
opts.UseSignalR();
opts.Publish(x => x.MessagesImplementing<IStorefrontWebSocketMessage>().ToSignalR());
```

**Never hand-roll a broadcaster when `opts.UseSignalR()` is available.**

**❌ Using SSE when bidirectional was the real requirement**

The Storefront began with Server-Sent Events (SSE)—an intentional initial simplification. When checkout interactions, vendor dashboards, and interactive UI flows emerged, SSE's unidirectional constraint required an awkward "POST + wait for matching SSE event" ceremony. The migration to SignalR cost a full cycle.

**Build with SignalR from the start when any client→server messaging is foreseeable.**

**❌ Passing identity in query strings for vendor-grade contexts**

The `StorefrontHub` reads `customerId` from the query string—acceptable for a customer-facing site where session auth backs the identity. The Vendor Portal explicitly rejects this:

```csharp
// ❌ Do not do this for commercially sensitive, tenant-isolated data:
var tenantId = Context.GetHttpContext()?.Request.Query["tenantId"].ToString();

// ✅ Always use JWT claims for vendor identity:
var tenantId = Context.User!.FindFirst("VendorTenantId")?.Value;
```

`VendorTenantId` must **always** come from cryptographically-verified JWT claims. A malicious client could pass any `tenantId` in the query string, gaining access to another tenant's data.

**❌ Deferring CONTEXTS.md updates**

After the SSE→SignalR migration in the Storefront, CONTEXTS.md still contained SSE references for several cycles. This caused confusion when planning the Vendor Portal. Keep CONTEXTS.md accurate and current—it is the architectural source of truth.

**❌ Forgetting `IAsyncDisposable` on Blazor components with hub connections**

Blazor components that subscribe to SignalR must implement `IAsyncDisposable` and call `signalrClient.unsubscribe()` (or `hubConnection.DisposeAsync()`) in `DisposeAsync`. Without this, JavaScript connections persist after the component is removed, callbacks fire on disposed objects, and memory accumulates.

**❌ Not pinning the `@microsoft/signalr` CDN version**

Storefront.Web pins to `@microsoft/signalr@8.0.0`. Unpinned CDN references (`@latest`) can break silently when the SignalR client's message format changes between major versions. Always pin the CDN version and update intentionally.

**❌ Security bug: `.ToSignalR()` broadcasts to all clients**

In Cycle 19, a P0 security bug was discovered: `opts.Publish(...).ToSignalR()` broadcasts to **ALL** connected clients, regardless of group membership. Customer A could see Customer B's cart updates.

**Fix:** Use `.ToWebSocketGroup($"customer:{customerId}")` in handlers to scope delivery.

**See also:** docs/research/storefront-ux-session/WOLVERINE-SIGNALR-OBSERVATIONS.md for complete analysis.

---

### Lessons from Cycle 18 Manual Testing

**L1: DTO field name mismatches**

Shopping BC returns `"cartId"` in JSON, but Storefront's `CartDto` expected `"Id"`. Result: empty GUID in BFF responses.

**Fix:** Always verify actual API responses before creating DTOs. Integration tests with real HTTP calls would catch field name mismatches.

**L2: Value object assumptions**

BFF assumed Product Catalog returned value objects like `CatalogSku` and `CatalogProductName`, but it actually returns plain strings.

**Fix:** Don't assume value objects without checking actual API contracts. Product Catalog uses plain strings for queryable fields ([ADR 0003](../decisions/0003-value-objects-vs-primitives-queryable-fields.md)).

**L3: Type mismatches (integer vs string)**

Product Catalog returns `"status": 0` (integer enum) but BFF expected `string? Status`.

**Fix:** Integration tests with TestContainers + real BC APIs would reveal type mismatches immediately.

**Key Takeaway:** Integration tests with typed HTTP clients and TestContainers would have prevented 80% of the bugs found during manual testing.

---

## Scaling Considerations

### Backplane Requirement

SignalR connections are instance-affine—a message published to a hub group must be delivered by the instance that holds the group's connections. In a single-instance deployment this is transparent. With horizontal scaling (multiple `Storefront.Api` or `VendorPortal.Api` instances), you need a **backplane**:

```csharp
// Redis backplane (recommended—Redis is already planned for CritterSupply cache needs)
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

> **Note:** The hand-rolled `EventBroadcaster` singleton had the *same* scaling problem—it was in-memory only. Adopting SignalR makes this explicit and forces it to be properly addressed.

### Connection Lifecycle for Long-Lived Sessions

Vendor Portal sessions may run for 8+ hours. Plan for:

- **Visual "Live" indicator** — show connection status in the portal header. Vendors should know if they are receiving real-time data.
- **Reconnect-and-catch-up** — on reconnect, query for missed alerts since `lastSeenAt` timestamp. SignalR's auto-reconnect handles the WebSocket; your application must handle the data gap.
- **JWT token refresh** — 15-minute access tokens expire during long sessions. The JavaScript `accessTokenFactory` is re-called on each reconnect, so storing the latest token in memory and refreshing it proactively keeps connections alive without forcing re-login.

### Diagnostics

Inspect Wolverine's message routing for SignalR (including message type aliases) with:

```bash
dotnet run -- describe
```

Look for the "Message Routing" table in the output. SignalR-routed messages appear with destination `signalr://wolverine`.

---

## Key Principles Summary

1. **Composition over domain logic** — BFFs compose, they don't decide
2. **UI-optimized view models** — Pre-calculate, flatten, format for display
3. **Real-time via integration messages** — Subscribe to domain events, push to clients
4. **Delegation for commands** — Forward to domain BCs, don't implement business rules
5. **Integration tests only** — No domain logic means unit tests provide little value
6. **SignalR over SSE** — Simpler to start with bidirectional capabilities (see ADR 0013)
7. **MudBlazor-only** — No Bootstrap (see ADR 0005)
8. **Interactive components** — Extract from layouts to avoid RenderFragment serialization errors
9. **Always implement `IAsyncDisposable`** — Clean up SignalR connections on component dispose
10. **Group-targeted delivery** — Use `.ToWebSocketGroup()`, never broadcast with `.ToSignalR()` for scoped data

---

## References

**Architectural Decision Records:**
- [ADR 0004: SSE over SignalR (Superseded)](../decisions/0004-sse-over-signalr.md)
- [ADR 0005: MudBlazor UI Framework](../decisions/0005-mudblazor-ui-framework.md)
- [ADR 0013: Migrate from SSE to SignalR (Accepted)](../decisions/0013-signalr-migration-from-sse.md)
- [ADR 0028: JWT for Vendor Identity](../decisions/0028-jwt-for-vendor-identity.md)

**Cycle Plans & Retrospectives:**
- [Cycle 16: Customer Experience BC (BFF + Blazor)](../planning/cycles/cycle-16-customer-experience.md)
- [Cycle 18 Retrospective: Customer Experience Phase II](../planning/cycles/CYCLE-18-RETROSPECTIVE.md)
- [Cycle 22 Retrospective: Vendor Portal Phases 1–6](../planning/cycles/cycle-22-retrospective.md)

**Research & Observations:**
- [Wolverine + SignalR: Observations and Open-Source Contribution Notes](../research/storefront-ux-session/WOLVERINE-SIGNALR-OBSERVATIONS.md)

**Skills Documentation:**
- [Wolverine + SignalR: Real-Time Transport Patterns](./wolverine-signalr.md)
- [Wolverine Message Handlers](./wolverine-message-handlers.md)
- [Marten Event Sourcing](./marten-event-sourcing.md)
- [Blazor WASM + JWT Authentication](./blazor-wasm-jwt.md)
- [E2E Testing with Playwright](./e2e-playwright-testing.md)
- [CritterStack Testing Patterns](./critterstack-testing-patterns.md)

**External References:**
- [Wolverine SignalR Transport Docs](https://wolverinefx.io/guide/messaging/transports/signalr.html)
- [ASP.NET Core SignalR Introduction](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0)
- [CloudEvents Specification](https://cloudevents.io/)
- [MudBlazor Component Gallery](https://mudblazor.com/components)

**CONTEXTS.md:**
- [Customer Experience](../../CONTEXTS.md#customer-experience)
- [Vendor Portal](../../CONTEXTS.md#vendor-portal)

---

## Appendix: SSE (Historical Reference)

> **Status:** SSE was the original real-time transport for Storefront BC (Cycle 16, ADR 0004). It was superseded by SignalR in Cycle 22 (ADR 0013). This section is preserved for historical reference and for developers evaluating SSE vs. SignalR for new projects.

### When SSE Was the Right Choice

**Original Decision (Cycle 16):** Use .NET 10's native Server-Sent Events (SSE) for one-way server→client push.

**Rationale:**
- **Simpler Protocol:** SSE is one-way (matches the initial use case)
- **Native Support:** .NET 10 has first-class SSE support (`IAsyncEnumerable<T>`)
- **No WebSocket Complexity:** No need for bidirectional communication
- **HTTP/2 Efficiency:** SSE works over HTTP/2 with multiplexing

**Trade-offs accepted:**
- One-way communication only (use HTTP POST for commands)
- Manual connection tracking (store active subscriptions in cache)
- Single SSE stream per client (multiplex cart + order + shipment events over one connection)

### SSE Endpoint Pattern

```csharp
// Storefront/Notifications/StorefrontHub.cs (Cycle 16 version)
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
}
```

### JavaScript EventSource Client

```javascript
// Storefront.Web/wwwroot/js/sse-client.js (Cycle 16 version)
window.sseClient = {
    eventSource: null,

    subscribe: function (customerId, dotNetHelper) {
        if (this.eventSource) {
            this.eventSource.close();
        }

        const url = `http://localhost:5237/sse/storefront?customerId=${customerId}`;
        this.eventSource = new EventSource(url);

        this.eventSource.onopen = function () {
            console.log('SSE connection opened');
        };

        this.eventSource.onmessage = function (event) {
            try {
                const data = JSON.parse(event.data);
                dotNetHelper.invokeMethodAsync('OnSseEvent', data);
            } catch (error) {
                console.error('Failed to parse SSE event:', error);
            }
        };

        this.eventSource.onerror = function (error) {
            console.error('SSE connection error:', error);
            // EventSource will automatically attempt to reconnect
        };
    },

    unsubscribe: function () {
        if (this.eventSource) {
            this.eventSource.close();
            this.eventSource = null;
        }
    }
};
```

### Why We Migrated to SignalR

Three developments challenged the original SSE decision:

1. **Bidirectional requirements emerged** — Storefront checkout needs client→server validation (coupon checks, shipping options), Vendor Portal needs vendor submissions + live approvals
2. **Wolverine 5.x ships native SignalR transport** — Replaces hand-rolled `EventBroadcaster` abstraction with framework-level routing
3. **CritterStack alignment** — JasperFx's own CritterWatch uses SignalR; CritterSupply should demonstrate idiomatic patterns

**See ADR 0013 for complete cost/benefit analysis and migration path.**

---

**Last Updated:** 2026-03-13 (Cycle 27 complete)
**Document Version:** 2.0 (SignalR-first; SSE historical reference only)
