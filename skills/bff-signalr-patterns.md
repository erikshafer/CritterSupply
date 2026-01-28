# Backend-for-Frontend (BFF) & SignalR Patterns

Patterns for building customer-facing frontends that aggregate data from multiple bounded contexts.

## What is a BFF?

A Backend-for-Frontend is an intermediate layer between the frontend and domain BCs that:

- **Composes** data from multiple BCs into frontend-optimized view models
- **Orchestrates** queries and commands across BCs for UI workflows
- **Aggregates** domain events into real-time notifications
- **Does NOT contain domain logic** — it delegates to domain BCs

## When to Use BFF

**Use BFF for:**
- Customer-facing web/mobile apps querying multiple BCs
- Real-time notification requirements (SignalR, WebSockets)
- Different client types with different composition needs

**Avoid BFF for:**
- Internal admin tools (direct BC access acceptable)
- Simple CRUD apps with single BC
- APIs consumed by other backend services

## Project Structure

```
src/
  Customer Experience/
    Storefront/                 # BFF domain
      Composition/              # View model composition
      Notifications/            # SignalR hub + handlers
      Queries/                  # BFF query handlers
      Commands/                 # Delegation to domain BCs
      Clients/                  # HTTP clients for domain BCs
    Storefront.Web/             # Blazor Server app
      Pages/
      Components/
```

## View Composition

BFFs compose data from multiple BCs — they don't contain business rules:

```csharp
public static class GetCheckoutViewHandler
{
    [WolverineGet("/api/storefront/checkout/{checkoutId}")]
    public static async Task<CheckoutView> Handle(
        GetCheckoutView query,
        IOrdersClient ordersClient,
        ICustomerIdentityClient identityClient,
        CancellationToken ct)
    {
        // Query Orders BC
        var checkout = await ordersClient.GetCheckoutAsync(query.CheckoutId, ct);

        // Query Customer Identity BC
        var addresses = await identityClient.GetCustomerAddressesAsync(
            checkout.CustomerId,
            AddressType.Shipping,
            ct);

        // Compose view model optimized for frontend
        return new CheckoutView(
            checkout.CheckoutId,
            checkout.Items,
            addresses);
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

    // ...
}

// GOOD — delegate to domain BC
public static async Task Handle(CompleteCheckout command, IOrdersClient ordersClient, ...)
{
    await ordersClient.CompleteCheckoutAsync(command);
}
```

## View Models

BFF view models prioritize frontend usability over domain purity:

```csharp
// View model optimized for checkout wizard UI
public sealed record CheckoutView(
    Guid CheckoutId,
    Guid CustomerId,
    CheckoutStatus Status,
    List<LineItemSummary> Items,        // Flattened for display
    decimal TotalAmount,                 // Pre-calculated
    List<AddressSummary> Addresses,     // Display strings ready
    bool CanComplete);                   // UI state flag

// Display-optimized nested type
public sealed record LineItemSummary(
    string Sku,
    string ProductName,                  // Joined from Catalog BC
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,                   // Pre-calculated
    bool IsInStock);                     // From Inventory BC
```

## Real-Time Updates with SignalR

BFFs subscribe to integration messages and push updates to connected clients:

```csharp
// Storefront/Notifications/CartUpdateNotifier.cs
public static class CartUpdateNotifier
{
    // Handler for Shopping BC integration message
    public static async Task Handle(
        Shopping.ItemAdded message,
        IHubContext<StorefrontHub> hubContext,
        IShoppingClient shoppingClient,
        CancellationToken ct)
    {
        // Query Shopping BC for updated cart state
        var cart = await shoppingClient.GetCartAsync(message.CartId, ct);

        // Compose view model
        var cartSummary = new CartSummaryView(
            cart.Id,
            cart.Items.Count,
            cart.TotalAmount);

        // Push to connected clients via SignalR
        await hubContext.Clients
            .Group($"cart:{message.CartId}")
            .SendAsync("CartUpdated", cartSummary, ct);
    }
}
```

> **Reference:** [ASP.NET Core SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction)

## SignalR Hub

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

## Program.cs Configuration

```csharp
builder.Services.AddSignalR();

builder.Host.UseWolverine(opts =>
{
    // Subscribe to integration messages from domain BCs
    opts.ListenToRabbitQueue("storefront-notifications");
});

app.MapHub<StorefrontHub>("/storefronthub");
```

## Blazor Component with Real-Time Updates

```razor
@page "/cart/{cartId:guid}"
@inject IStorefrontClient StorefrontClient
@inject NavigationManager Navigation
@implements IAsyncDisposable

<h1>Shopping Cart</h1>

@if (cart is null)
{
    <p>Loading...</p>
}
else
{
    <CartSummary Cart="@cart" />
    <button @onclick="Checkout">Proceed to Checkout</button>
}

@code {
    [Parameter] public Guid CartId { get; set; }

    private HubConnection? hubConnection;
    private CartView? cart;

    protected override async Task OnInitializedAsync()
    {
        // Initial load from BFF
        cart = await StorefrontClient.GetCartViewAsync(CartId);

        // Connect to SignalR hub
        hubConnection = new HubConnectionBuilder()
            .WithUrl(Navigation.ToAbsoluteUri("/storefronthub"))
            .Build();

        // Subscribe to real-time updates
        hubConnection.On<CartSummaryView>("CartUpdated", async (updated) =>
        {
            cart = await StorefrontClient.GetCartViewAsync(CartId);
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

> **Reference:** [Blazor SignalR Client](https://learn.microsoft.com/en-us/aspnet/core/blazor/tutorials/signalr-blazor)

## Testing BFFs

BFFs don't contain domain logic, so focus on integration tests:

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
        s.Get.Url($"/api/storefront/checkout/{checkoutId}");
        s.StatusCodeShouldBe(200);
    });

    var view = result.ReadAsJson<CheckoutView>();

    // Assert — view contains composed data
    view.CheckoutId.ShouldBe(checkoutId);
    view.Items.ShouldNotBeEmpty();       // From Orders BC
    view.Addresses.ShouldNotBeEmpty();   // From Customer Identity BC
}
```

## Key Principles

1. **Composition over domain logic** — BFFs compose, they don't decide
2. **UI-optimized view models** — Pre-calculate, flatten, format for display
3. **Real-time via integration messages** — Subscribe to domain events, push to clients
4. **Delegation for commands** — Forward to domain BCs, don't implement business rules
5. **Integration tests only** — No domain logic means unit tests provide little value
