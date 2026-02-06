# Backend-for-Frontend (BFF) & Real-Time Patterns

Patterns for building customer-facing frontends that aggregate data from multiple bounded contexts with real-time updates.

## What is a BFF?

A Backend-for-Frontend is an intermediate layer between the frontend and domain BCs that:

- **Composes** data from multiple BCs into frontend-optimized view models
- **Orchestrates** queries and commands across BCs for UI workflows
- **Aggregates** domain events into real-time notifications
- **Does NOT contain domain logic** — it delegates to domain BCs

## When to Use BFF

**Use BFF for:**
- Customer-facing web/mobile apps querying multiple BCs
- Real-time notification requirements (SSE, WebSockets, SignalR)
- Different client types with different composition needs
- Complex UI workflows spanning multiple BCs

**Avoid BFF for:**
- Internal admin tools (direct BC access acceptable)
- Simple CRUD apps with single BC
- APIs consumed by other backend services

## Project Structure (CritterSupply Pattern)

CritterSupply uses a **3-project structure** for BFF bounded contexts:

```
src/
  Customer Experience/
    Storefront/                 # BFF Domain (regular SDK)
      Composition/              # View model types
        CartView.cs
        CheckoutView.cs
      Notifications/            # Integration message handlers + SSE
        IEventBroadcaster.cs    # Pub/sub interface
        EventBroadcaster.cs     # Channel<T> implementation
        StorefrontEvent.cs      # Discriminated union for SSE
        ItemAddedHandler.cs     # Integration message handler
      Clients/                  # HTTP client interfaces (domain)
        IShoppingClient.cs
        IOrdersClient.cs

    Storefront.Api/             # BFF API (Web SDK)
      Program.cs                # Wolverine + Marten + DI
      Queries/                  # BFF composition endpoints
        GetCartView.cs          # [WolverineGet] handler
        GetCheckoutView.cs
      Clients/                  # HTTP client implementations
        ShoppingClient.cs
        OrdersClient.cs
      StorefrontHub.cs          # SSE endpoint

    Storefront.Web/             # Blazor Server UI (Web SDK)
      Program.cs                # MudBlazor + HttpClient config
      Components/
        Layout/
          MainLayout.razor
          InteractiveAppBar.razor  # Interactive components
        Pages/
          Cart.razor            # SSE-enabled page
          Checkout.razor
      wwwroot/
        js/sse-client.js        # JavaScript EventSource client
```

**Why 3 projects?**
1. **Storefront (Domain)** - Composition logic, interfaces, integration handlers (no HTTP)
2. **Storefront.Api (BFF API)** - HTTP endpoints, infrastructure, HTTP clients
3. **Storefront.Web (Blazor UI)** - Frontend, MudBlazor components, JavaScript interop

**References:**
- See CLAUDE.md "BFF Project Structure Pattern" section
- Cycle 16 Phase 2c refactor notes

---

## View Composition

BFFs compose data from multiple BCs — they don't contain business rules:

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
        // Query Orders BC for checkout state
        var checkout = await ordersClient.GetCheckoutAsync(checkoutId, ct);

        // Query Customer Identity BC for saved addresses
        var addresses = await identityClient.GetCustomerAddressesAsync(
            checkout.CustomerId,
            AddressType.Shipping,
            ct);

        // Query Product Catalog BC for product details
        var products = await catalogClient.GetProductsBySkusAsync(
            checkout.Items.Select(i => i.Sku).ToList(),
            ct);

        // Compose view model optimized for frontend
        return new CheckoutView(
            checkout.CheckoutId,
            checkout.CustomerId,
            checkout.CurrentStep,
            EnrichLineItems(checkout.Items, products),  // Join data
            addresses,
            checkout.Subtotal,
            checkout.ShippingCost,
            checkout.Total,
            checkout.CanProceedToNextStep);
    }
}
```

**Don't put domain logic in the BFF:**

```csharp
// BAD — domain logic belongs in Orders BC
public static async Task Handle(CompleteCheckout command, ...)
{
    if (command.Items.Count == 0)
        throw new InvalidOperationException("Cannot checkout empty cart");

    // Validate payment token
    if (string.IsNullOrEmpty(command.PaymentToken))
        throw new InvalidOperationException("Payment required");

    // Calculate totals (WRONG - this is domain logic!)
    var total = command.Items.Sum(i => i.Quantity * i.UnitPrice);
    // ...
}

// GOOD — delegate to domain BC
public static async Task Handle(CompleteCheckout command, IOrdersClient ordersClient, ...)
{
    // BFF just forwards - Orders BC contains validation and business rules
    await ordersClient.CompleteCheckoutAsync(command);
}
```

---

## View Models

BFF view models prioritize frontend usability over domain purity:

```csharp
// Storefront/Composition/CheckoutView.cs

/// <summary>
/// Composed view for checkout wizard (aggregates Orders BC + Customer Identity BC)
/// </summary>
public sealed record CheckoutView(
    Guid CheckoutId,
    Guid CustomerId,
    CheckoutStep CurrentStep,
    IReadOnlyList<CartLineItemView> Items,
    IReadOnlyList<AddressSummary> SavedAddresses,  // From Customer Identity BC
    decimal Subtotal,
    decimal ShippingCost,
    decimal Total,
    bool CanProceedToNextStep);

/// <summary>
/// Line item enriched with product details from Catalog BC
/// </summary>
public sealed record CartLineItemView(
    string Sku,
    string ProductName,        // Joined from Catalog BC
    string ProductImageUrl,    // Joined from Catalog BC
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);        // Pre-calculated for display

/// <summary>
/// Address summary for dropdown selection (from Customer Identity BC)
/// </summary>
public sealed record AddressSummary(
    Guid AddressId,
    string Nickname,
    string DisplayLine);  // Pre-formatted: "123 Main St, Seattle, WA 98101"
```

**Key principles:**
- **Flatten nested structures** - UI doesn't need deep hierarchies
- **Pre-calculate values** - Don't make UI do math
- **Pre-format strings** - `DisplayLine` ready for display
- **Include UI state** - `CanProceedToNextStep` flag

---

## Real-Time Updates: SSE vs SignalR

CritterSupply uses **Server-Sent Events (SSE)** for real-time updates (see ADR 0004).

**Decision:** SSE over SignalR
- ✅ Simpler (built into HTTP, no library needed)
- ✅ One-way push (perfect for notifications)
- ✅ Native browser support (`EventSource`)
- ✅ Works with `IAsyncEnumerable<T>` in .NET 10
- ❌ SignalR adds complexity for our use case

**When to use SignalR instead:**
- Need bidirectional communication (client sends to server frequently)
- Need connection lifecycle management (reconnect logic)
- Need typed hubs with client-server contracts

---

## SSE with Blazor (Recommended)

### EventBroadcaster Pattern

Use `System.Threading.Channels.Channel<T>` for in-memory pub/sub:

```csharp
// Storefront/Notifications/IEventBroadcaster.cs
public interface IEventBroadcaster
{
    Task BroadcastAsync(Guid customerId, StorefrontEvent @event, CancellationToken ct = default);
    IAsyncEnumerable<StorefrontEvent> SubscribeAsync(Guid customerId, CancellationToken ct);
}

// Storefront/Notifications/EventBroadcaster.cs
public sealed class EventBroadcaster : IEventBroadcaster
{
    private readonly ConcurrentDictionary<Guid, List<Channel<StorefrontEvent>>> _subscriptions = new();

    public async Task BroadcastAsync(Guid customerId, StorefrontEvent @event, CancellationToken ct = default)
    {
        if (!_subscriptions.TryGetValue(customerId, out var channels))
            return;

        foreach (var channel in channels)
        {
            await channel.Writer.WriteAsync(@event, ct);
        }
    }

    public async IAsyncEnumerable<StorefrontEvent> SubscribeAsync(
        Guid customerId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<StorefrontEvent>();

        // Add channel to customer's subscription list
        _subscriptions.AddOrUpdate(
            customerId,
            _ => new List<Channel<StorefrontEvent>> { channel },
            (_, list) => { list.Add(channel); return list; });

        try
        {
            await foreach (var @event in channel.Reader.ReadAllAsync(ct))
            {
                yield return @event;
            }
        }
        finally
        {
            // Cleanup on disconnect
            if (_subscriptions.TryGetValue(customerId, out var channels))
            {
                channels.Remove(channel);
                if (channels.Count == 0)
                    _subscriptions.TryRemove(customerId, out _);
            }
        }
    }
}
```

**Register as singleton:**
```csharp
// Storefront.Api/Program.cs
builder.Services.AddSingleton<IEventBroadcaster, EventBroadcaster>();
```

---

### Discriminated Union for SSE Events

Use JSON polymorphic serialization for multiplexing event types:

```csharp
// Storefront/Notifications/StorefrontEvent.cs

[JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
[JsonDerivedType(typeof(CartUpdated), typeDiscriminator: "cart-updated")]
[JsonDerivedType(typeof(OrderStatusChanged), typeDiscriminator: "order-status-changed")]
[JsonDerivedType(typeof(ShipmentStatusChanged), typeDiscriminator: "shipment-status-changed")]
public abstract record StorefrontEvent(DateTimeOffset OccurredAt);

public sealed record CartUpdated(
    Guid CartId,
    Guid CustomerId,
    int ItemCount,
    decimal TotalAmount,
    DateTimeOffset OccurredAt) : StorefrontEvent(OccurredAt);

public sealed record OrderStatusChanged(
    Guid OrderId,
    Guid CustomerId,
    string NewStatus,
    DateTimeOffset OccurredAt) : StorefrontEvent(OccurredAt);
```

**JSON output:**
```json
{
  "eventType": "cart-updated",
  "cartId": "...",
  "customerId": "...",
  "itemCount": 3,
  "totalAmount": 129.99,
  "occurredAt": "2026-02-05T19:30:00Z"
}
```

---

### SSE Endpoint (Wolverine HTTP)

```csharp
// Storefront.Api/StorefrontHub.cs
[WolverineGet("/sse/storefront")]
public static async IAsyncEnumerable<StorefrontEvent> SubscribeToUpdates(
    Guid customerId,
    IEventBroadcaster broadcaster,
    [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var @event in broadcaster.SubscribeAsync(customerId, ct))
    {
        yield return @event;
    }
}
```

**How it works:**
- Client: `GET /sse/storefront?customerId=...`
- Returns `Content-Type: text/event-stream`
- Long-lived connection streams JSON events
- Wolverine serializes `StorefrontEvent` to SSE format

---

### Integration Message Handler (Publishes to SSE)

```csharp
// Storefront/Notifications/ItemAddedHandler.cs
public static class ItemAddedHandler
{
    public static async Task Handle(
        Messages.Contracts.Shopping.ItemAdded message,
        IShoppingClient shoppingClient,
        IEventBroadcaster broadcaster,
        CancellationToken ct)
    {
        // Query Shopping BC for updated cart state
        var cart = await shoppingClient.GetCartAsync(message.CartId, ct);

        if (cart is null)
            return;

        // Create SSE event
        var @event = new CartUpdated(
            cart.CartId,
            cart.CustomerId,
            cart.Items.Count,
            cart.Subtotal,
            message.AddedAt);

        // Broadcast to all clients subscribed to this customer
        await broadcaster.BroadcastAsync(cart.CustomerId, @event, ct);
    }
}
```

**Flow:**
1. Shopping BC publishes `Shopping.ItemAdded` to RabbitMQ
2. Storefront BFF receives message via Wolverine subscription
3. Handler queries Shopping BC for latest cart state
4. Handler broadcasts `CartUpdated` SSE event
5. EventBroadcaster pushes to all connected clients for that customerId
6. Blazor UI receives event via JavaScript EventSource
7. Blazor UI updates cart display in real-time

---

### JavaScript EventSource Client

```javascript
// Storefront.Web/wwwroot/js/sse-client.js
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
            console.log('SSE event received:', event.data);
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
            console.log('SSE connection closed');
        }
    }
};
```

---

### Blazor Component with SSE

```razor
@page "/cart"
@rendermode InteractiveServer
@using System.Text.Json
@inject IHttpClientFactory HttpClientFactory
@inject IJSRuntime JS
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

    @if (_sseConnected)
    {
        <MudChip T="string" Icon="@Icons.Material.Filled.Wifi" Color="Color.Success">
            Real-time updates active
        </MudChip>
    }
}

@code {
    private CartView? _cartView;
    private bool _sseConnected = false;
    private DotNetObjectReference<Cart>? _dotNetHelper;

    private readonly Guid _customerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _cartId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    protected override async Task OnInitializedAsync()
    {
        await LoadCart();
        await SubscribeToSSE();
    }

    private async Task LoadCart()
    {
        var client = HttpClientFactory.CreateClient("StorefrontApi");
        _cartView = await client.GetFromJsonAsync<CartView>($"/api/storefront/carts/{_cartId}");
    }

    private async Task SubscribeToSSE()
    {
        _dotNetHelper = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("sseClient.subscribe", _customerId.ToString(), _dotNetHelper);
        _sseConnected = true;
    }

    [JSInvokable]
    public async Task OnSseEvent(JsonElement eventData)
    {
        if (eventData.TryGetProperty("eventType", out var eventType) &&
            eventType.GetString() == "cart-updated")
        {
            // Reload cart data from BFF
            await LoadCart();
            StateHasChanged();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await JS.InvokeVoidAsync("sseClient.unsubscribe");
        _dotNetHelper?.Dispose();
    }

    private sealed record CartView(Guid CartId, Guid CustomerId, IReadOnlyList<CartLineItemView> Items, decimal Subtotal);
    private sealed record CartLineItemView(string Sku, string ProductName, string ProductImageUrl, int Quantity, decimal UnitPrice, decimal LineTotal);
}
```

**Key patterns:**
- `@rendermode InteractiveServer` - Required for `OnInitializedAsync()` and `StateHasChanged()`
- `DotNetObjectReference<T>` - Allows JavaScript to call back to .NET
- `[JSInvokable]` - Marks method callable from JavaScript
- `IAsyncDisposable` - Clean up SSE connection on navigation away

---

## SignalR with Blazor (Alternative)

> **Note:** CritterSupply uses SSE (see above). SignalR patterns kept for reference.

### SignalR Hub

```csharp
public class StorefrontHub : Hub
{
    public async Task SubscribeToCart(Guid cartId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"cart:{cartId}");
    }

    public async Task UnsubscribeFromCart(Guid cartId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"cart:{cartId}");
    }

    public async Task SubscribeToOrder(Guid orderId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"order:{orderId}");
    }
}
```

### Integration Message Handler (SignalR)

```csharp
public static class CartUpdateNotifier
{
    public static async Task Handle(
        Shopping.ItemAdded message,
        IHubContext<StorefrontHub> hubContext,
        IShoppingClient shoppingClient,
        CancellationToken ct)
    {
        var cart = await shoppingClient.GetCartAsync(message.CartId, ct);

        var cartSummary = new CartSummaryView(
            cart.Id,
            cart.Items.Count,
            cart.TotalAmount);

        await hubContext.Clients
            .Group($"cart:{message.CartId}")
            .SendAsync("CartUpdated", cartSummary, ct);
    }
}
```

### Program.cs Configuration (SignalR)

```csharp
builder.Services.AddSignalR();

builder.Host.UseWolverine(opts =>
{
    opts.ListenToRabbitQueue("storefront-notifications");
});

app.MapHub<StorefrontHub>("/storefronthub");
```

### Blazor Component with SignalR

```razor
@inject NavigationManager Navigation
@implements IAsyncDisposable

@code {
    private HubConnection? hubConnection;

    protected override async Task OnInitializedAsync()
    {
        hubConnection = new HubConnectionBuilder()
            .WithUrl(Navigation.ToAbsoluteUri("/storefronthub"))
            .Build();

        hubConnection.On<CartSummaryView>("CartUpdated", async (updated) =>
        {
            await LoadCart();
            StateHasChanged();
        });

        await hubConnection.StartAsync();
        await hubConnection.InvokeAsync("SubscribeToCart", CartId);
    }

    public async ValueTask DisposeAsync()
    {
        if (hubConnection is not null)
            await hubConnection.DisposeAsync();
    }
}
```

---

## MudBlazor Integration

CritterSupply uses MudBlazor for Material Design UI (see ADR 0005).

### Setup

**1. Add to Directory.Packages.props:**
```xml
<PackageVersion Include="MudBlazor" Version="8.5.1" />
```

**2. Add to Storefront.Web.csproj:**
```xml
<PackageReference Include="MudBlazor" />
```

**3. Configure in Program.cs:**
```csharp
builder.Services.AddMudServices();
```

**4. Add to App.razor head:**
```html
<link href="https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap" rel="stylesheet" />
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
```

**5. Add to App.razor body (before Blazor script):**
```html
<script src="_content/MudBlazor/MudBlazor.min.js"></script>
```

**6. Add to _Imports.razor:**
```razor
@using MudBlazor
```

**7. Wrap app in providers:**
```razor
<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <!-- Your app here -->
</MudLayout>
```

### Bootstrap Removal Checklist

`dotnet new blazor` scaffolds Bootstrap by default. **Remove it:**

- ✅ Delete `wwwroot/lib/bootstrap/` directory
- ✅ Remove Bootstrap CSS references from `App.razor`
- ✅ Clean up `wwwroot/app.css` (remove Bootstrap classes like `.btn-primary`, `.form-control`)
- ✅ Verify MudBlazor CSS loaded in browser Network tab
- ✅ Verify NO `bootstrap.min.css` in Network tab

**Common mistake:** Forgetting to remove Bootstrap leads to style conflicts.

### Interactive Component Pattern

**Problem:** Blazor layouts cannot have `@rendermode` when they receive `@Body` (RenderFragment serialization error).

**Solution:** Extract interactive UI to child components:

```razor
<!-- MainLayout.razor (no @rendermode) -->
@inherits LayoutComponentBase

<MudThemeProvider />
<MudPopoverProvider />

<MudLayout>
    <InteractiveAppBar @bind-DrawerOpen="@_drawerOpen" />

    <MudMainContent>
        @Body
    </MudMainContent>
</MudLayout>

@code {
    private bool _drawerOpen = true;
}
```

```razor
<!-- InteractiveAppBar.razor (has @rendermode) -->
@rendermode InteractiveServer

<MudAppBar>
    <MudIconButton Icon="@Icons.Material.Filled.Menu" OnClick="@ToggleDrawer" />
    <MudText Typo="Typo.h6">CritterSupply</MudText>
</MudAppBar>

<MudDrawer @bind-Open="@DrawerOpen">
    <MudNavMenu>
        <MudNavLink Href="/">Home</MudNavLink>
        <MudNavLink Href="/cart">Cart</MudNavLink>
    </MudNavMenu>
</MudDrawer>

@code {
    [Parameter]
    public bool DrawerOpen { get; set; } = true;

    [Parameter]
    public EventCallback<bool> DrawerOpenChanged { get; set; }

    private async Task ToggleDrawer()
    {
        DrawerOpen = !DrawerOpen;
        await DrawerOpenChanged.InvokeAsync(DrawerOpen);
    }
}
```

**Key points:**
- Layout has NO `@rendermode` (avoids RenderFragment serialization error)
- Interactive child component has `@rendermode InteractiveServer`
- Two-way binding via `@bind-DrawerOpen` parameter

---

## Blazor Render Mode Gotchas

### Error: "Cannot pass parameter 'Body' to component with rendermode"

**Full error:**
```
InvalidOperationException: Cannot pass the parameter 'Body' to component 'MainLayout'
with rendermode 'InteractiveServerRenderMode'. This is because the parameter is of the
delegate type 'Microsoft.AspNetCore.Components.RenderFragment', which is arbitrary code
and cannot be serialized.
```

**Cause:** Layout component has `@rendermode` directive and receives `@Body` parameter.

**Fix:** Remove `@rendermode` from layout, extract interactive parts to child components (see Interactive Component Pattern above).

### When to Use @rendermode InteractiveServer

**Use on:**
- Pages that need `OnInitializedAsync()`, `StateHasChanged()`
- Components with event handlers (`@onclick`, `OnClick` parameters)
- Components with JavaScript interop (`IJSRuntime`)

**Do NOT use on:**
- Layouts that receive `@Body`
- Pure presentation components (no user interaction)
- Components that only display data (no state changes)

### SSE Requires Interactive Render Mode

**Why:** SSE subscription happens in `OnInitializedAsync()` which requires interactive mode.

```razor
@page "/cart"
@rendermode InteractiveServer  <!-- Required for SSE -->
@inject IJSRuntime JS

@code {
    protected override async Task OnInitializedAsync()
    {
        await SubscribeToSSE();  // Needs interactive mode
    }
}
```

**Without `@rendermode`:** `OnInitializedAsync()` runs on server during prerender only, SSE never subscribes.

---

## Testing BFFs

BFFs don't contain domain logic, so focus on **integration tests**:

```csharp
[Fact]
public async Task GetCheckoutView_ComposesFromMultipleBCs()
{
    // Arrange — seed data in domain BCs
    var checkoutId = Guid.NewGuid();
    var customerId = Guid.NewGuid();

    await _ordersClient.CreateCheckoutAsync(checkoutId, customerId, /* ... */);
    await _identityClient.AddAddressAsync(customerId, /* ... */);

    // Act — query BFF composition endpoint
    var result = await _host.Scenario(s =>
    {
        s.Get.Url($"/api/storefront/checkouts/{checkoutId}");
        s.StatusCodeShouldBe(200);
    });

    var view = result.ReadAsJson<CheckoutView>();

    // Assert — view contains composed data from multiple BCs
    view.CheckoutId.ShouldBe(checkoutId);
    view.Items.ShouldNotBeEmpty();       // From Orders BC
    view.SavedAddresses.ShouldNotBeEmpty();   // From Customer Identity BC
}
```

**SSE Integration Tests:**

```csharp
[Fact]
public async Task ItemAddedHandler_BroadcastsCartUpdatedEvent()
{
    // Arrange
    var cartId = Guid.NewGuid();
    var customerId = Guid.NewGuid();

    // Inject integration message directly (bypasses RabbitMQ for testing)
    var message = new Messages.Contracts.Shopping.ItemAdded(
        cartId,
        customerId,
        "DOG-BOWL-01",
        2,
        19.99m,
        DateTimeOffset.Now);

    // Act
    await _wolverine.InvokeMessageAndWaitAsync(message);

    // Assert — EventBroadcaster received event
    // (In real tests, you'd subscribe to EventBroadcaster and verify event)
}
```

**What to test:**
- ✅ View composition from multiple BCs
- ✅ Integration message handlers invoke EventBroadcaster
- ✅ SSE endpoint returns expected event types
- ❌ NOT: Domain logic (that's in domain BCs)
- ❌ NOT: UI rendering (manual browser testing sufficient for Phase 3)

---

## Key Principles

1. **Composition over domain logic** — BFFs compose, they don't decide
2. **UI-optimized view models** — Pre-calculate, flatten, format for display
3. **Real-time via integration messages** — Subscribe to domain events, push to clients
4. **Delegation for commands** — Forward to domain BCs, don't implement business rules
5. **Integration tests only** — No domain logic means unit tests provide little value
6. **SSE over SignalR** — Simpler for one-way push notifications (see ADR 0004)
7. **MudBlazor-only** — No Bootstrap (see ADR 0005)
8. **Interactive components** — Extract from layouts to avoid RenderFragment serialization errors

---

## References

- **ADR 0004:** [SSE over SignalR](../docs/decisions/0004-sse-over-signalr.md)
- **ADR 0005:** [MudBlazor UI Framework](../docs/decisions/0005-mudblazor-ui-framework.md)
- **Cycle 16:** [Customer Experience Implementation](../docs/planning/cycles/cycle-16-customer-experience.md)
- **CONTEXTS.md:** Integration message contracts
- **Microsoft Docs:** [Blazor Render Modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes)
- **Microsoft Docs:** [Server-Sent Events](https://developer.mozilla.org/en-US/docs/Web/API/Server-sent_events)
- **MudBlazor:** [Component Gallery](https://mudblazor.com/components)
