# ADR 0022: Wolverine HTTP Route Parameter Binding Patterns

**Status:** ✅ Accepted

**Date:** 2026-03-09

**Context:**

During VendorIdentity BC integration testing (Cycle 22), we encountered a subtle but critical issue with Wolverine HTTP endpoint route parameter binding. Tests were failing with validation errors showing `Guid.Empty` for route parameters like `{tenantId}`, even though the route was correctly defined and the command property name matched the route parameter.

Initial attempts to fix the issue included:
1. Changing route parameter casing from `{tenantId}` to `{TenantId}` (didn't work)
2. Simplifying command property names to match route parameters exactly (didn't work)

The root cause was discovered by examining CustomerIdentity's `AddAddress` endpoint pattern: when using a single command parameter, **Wolverine requires the entire command object (including route parameters) to be present in the JSON request body**.

Further investigation revealed that Wolverine actually supports **two distinct patterns** for route parameter binding:

1. **Pattern A: Command-Embedded Route Parameters** (AddAddress, InviteVendorUser, AddItemToCart)
   - Route parameter is a property of the command record
   - Entire command serialized in JSON body
   - FluentValidation applies to all properties including route parameters

2. **Pattern B: Separate Route Parameters** (CancelOrderEndpoint)
   - Route parameter is a separate handler parameter with `[FromBody]` attribute for the request body
   - Route parameter bound from URL, body bound from JSON
   - Allows pre-body-deserialization logic (e.g., authorization checks)

**Decision:**

CritterSupply uses **Pattern A (Command-Embedded Route Parameters)** as the default for most endpoints because it:
- Enables FluentValidation for route parameters (existence checks, format validation)
- Maintains complete command serialization benefits (logging, auditing, replay)
- Works seamlessly with Wolverine's compound handler lifecycle (`Before`, `Validate`, `Load`, `Handle`)
- Provides consistent pattern across Shopping, Orders, Fulfillment, CustomerIdentity, VendorIdentity BCs

**Pattern B (Separate Route Parameters)** is used sparingly for endpoints that need to:
- Perform authorization checks before deserializing the request body
- Load entities based on route parameters before validating the body
- Return early based on route parameter validation without processing the body

**Example: Orders BC `CancelOrderEndpoint` uses Pattern B** because it needs to load the order and check authorization before processing the cancellation reason.

## Pattern A: Command-Embedded Route Parameters (Default)

**Use this pattern for most endpoints** - route parameters are command properties.

```csharp
// Command record
public sealed record InviteVendorUser(
    Guid TenantId,  // Route parameter (from URL + JSON body)
    string Email,   // Body parameter
    ...
);

// Validator - validates ALL properties including route parameters
public class InviteVendorUserValidator : AbstractValidator<InviteVendorUser>
{
    public InviteVendorUserValidator(VendorIdentityDbContext db)
    {
        RuleFor(x => x.TenantId)
            .NotEmpty()
            .MustAsync(async (tenantId, ct) =>
                await db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            .WithMessage("Tenant does not exist");
    }
}

// Handler
[WolverinePost("/api/vendor-identity/tenants/{tenantId}/users/invite")]
public static async Task<CreationResponse> Handle(InviteVendorUser command, ...)
{
    // command.TenantId is already validated by FluentValidation
    // command is fully populated from JSON body
}

// Test (Alba)
var command = new InviteVendorUser(
    tenantId,      // Route parameter value included in command
    "user@example.com",
    ...
);

var result = await _fixture.Host.Scenario(x =>
{
    x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/invite");
    //         ^^^^^^^^^ Full command object (including route parameter)
    x.StatusCodeShouldBe(201);
});
```

**Key Points:**
- ✅ Route parameter (`tenantId`) MUST be included in the JSON body
- ✅ FluentValidation runs on the entire command, including route parameters
- ✅ Compound handler lifecycle (`Before`, `Validate`, `Load`, `Handle`) works seamlessly

---

## Pattern B: Separate Route Parameters (Rare)

**Use this pattern sparingly** - when you need to validate/authorize based on route parameters BEFORE deserializing the body.

```csharp
// Request record (excludes route parameter)
public sealed record CancelOrderRequest(string Reason);

// Handler with separate parameters
[WolverinePost("/api/orders/{orderId}/cancel")]
public static async Task<IResult> Handle(
    Guid orderId,                              // Route parameter (from URL)
    [FromBody] CancelOrderRequest request,     // Body parameter (from JSON)
    IQuerySession querySession,
    IMessageBus bus)
{
    // Validate request body manually (no FluentValidation)
    if (string.IsNullOrWhiteSpace(request.Reason))
        return Results.BadRequest(new ProblemDetails { Detail = "Reason required" });

    // Load entity based on route parameter
    var order = await querySession.LoadAsync<Order>(orderId);
    if (order is null)
        return Results.NotFound();

    // Authorize based on loaded entity
    if (!OrderDecider.CanBeCancelled(order.Status))
        return Results.Conflict(new ProblemDetails { Detail = "Cannot cancel" });

    // Publish command to message bus
    await bus.PublishAsync(new CancelOrder(orderId, request.Reason));
    return Results.Accepted();
}

// Test (Alba)
var request = new CancelOrderRequest("Customer requested cancellation");

var result = await _fixture.Host.Scenario(x =>
{
    x.Post.Json(request).ToUrl($"/api/orders/{orderId}/cancel");
    //         ^^^^^^^ Only body (route parameter NOT in JSON)
    x.StatusCodeShouldBe(202);
});
```

**Key Points:**
- ❌ Route parameter (`orderId`) is NOT in the JSON body
- ❌ FluentValidation does NOT run (manual validation required)
- ✅ Allows early return before deserializing body (authorization, existence checks)
- ✅ Useful when route parameter determines authorization context

---

## Rationale: Why Pattern A is Default

**Pattern A (Command-Embedded)** is the default because:

1. **FluentValidation Integration** - Route parameters get the same validation infrastructure as body fields
2. **Command Serialization** - Entire request is serializable for logging, auditing, and replay
3. **Compound Handler Lifecycle** - `Before`, `Validate`, `Load`, `Handle` all work seamlessly
4. **Consistency** - All Shopping, Fulfillment, CustomerIdentity endpoints use this pattern
5. **Type Safety** - Route parameters are strongly-typed properties, not loosely-typed handler parameters

**Pattern B (Separate Parameters)** is only needed when:
- Authorization checks must happen before body deserialization
- Early return logic based on route parameters (reduce unnecessary processing)
- Route parameters determine security context (multi-tenant scenarios)

## Consequences

### Pattern A Consequences

**Positive:**
- ✅ FluentValidation applies to route parameters (existence checks, format validation)
- ✅ Complete command serialization for logging/auditing
- ✅ Compound handler lifecycle works seamlessly
- ✅ Consistent pattern across most BCs

**Negative:**
- ❌ Redundancy - Route parameter appears in both URL and JSON body
- ❌ Non-standard - Differs from typical REST API patterns
- ❌ Learning curve for developers familiar with ASP.NET Core

### Pattern B Consequences

**Positive:**
- ✅ Early return before body deserialization (performance)
- ✅ Authorization checks before processing body
- ✅ Familiar pattern for ASP.NET Core developers

**Negative:**
- ❌ No FluentValidation - manual validation required
- ❌ No compound handler lifecycle - manual `Before` logic
- ❌ Inconsistent with most CritterSupply endpoints

---

## When to Use Each Pattern

| Scenario | Pattern | Reason |
|----------|---------|--------|
| Standard CRUD operation | **Pattern A** | FluentValidation, serialization, compound lifecycle |
| Command with business logic | **Pattern A** | FluentValidation, compound lifecycle (`Before`, `Validate`) |
| Multi-tenant route parameters | **Pattern A** | FluentValidation for tenant existence checks |
| Authorization before body processing | **Pattern B** | Early return if unauthorized (avoid deserialization) |
| Expensive body deserialization | **Pattern B** | Validate route parameters first to fail fast |
| Public API endpoints | **Pattern B** | Familiar REST pattern for external consumers |

**Default Rule:** Use Pattern A unless you have a specific reason to use Pattern B.

---

## Common Mistake: Anonymous Objects in Tests

```csharp
// ❌ INCORRECT - Anonymous object missing route parameter (Pattern A)
var request = new
{
    Email = "user@example.com",
    FirstName = "Jane"
    // Missing TenantId - will be Guid.Empty!
};
x.Post.Json(request).ToUrl($"/api/tenants/{tenantId}/users");
// Result: FluentValidation fails with "Tenant ID is required"

// ✅ CORRECT - Full command with route parameter (Pattern A)
var command = new InviteUser(
    TenantId: tenantId,  // Explicitly included
    Email: "user@example.com",
    FirstName: "Jane"
);
x.Post.Json(command).ToUrl($"/api/tenants/{tenantId}/users");

// ✅ CORRECT - Separate body object (Pattern B with [FromBody])
var request = new CancelOrderRequest("Customer requested");
x.Post.Json(request).ToUrl($"/api/orders/{orderId}/cancel");
// Note: orderId NOT in JSON body
```

---

## References

### Pattern A Examples (Command-Embedded Route Parameters)
- **CustomerIdentity BC**: `AddAddress` (`src/Customer Identity/Customers/AddressBook/AddAddress.cs`)
- **VendorIdentity BC**: `InviteVendorUser` (`src/Vendor Identity/VendorIdentity/UserInvitations/InviteVendorUserHandler.cs`)
- **Shopping BC**: `AddItemToCart` (`src/Shopping/Shopping/Cart/AddItemToCart.cs`)
- **Fulfillment BC**: `DispatchShipment` (`src/Fulfillment/Fulfillment/Shipments/DispatchShipment.cs`)
- **Orders BC**: `CompleteCheckout` (`src/Orders/Orders/Checkout/CompleteCheckout.cs`)

### Pattern B Examples (Separate Route Parameters)
- **Orders BC**: `CancelOrderEndpoint` (`src/Orders/Orders.Api/Placement/CancelOrderEndpoint.cs`)

### Test References
- **CustomerIdentity Tests**: `tests/Customer Identity/CustomerIdentity.Api.IntegrationTests/AddressBookTests.cs`
- **VendorIdentity Tests**: `tests/Vendor Identity/VendorIdentity.Api.IntegrationTests/UserInvitationTests.cs`

### Related Documentation
- Cycle 22 Phase 1: VendorIdentity BC integration testing
- `docs/skills/wolverine-message-handlers.md` - HTTP endpoint patterns
- `docs/skills/efcore-wolverine-integration.md` - EF Core with Wolverine HTTP endpoints
