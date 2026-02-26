# Vertical Slice Organization

File organization patterns for bounded contexts in CritterSupply.

## Core Principle: Colocation

Commands, validators, and handlers belong in the **same file**. This provides:

1. **Single location for comprehension** — See the complete workflow without file hopping
2. **Tight coupling made explicit** — Commands and handlers are 1:1 by design
3. **Onboarding efficiency** — New developers understand "what happens" quickly

## Standard File Structure

```
src/
  <Bounded Context Name>/
    <ProjectName>/
      Features/
        <Feature Area>/
          Commands/
            AddItemToCart.cs       # Command + Validator + Handler
            RemoveItemFromCart.cs
            InitializeCart.cs
          Queries/
            GetCartById.cs         # Query + Handler
            GetCartSummary.cs
          Events/
            ItemAdded.cs           # Domain event (separate file)
            ItemRemoved.cs
            CartInitialized.cs
      Domain/
        Cart.cs                    # Aggregate (event-sourced or document)
        CartLineItem.cs            # Value objects
        CartStatus.cs              # Enums
      Infrastructure/
        MartenConfiguration.cs     # Marten/EF Core setup
```

## Command File Pattern

A single file contains the command, its validator, and its handler:

```csharp
// File: AddItemToCart.cs
using FluentValidation;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart.Commands;

// 1. Command definition
public sealed record AddItemToCart(
    Guid CartId,
    string Sku,
    int Quantity,
    decimal UnitPrice)
{
    // 2. Validator as nested class
    public class AddItemToCartValidator : AbstractValidator<AddItemToCart>
    {
        public AddItemToCartValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        }
    }
}

// 3. Handler as static class
public static class AddItemToCartHandler
{
    public static ProblemDetails Before(AddItemToCart command, Cart? cart)
    {
        if (cart is null)
            return new ProblemDetails { Detail = "Cart not found", Status = 404 };

        if (cart.IsTerminal)
            return new ProblemDetails { Detail = "Cart is closed", Status = 400 };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/carts/{cartId}/items")]
    public static ItemAdded Handle(
        AddItemToCart command,
        [WriteAggregate] Cart cart)
    {
        return new ItemAdded(
            command.Sku,
            command.Quantity,
            command.UnitPrice,
            DateTimeOffset.UtcNow);
    }
}
```

## Query File Pattern

```csharp
// File: GetCartById.cs
namespace Shopping.Cart.Queries;

public sealed record GetCartById(Guid CartId);

public static class GetCartByIdHandler
{
    [WolverineGet("/api/carts/{cartId}")]
    public static async Task<CartView?> Handle(
        Guid cartId,
        IDocumentSession session,
        CancellationToken ct)
    {
        var cart = await session.Events.AggregateStreamAsync<Cart>(cartId, ct);

        if (cart is null)
            return null;

        return new CartView(
            cart.Id,
            cart.Items.Values.ToList(),
            cart.Status);
    }
}

public sealed record CartView(
    Guid Id,
    List<CartLineItem> Items,
    CartStatus Status);
```

## Event File Pattern

Events are in **separate files** unless they have subscription handlers:

```csharp
// File: ItemAdded.cs — Event only, no handler
namespace Shopping.Cart.Events;

public sealed record ItemAdded(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    DateTimeOffset AddedAt);
```

If an event has a subscription handler, include both:

```csharp
// File: OrderPlaced.cs — Event + subscription handler
namespace Orders.Events;

public sealed record OrderPlaced(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderLine> Lines,
    DateTimeOffset PlacedAt);

// Marten event subscription
public static class OrderPlacedSubscription
{
    public static async Task Handle(
        IEvent<OrderPlaced> @event,
        IMessageBus bus,
        CancellationToken ct)
    {
        // Publish integration message to other BCs
        await bus.PublishAsync(new IntegrationMessages.OrderPlaced(
            @event.StreamId,
            @event.Data.CustomerId,
            @event.Data.PlacedAt));
    }
}
```

## Naming Conventions

| Type | File Name | Class Name |
|------|-----------|------------|
| Command | `{CommandName}.cs` | `{CommandName}` |
| Command Validator | (same file) | `{CommandName}Validator` (nested) |
| Command Handler | (same file) | `{CommandName}Handler` |
| Query | `{QueryName}.cs` | `{QueryName}` |
| Query Handler | (same file) | `{QueryName}Handler` |
| Domain Event | `{EventName}.cs` | `{EventName}` |

## Solution Organization

The .NET solution mirrors bounded context boundaries:

```xml
<Solution>
  <Folder Name="/Customer Identity/">
    <Project Path="src/Customer Identity/Customers/Customers.csproj" />
    <Project Path="tests/Customer Identity/Customers.IntegrationTests/..." />
  </Folder>

  <Folder Name="/Orders/">
    <Project Path="src/Orders/Orders/Orders.csproj" />
    <Project Path="src/Orders/Orders.Api/Orders.Api.csproj" />
    <Project Path="tests/Orders/Orders.IntegrationTests/..." />
  </Folder>

  <Folder Name="/Shared/">
    <Project Path="src/Shared/Messages.Contracts/Messages.Contracts.csproj" />
  </Folder>
</Solution>
```

## Physical Folder Structure

```
src/
  Customer Identity/           # BC folder
    Customers/                 # Domain + API (single project)
  Orders/                      # BC folder
    Orders/                    # Domain project
    Orders.Api/                # API project (separate)
  Payments/
    Payments/
    Payments.Api/
  Shared/
    Messages.Contracts/        # Shared integration messages

tests/
  Customer Identity/
    Customers.IntegrationTests/
  Orders/
    Orders.IntegrationTests/
    Orders.UnitTests/
```

## Project Naming Conventions

### Single Project Pattern

When domain and API are combined in one project:

```
src/
  <BC Name>/
    <ProjectName>/              # SDK: Microsoft.NET.Sdk.Web
      <ProjectName>.csproj
      Program.cs                # Hosting + Wolverine + Marten config
      Features/                 # Handlers (commands, queries)
      Domain/                   # Aggregates, value objects

tests/
  <BC Name>/
    <ProjectName>.IntegrationTests/
      <ProjectName>.IntegrationTests.csproj
```

**Example:** Customer Identity BC
- `src/Customer Identity/Customers/Customers.csproj`
- `tests/Customer Identity/Customers.IntegrationTests/`

### Split Project Pattern

When domain and API are separated:

```
src/
  <BC Name>/
    <ProjectName>/              # SDK: Microsoft.NET.Sdk (class library)
      <ProjectName>.csproj
      Features/                 # Handlers (domain logic only)
      Domain/                   # Aggregates, value objects
    <ProjectName>.Api/          # SDK: Microsoft.NET.Sdk.Web
      <ProjectName>.Api.csproj
      Program.cs                # Hosting + Wolverine + Marten config
      Features/                 # HTTP endpoint handlers

tests/
  <BC Name>/
    <ProjectName>.Api.IntegrationTests/  # NOTE: Named after API project!
      <ProjectName>.Api.IntegrationTests.csproj
```

**Example:** Orders BC
- Domain: `src/Orders/Orders/Orders.csproj`
- API: `src/Orders/Orders.Api/Orders.Api.csproj`
- Tests: `tests/Orders/Orders.Api.IntegrationTests/`

**Example:** Product Catalog BC
- Domain: `src/Product Catalog/ProductCatalog/ProductCatalog.csproj`
- API: `src/Product Catalog/ProductCatalog.Api/ProductCatalog.Api.csproj`
- Tests: `tests/Product Catalog/ProductCatalog.Api.IntegrationTests/`

### Key Naming Rules

1. **Domain project name** = Base name (e.g., `Orders`, `ProductCatalog`, `Payments`)
2. **API project name** = Base name + `.Api` suffix (e.g., `Orders.Api`, `ProductCatalog.Api`)
3. **Test project name** = API project name + `.IntegrationTests` suffix
4. **IMPORTANT:** When split, tests reference the **API project**, not the domain project
5. **Folder names** can have spaces (e.g., `Customer Identity/`), but project names should not

### When to Split Projects

**Single project (domain + API):**
- Simple BCs with straightforward hosting
- No plans to share domain logic across multiple hosts
- Small team or solo development

**Separate projects (domain + API):**
- Complex hosting requirements (multiple entry points)
- Shared domain logic across multiple hosts (API + background workers)
- Clear separation needed for large teams
- Most BCs in CritterSupply use this pattern

### Common Mistake to Avoid

❌ **Wrong:** Test project named after domain project when using split pattern
```
src/Orders/Orders/
src/Orders/Orders.Api/
tests/Orders/Orders.IntegrationTests/  # WRONG!
```

✅ **Correct:** Test project named after API project
```
src/Orders/Orders/
src/Orders/Orders.Api/
tests/Orders/Orders.Api.IntegrationTests/  # CORRECT
```

**Why?** Integration tests host the API project (which contains `Program.cs` and HTTP endpoints), not the domain project.

## Integration Messages Location

Cross-context messages live in `Messages.Contracts`:

```
src/Shared/Messages.Contracts/
  Shopping/
    CheckoutInitiated.cs
    ItemAdded.cs
  Orders/
    OrderPlaced.cs
    OrderShipped.cs
  Payments/
    PaymentAuthorized.cs
    PaymentCaptured.cs
```

Each BC references `Messages.Contracts` to publish/subscribe to integration messages.
