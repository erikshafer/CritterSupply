# Vertical Slice Organization and Naming Conventions

File and folder organization patterns for bounded contexts in CritterSupply.

---

## Table of Contents

1. [Core Principle](#core-principle)
2. [Folder Naming Conventions](#folder-naming-conventions)
3. [File Naming Conventions](#file-naming-conventions)
4. [Colocation Patterns](#colocation-patterns)
5. [Event File Patterns](#event-file-patterns)
6. [Project Structure](#project-structure)
7. [Solution Organization](#solution-organization)
8. [Integration Messages Location](#integration-messages-location)
9. [Examples: Good vs Anti-Pattern](#examples-good-vs-anti-pattern)
10. [Lessons Learned](#lessons-learned)
11. [Appendix: Exemplary Implementations](#appendix-exemplary-implementations)

---

## Core Principle

**Vertical slices are organized around features and business capabilities, not technical concerns.**

When a developer or AI agent opens a folder, they should immediately understand **what the system does**, not **what kinds of technical artifacts it contains**.

### ✅ Feature-Oriented Organization

```
Shopping/
  Cart/                          # ← Business capability
    AddItemToCart.cs
    RemoveItemFromCart.cs
    InitializeCart.cs
    Cart.cs
```

**What this tells you:** "This BC handles shopping cart operations."

### ❌ Technically-Oriented Organization

```
Shopping/
  Commands/                      # ← Technical layer
    AddItemToCart.cs
    RemoveItemFromCart.cs
  Queries/
    GetCart.cs
  Events/
    ItemAdded.cs
```

**What this tells you:** "This BC has commands, queries, and events." (What does it *do*? You have to read the files to know.)

---

## Folder Naming Conventions

### Folder Names Reflect Business Capabilities

Feature folders should be named after **what they enable**, not **how they work**.

| ✅ Good (Business Capability) | ❌ Anti-Pattern (Technical Layer) |
|-------------------------------|-----------------------------------|
| `Cart/` | `Commands/` |
| `Checkout/` | `Queries/` |
| `AddressBook/` | `Events/` |
| `TenantManagement/` | `Handlers/` |
| `UserInvitations/` | `Validators/` |
| `Placement/` | `Data/`, `Entities/` |

### Examples from CritterSupply BCs

**Shopping BC:**
```
Shopping/
  Cart/                          # Business capability
    AddItemToCart.cs
    RemoveItemFromCart.cs
    Cart.cs
    ItemAdded.cs
```

**Vendor Identity BC (after ADR 0023 refactoring):**
```
VendorIdentity/
  TenantManagement/              # Business capability
    CreateVendorTenant.cs
    CreateVendorTenantHandler.cs
    CreateVendorTenantValidator.cs
  UserInvitations/               # Business capability
    InviteVendorUser.cs
    AcceptInvitation.cs
  Identity/                      # Infrastructure (shared across features)
    VendorIdentityDbContext.cs
    VendorTenant.cs
```

**Customer Identity BC:**
```
Customers/
  AddressBook/                   # Business capability
    AddAddress.cs
    UpdateAddress.cs
  Authentication/                # Business capability
    Login.cs
    Logout.cs
```

---

## File Naming Conventions

### File Names Reflect Operations, Not Technical Roles

**The most common failure mode:** Files named after their technical artifact type rather than the operation they represent.

---

### ❌ ANTI-PATTERN: Technically-Oriented File Naming

**DO NOT group multiple commands, events, or validators into files named by type:**

```csharp
// ❌ ReturnCommands.cs (Returns BC anti-pattern - DO NOT REPLICATE)
namespace Returns.Returns;

public sealed record RequestReturn(...);
public sealed record ApproveReturn(...);
public sealed record DenyReturn(...);
public sealed record ReceiveReturn(...);
public sealed record StartInspection(...);
public sealed record SubmitInspection(...);
public sealed record ExpireReturn(...);
public sealed record ApproveExchange(...);
public sealed record DenyExchange(...);
public sealed record ShipReplacementItem(...);
```

```csharp
// ❌ ReturnEvents.cs (Returns BC anti-pattern - DO NOT REPLICATE)
namespace Returns.Returns;

public sealed record ReturnRequested(...);
public sealed record ReturnApproved(...);
public sealed record ReturnDenied(...);
public sealed record ReturnReceived(...);
public sealed record InspectionStarted(...);
public sealed record InspectionPassed(...);
public sealed record InspectionFailed(...);
public sealed record InspectionMixed(...);
public sealed record ReturnExpired(...);
public sealed record ExchangeApproved(...);
public sealed record ExchangeDenied(...);
// ... 15+ events in one file
```

```csharp
// ❌ ReturnValidators.cs (Returns BC anti-pattern - DO NOT REPLICATE)
namespace Returns.Returns;

public class RequestReturnValidator : AbstractValidator<RequestReturn> { ... }
public class SubmitInspectionValidator : AbstractValidator<SubmitInspection> { ... }
public class DenyReturnValidator : AbstractValidator<DenyReturn> { ... }
```

**Why this is wrong:**

1. **Obscures intent:** A developer reading the folder sees "ReturnCommands.cs" and "ReturnEvents.cs" — they learn the BC has commands and events, but not **what the BC does**.
2. **Poor navigability:** To find the `ApproveReturn` command, you must open `ReturnCommands.cs` and scan a list of 10+ records.
3. **Merge conflicts:** Multiple developers working on different features will conflict in the same file.
4. **Breaks vertical slice principle:** Related types (command, validator, handler) are scattered across 3 files instead of colocated.

---

### ✅ GOOD: Operation-Oriented File Naming

**Name files after the operation or capability they implement:**

```csharp
// ✅ AddItemToCart.cs (Shopping BC - FOLLOW THIS PATTERN)
namespace Shopping.Cart;

// Command definition
public sealed record AddItemToCart(
    Guid CartId,
    string Sku,
    int Quantity)
{
    // Validator as nested class (colocated)
    public class AddItemToCartValidator : AbstractValidator<AddItemToCart>
    {
        public AddItemToCartValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Quantity).GreaterThan(0);
        }
    }
}

// Handler as static class (colocated)
public static class AddItemToCartHandler
{
    public static ProblemDetails Before(AddItemToCart command, Cart? cart) { ... }

    [WolverinePost("/api/carts/{cartId}/items")]
    public static async Task<...> Handle(...) { ... }
}
```

**Why this is correct:**

1. **Intent is clear:** File name tells you exactly what operation it implements.
2. **Navigable:** Type `AddItem` in IDE search, you land in the right file immediately.
3. **No merge conflicts:** Each operation is isolated; developers working on different features never collide.
4. **Vertical slice:** Command + Validator + Handler are in one file; you understand the complete workflow without file hopping.

---

### File Naming Rules

| Type | File Name | Class Name | Location |
|------|-----------|------------|----------|
| **Command** | `{OperationName}.cs` | `{OperationName}` | Same file |
| **Command Validator** | (same file) | `{OperationName}Validator` (nested class) | Same file |
| **Command Handler** | (same file) | `{OperationName}Handler` (static class) | Same file |
| **Query** | `{QueryName}.cs` | `{QueryName}` | Same file |
| **Query Handler** | (same file) | `{QueryName}Handler` (static class) | Same file |
| **Domain Event** | `{EventName}.cs` | `{EventName}` | Separate file (unless has subscription handler) |
| **Aggregate** | `{AggregateName}.cs` | `{AggregateName}` | Separate file |
| **Value Object** | `{Name}.cs` | `{Name}` | Separate file or colocated with aggregate |

---

## Colocation Patterns

CritterSupply uses **two colocation patterns** depending on the persistence technology:

### Pattern 1: Single-File Colocation (Marten-based BCs)

**Used by:** Shopping, Orders, Payments, Inventory, Fulfillment, Product Catalog

**Structure:** Command + Validator (nested) + Handler (static class) in one file.

```csharp
// File: AddItemToCart.cs
namespace Shopping.Cart;

// 1. Command definition
public sealed record AddItemToCart(Guid CartId, string Sku, int Quantity)
{
    // 2. Validator as nested class
    public class AddItemToCartValidator : AbstractValidator<AddItemToCart>
    {
        public AddItemToCartValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Quantity).GreaterThan(0);
        }
    }
}

// 3. Handler as static class
public static class AddItemToCartHandler
{
    public static ProblemDetails Before(AddItemToCart command, Cart? cart) { ... }

    [WolverinePost("/api/carts/{cartId}/items")]
    public static async Task<(Events, OutgoingMessages, ProblemDetails)> Handle(...) { ... }
}
```

**Benefits:**
- **Single location for comprehension** — See the complete workflow without file hopping
- **Tight coupling made explicit** — Commands and handlers are 1:1 by design
- **Onboarding efficiency** — New developers understand "what happens" quickly

---

### Pattern 2: Three-File Colocation (EF Core-based BCs)

**Used by:** Customer Identity, Vendor Identity

**Structure:** Command, Handler, Validator in separate files **within the same feature folder**.

```
VendorIdentity/
  TenantManagement/                    # Feature folder (business capability)
    CreateVendorTenant.cs              # Command
    CreateVendorTenantHandler.cs       # Handler (separate file)
    CreateVendorTenantValidator.cs     # Validator (separate file)
```

**Example:**

```csharp
// File: CreateVendorTenant.cs
namespace VendorIdentity.TenantManagement;

public sealed record CreateVendorTenant(
    string OrganizationName,
    string ContactEmail);
```

```csharp
// File: CreateVendorTenantHandler.cs
namespace VendorIdentity.TenantManagement;

public static class CreateVendorTenantHandler
{
    [WolverinePost("/api/vendor-identity/tenants")]
    public static async Task<(CreationResponse, OutgoingMessages)> Handle(
        CreateVendorTenant command,
        VendorIdentityDbContext db,
        CancellationToken cancellation)
    {
        var tenant = new VendorTenant { ... };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellation);

        var integrationEvent = new VendorTenantCreated(...);
        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationEvent);

        return (new CreationResponse($".../{tenant.Id}"), outgoing);
    }
}
```

```csharp
// File: CreateVendorTenantValidator.cs
namespace VendorIdentity.TenantManagement;

public class CreateVendorTenantValidator : AbstractValidator<CreateVendorTenant>
{
    public CreateVendorTenantValidator()
    {
        RuleFor(x => x.OrganizationName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress();
    }
}
```

**Why separate files for EF Core BCs:**
- Handlers tend to be more complex (async EF Core operations, transaction management)
- Validators may include database-driven rules (e.g., uniqueness checks)
- Separate files improve readability for larger handler logic

**Key point:** Files are **still colocated in the same feature folder** — they are not scattered across `Commands/`, `Handlers/`, `Validators/` folders.

---

### When to Split vs Colocate

| Characteristic | Single File | Three Files |
|----------------|-------------|-------------|
| **Handler complexity** | Simple (pure function returning events) | Complex (async EF Core, multiple DB calls) |
| **Validator complexity** | Simple (property rules only) | Complex (database-driven rules, cross-entity checks) |
| **Handler length** | < 50 lines | > 50 lines |
| **Persistence** | Marten (event sourcing or document store) | EF Core (relational) |

**Both patterns follow the same principle:** All files for a feature live in the same business-capability-named folder.

---

## Event File Patterns

### Domain Events: One Event Per File

Domain events should be in **separate files** (not grouped by type):

```csharp
// ✅ File: ItemAdded.cs
namespace Shopping.Cart;

public sealed record ItemAdded(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    DateTimeOffset AddedAt);
```

```csharp
// ✅ File: ItemRemoved.cs
namespace Shopping.Cart;

public sealed record ItemRemoved(string Sku);
```

**Why separate files:**
- Events are immutable, stable contracts — they change infrequently
- Keeping them separate reduces merge conflicts
- Each event may later grow its own subscription handlers

---

### Events with Subscription Handlers: Colocate

If an event has a subscription handler (e.g., publishing an integration message), include both in the same file:

```csharp
// ✅ File: OrderPlaced.cs — Event + subscription handler colocated
namespace Orders.Events;

// Domain event
public sealed record OrderPlaced(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderLine> Lines,
    DateTimeOffset PlacedAt);

// Marten event subscription handler (colocated)
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

**Rationale:** The subscription handler is tightly coupled to the event; keeping them together makes the integration boundary explicit.

---

## Project Structure

CritterSupply bounded contexts follow one of two project structure patterns:

### Single Project Pattern (Domain + API Combined)

When domain and API are combined in one project:

```
src/
  <BC Name>/
    <ProjectName>/              # SDK: Microsoft.NET.Sdk.Web
      <ProjectName>.csproj
      Program.cs                # Hosting + Wolverine + Marten config
      <FeatureArea>/            # Business capability folders
        <OperationName>.cs      # Command + Handler + Validator
        <AggregateName>.cs      # Aggregate root
        <EventName>.cs          # Domain event
```

**Example:** Customer Identity BC

```
src/
  Customer Identity/
    Customers/                  # SDK: Microsoft.NET.Sdk.Web
      Customers.csproj
      Program.cs
      AddressBook/              # Feature folder
        AddAddress.cs
        UpdateAddress.cs
        Customer.cs
      Authentication/           # Feature folder
        Login.cs
        Logout.cs
      Migrations/               # EF Core migrations
```

**When to use:**
- Simple BCs with straightforward hosting
- No plans to share domain logic across multiple hosts
- Small team or solo development

---

### Split Project Pattern (Domain + API Separate)

When domain and API are separated:

```
src/
  <BC Name>/
    <ProjectName>/              # SDK: Microsoft.NET.Sdk (class library)
      <ProjectName>.csproj
      <FeatureArea>/            # Business capability folders
        <OperationName>.cs      # Command + Handler + Validator
        <AggregateName>.cs      # Aggregate root
        <EventName>.cs          # Domain event
    <ProjectName>.Api/          # SDK: Microsoft.NET.Sdk.Web
      <ProjectName>.Api.csproj
      Program.cs                # Hosting + Wolverine + Marten config
```

**Example:** Orders BC

```
src/
  Orders/
    Orders/                     # Domain project (SDK: class library)
      Orders.csproj
      Placement/                # Feature folder
        PlaceOrder.cs
        PlaceOrderHandler.cs
        Order.cs                # Saga
        OrderDecider.cs
      Checkout/                 # Feature folder
        CompleteCheckout.cs
        Checkout.cs             # Aggregate
    Orders.Api/                 # API project (SDK: Web)
      Orders.Api.csproj
      Program.cs
```

**When to use:**
- Complex hosting requirements (multiple entry points)
- Shared domain logic across multiple hosts (API + background workers)
- Clear separation needed for large teams
- **Most BCs in CritterSupply use this pattern**

---

### Project Naming Conventions

1. **Domain project name** = Base name (e.g., `Orders`, `ProductCatalog`, `Payments`)
2. **API project name** = Base name + `.Api` suffix (e.g., `Orders.Api`, `ProductCatalog.Api`)
3. **Test project name** = API project name + `.IntegrationTests` suffix
4. **IMPORTANT:** When split, tests reference the **API project**, not the domain project
5. **Folder names** can have spaces (e.g., `Customer Identity/`), but project names should not

**Example:**

```
src/Orders/Orders/                              # Domain
src/Orders/Orders.Api/                          # API
tests/Orders/Orders.Api.IntegrationTests/       # Tests (named after API, not domain)
```

---

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
    <Project Path="tests/Orders/Orders.Api.IntegrationTests/..." />
  </Folder>

  <Folder Name="/Shared/">
    <Project Path="src/Shared/Messages.Contracts/Messages.Contracts.csproj" />
  </Folder>
</Solution>
```

### Physical Folder Structure

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
    Orders.Api.IntegrationTests/
    Orders.UnitTests/
```

---

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

**Naming convention:** Each BC has its own folder; integration events are named after the domain event they represent.

Each BC references `Messages.Contracts` to publish/subscribe to integration messages.

---

## Examples: Good vs Anti-Pattern

### Side-by-Side: Shopping Cart (✅ Good)

```
Shopping/
  Cart/                          # ← Business capability
    AddItemToCart.cs             # Command + Validator + Handler (all colocated)
    RemoveItemFromCart.cs        # Command + Validator + Handler
    ChangeItemQuantity.cs        # Command + Validator + Handler
    InitializeCart.cs            # Command + Validator + Handler
    InitiateCheckout.cs          # Command + Validator + Handler
    ClearCart.cs                 # Command + Validator + Handler
    Cart.cs                      # Aggregate root
    CartLineItem.cs              # Value object
    CartStatus.cs                # Enum
    ItemAdded.cs                 # Domain event
    ItemRemoved.cs               # Domain event
    ItemQuantityChanged.cs       # Domain event
    CheckoutInitiated.cs         # Domain event
    CartCleared.cs               # Domain event
    CartAbandoned.cs             # Domain event
```

**What you learn from this structure:**
1. This BC handles shopping cart operations (Add, Remove, Change, Initialize, Checkout, Clear)
2. Each operation is self-contained (command, validator, handler colocated)
3. The Cart aggregate is the stream root
4. 6 domain events track state transitions

**Developer experience:**
- Type `AddItem` in IDE → lands in `AddItemToCart.cs`
- Open file → see command, validation rules, handler logic in one place
- No file hopping to understand the workflow

---

### Side-by-Side: Returns BC (❌ Anti-Pattern - DO NOT REPLICATE)

```
Returns/
  Returns/                       # ← Folder named after BC, not feature
    ReturnCommands.cs            # ❌ 10+ commands grouped by type
    ReturnEvents.cs              # ❌ 15+ events grouped by type
    ReturnValidators.cs          # ❌ Multiple validators grouped by type
    ReturnCommandHandlers.cs     # ❌ Multiple handlers grouped by type
    ReturnQueries.cs             # ❌ Queries grouped by type
    Return.cs                    # Aggregate root
    ReturnType.cs                # Enum
    ReturnReason.cs              # Enum
```

**What you learn from this structure:**
1. This BC has commands, events, validators, handlers, and queries
2. ❓ **What does it DO?** You have to open files and read to find out

**Developer experience:**
- Type `ApproveReturn` in IDE → lands in `ReturnCommands.cs`
- Open file → scroll through 10+ records to find `ApproveReturn`
- Open `ReturnValidators.cs` → scroll to find `ApproveReturnValidator`
- Open `ReturnCommandHandlers.cs` → scroll to find `ApproveReturnHandler`
- Now you've opened 3 files to understand one operation

---

### How Returns BC Should Be Organized (Hypothetical Refactoring)

If Returns BC were refactored to follow vertical slice principles:

```
Returns/
  Requests/                      # ← Business capability
    RequestReturn.cs             # Command + Validator + Handler
    ApproveReturn.cs             # Command + Validator + Handler
    DenyReturn.cs                # Command + Validator + Handler
    ExpireReturn.cs              # Command + Validator + Handler
    ReturnRequested.cs           # Domain event
    ReturnApproved.cs            # Domain event
    ReturnDenied.cs              # Domain event
    ReturnExpired.cs             # Domain event
  Inspections/                   # ← Business capability
    ReceiveReturn.cs             # Command + Validator + Handler
    StartInspection.cs           # Command + Validator + Handler
    SubmitInspection.cs          # Command + Validator + Handler
    ReturnReceived.cs            # Domain event
    InspectionStarted.cs         # Domain event
    InspectionPassed.cs          # Domain event
    InspectionFailed.cs          # Domain event
    InspectionMixed.cs           # Domain event
  Exchanges/                     # ← Business capability
    ApproveExchange.cs           # Command + Validator + Handler
    DenyExchange.cs              # Command + Validator + Handler
    ShipReplacementItem.cs       # Command + Validator + Handler
    ExchangeApproved.cs          # Domain event
    ExchangeDenied.cs            # Domain event
    ReplacementShipped.cs        # Domain event
  Return.cs                      # Aggregate root
  ReturnType.cs                  # Enum
  ReturnReason.cs                # Enum
```

**Now the structure tells a story:**
1. This BC handles return requests, inspections, and exchanges
2. Each feature area is self-contained
3. Related files are colocated

---

### Side-by-Side: Vendor Identity (✅ Good - After ADR 0023)

**Before ADR 0023 (Cycle 22 - Technical Folders):**

```
VendorIdentity/
  Commands/                      # ❌ Technical folder
    CreateVendorTenant.cs
    InviteVendorUser.cs
    AcceptInvitation.cs
    DeactivateUser.cs
  Data/                          # ❌ Technical folder
    VendorIdentityDbContext.cs
  Entities/                      # ❌ Technical folder
    VendorTenant.cs
    VendorUser.cs
    VendorUserInvitation.cs
```

**After ADR 0023 (Feature-Based):**

```
VendorIdentity/
  TenantManagement/              # ✅ Business capability
    CreateVendorTenant.cs
    CreateVendorTenantHandler.cs
    CreateVendorTenantValidator.cs
    UpdateVendorTenant.cs
    SuspendVendorTenant.cs
    ReinstateVendorTenant.cs
    TerminateVendorTenant.cs
  UserInvitations/               # ✅ Business capability
    InviteVendorUser.cs
    InviteVendorUserHandler.cs
    InviteVendorUserValidator.cs
    AcceptInvitation.cs
    ResendInvitation.cs
    RevokeInvitation.cs
  UserManagement/                # ✅ Business capability
    DeactivateVendorUser.cs
    ReactivateVendorUser.cs
    ChangeVendorUserRole.cs
  Identity/                      # ✅ Infrastructure (not "Data" or "Entities")
    VendorIdentityDbContext.cs
    VendorTenant.cs
    VendorUser.cs
    VendorUserInvitation.cs
    VendorTenantStatus.cs
    VendorUserStatus.cs
    VendorRole.cs
    InvitationStatus.cs
```

**Improvement:**
- Folder names now describe business capabilities (TenantManagement, UserInvitations, UserManagement)
- All files for a feature are colocated in the same folder
- Infrastructure folder named `Identity/` (not `Data/` or `Entities/`) — signals shared persistence concern

---

## Lessons Learned

### L1 — Feature-Based Organization Requires Discipline (ADR 0023, Cycle 22)

**Context:** Vendor Identity BC was initially organized with technical folders (`Commands/`, `Data/`, `Entities/`) during Cycle 22 Phase 1. This worked but had several drawbacks:
- Related files (command, handler, validator) were scattered across multiple folders
- Developers had to navigate 3-4 folders to understand a single feature
- Inconsistent with CritterSupply's Marten-based BCs (Shopping, Orders, Payments, etc.)

**Decision:** Refactored to feature-based organization in Cycle 22 Phase 2.

**Outcome:**
- ✅ Faster feature development — all related files in one place
- ✅ Easier code reviews — feature changes are contained
- ✅ Better alignment with vertical slice architecture
- ✅ Consistent with existing Marten-based BCs

**Reference:** See [ADR 0023: Feature-Based Organization for EF Core BCs](../decisions/0023-feature-based-organization-for-ef-core-bcs.md)

---

### L2 — Technically-Oriented Naming is a Persistent Anti-Pattern

**Observation:** The Returns BC (Cycles 25-27) uses technical file naming (`ReturnCommands.cs`, `ReturnEvents.cs`, `ReturnValidators.cs`) despite multiple cycles of development and CritterSupply having established patterns in other BCs.

**Why this persists:**
1. **Familiar to traditional .NET developers** — layered architecture is taught as the default
2. **Seems efficient initially** — "I'll just add one more command to `ReturnCommands.cs`"
3. **Not explicitly warned against** — until this document refresh, no skill file called it out as an anti-pattern

**Impact:**
- Harder to navigate (10+ types in one file)
- Merge conflicts when multiple developers work on the BC
- Inconsistent with CritterSupply's established patterns
- Obscures business intent

**Recommendation for future refactoring:** Returns BC should be refactored to use feature folders (`Requests/`, `Inspections/`, `Exchanges/`) with operation-named files.

---

### L3 — Infrastructure Folders Should Be Named After Their Purpose, Not "Data" or "Entities"

**Context:** Early EF Core BCs used `Data/` and `Entities/` folders. These names are technical, not semantic.

**Better naming:**
- `Identity/` (Customer Identity, Vendor Identity) — signals shared identity concerns
- `Persistence/` (if needed for complex multi-DB scenarios)
- `Schema/` (if documenting PostgreSQL schema definitions)

**Why this matters:** The folder name should tell you **why** it exists, not **what** it contains.

---

### L4 — AI Agents Default to Technical Organization Without Explicit Guidance

**Observation:** When asked to scaffold a new BC without referencing this document, AI agents default to:
```
Commands/
Events/
Queries/
Handlers/
Data/
```

**Root cause:** This is the most common pattern in .NET tutorials, blog posts, and training materials.

**Solution:** This document now explicitly warns against this pattern and provides clear examples of CritterSupply's feature-oriented organization.

**For AI agents:** If you are scaffolding a new BC or feature in CritterSupply, **always organize by business capability, not technical layer**. Read this document before generating folder structures or file names.

---

### L5 — Exceptions to the Rule: Infrastructure Concerns

Not everything belongs in a feature folder. Some code genuinely is infrastructure:

**Acceptable technical folders:**
- `Identity/` (EF Core DbContext and entities shared across features)
- `Migrations/` (EF Core migrations)
- `Configuration/` (if you have complex Marten or EF Core setup split into multiple files)

**Rule of thumb:** If the code is **shared across multiple features** and has no single business capability owner, it belongs in an infrastructure folder. But name it semantically (`Identity/`), not generically (`Data/`, `Entities/`).

---

## Appendix: Exemplary Implementations

Use these real CritterSupply examples as templates when creating new features or BCs:

### Marten-Based BC (Single-File Colocation)

**Shopping BC — Cart Feature:**
- **File:** `src/Shopping/Shopping/Cart/AddItemToCart.cs`
- **Pattern:** Command + Validator (nested) + Handler (static class) in one file
- **Demonstrates:** Compound handler lifecycle (`Before`, `Handle`), Marten `[WriteAggregate]`, integration message publishing

**Orders BC — Placement Feature:**
- **File:** `src/Orders/Orders/Placement/PlaceOrder.cs`
- **Pattern:** Command + Handler returning tuple `(Order, Event)` for saga initialization
- **Demonstrates:** Saga initialization pattern, decider pattern, integration with external pricing

---

### EF Core-Based BC (Three-File Colocation)

**Vendor Identity BC — TenantManagement Feature:**
- **Files:**
  - `src/Vendor Identity/VendorIdentity/TenantManagement/CreateVendorTenant.cs`
  - `src/Vendor Identity/VendorIdentity/TenantManagement/CreateVendorTenantHandler.cs`
  - `src/Vendor Identity/VendorIdentity/TenantManagement/CreateVendorTenantValidator.cs`
- **Pattern:** Separate files for command, handler, validator (all in same feature folder)
- **Demonstrates:** EF Core async operations, integration event publishing, DbContext usage

**Customer Identity BC — AddressBook Feature:**
- **Files:** `src/Customer Identity/Customers/AddressBook/AddAddress.cs`
- **Pattern:** Three-file EF Core pattern
- **Demonstrates:** Address book CRUD with EF Core, FluentValidation rules

---

### Event-Sourced Aggregate

**Orders BC — Order Saga:**
- **File:** `src/Orders/Orders/Placement/Order.cs`
- **Pattern:** Event-sourced saga with `Create()` and `Apply()` methods
- **Demonstrates:** Saga lifecycle, message correlation, state machine, decider pattern

---

### Document Store (Non-Event-Sourced)

**Product Catalog BC — Product Management:**
- **File:** `src/Product Catalog/ProductCatalog/Products/Product.cs`
- **Pattern:** Plain Marten document with factory methods
- **Demonstrates:** Document store CRUD, soft delete, status field state machine

---

### BFF (Backend-for-Frontend)

**Customer Experience BC (Storefront):**
- **Structure:**
  ```
  Storefront/                     # Domain project
    Clients/                      # HTTP client interfaces
    Composition/                  # View models
    Notifications/                # Integration message handlers
  Storefront.Api/                 # API project
    Queries/                      # HTTP endpoints
    Clients/                      # HTTP client implementations
    StorefrontHub.cs              # SignalR hub
  ```
- **Pattern:** Domain/API split, view composition, real-time SignalR updates
- **Demonstrates:** BFF pattern, multi-BC composition, SignalR real-time communication

**Reference:** See [docs/skills/bff-realtime-patterns.md](./bff-realtime-patterns.md)

---

## Related Documentation

- [ADR 0023: Feature-Based Organization for EF Core BCs](../decisions/0023-feature-based-organization-for-ef-core-bcs.md)
- [Wolverine Message Handlers](./wolverine-message-handlers.md)
- [Marten Event Sourcing](./marten-event-sourcing.md)
- [Marten Document Store](./marten-document-store.md)
- [EF Core + Wolverine Integration](./efcore-wolverine-integration.md)
- [BFF Real-Time Patterns](./bff-realtime-patterns.md)
- [Modern C# Coding Standards](./modern-csharp-coding-standards.md)
