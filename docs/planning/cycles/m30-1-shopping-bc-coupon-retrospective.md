# M30.1 Shopping BC Coupon Integration - Session Retrospective

**Date:** 2026-03-15
**Milestone:** M30.1 (Shopping BC Coupon Integration)
**Sessions:** 2 debugging sessions (timeout recovery + investigation)
**Status:** 🟡 Partial Complete - RemoveCouponFromCart fixed, validation tests still failing

---

## Overview

These sessions focused on debugging Shopping BC integration test failures after implementing coupon support (ApplyCouponToCart, RemoveCouponFromCart handlers). The work revealed important patterns about Wolverine handler discovery and Alba test fixture DI configuration.

---

## What We Accomplished

### ✅ Session 1-2: RemoveCouponFromCart Handler Discovery Fix

**Problem:** Test error: `Could not determine any valid subscribers or local handlers for message type Shopping.Cart.RemoveCouponFromCart`

**Root Cause:** The `RemoveCouponFromCart` handler was trying to serve two purposes in one class:
1. Command handler (for internal use via `ExecuteAndWaitAsync`)
2. HTTP DELETE endpoint handler (for REST API)

This created confusion for Wolverine's handler discovery system because:
- The command record `RemoveCouponFromCart(Guid CartId)` existed
- But the handler's `Before()` method took `Guid cartId` (route parameter) instead of the command
- The `Handle()` method also took `Guid cartId` (route parameter)
- Wolverine couldn't match the command record to a handler that accepted it

**Solution:** Split into two separate handler classes following the `ClearCart` pattern:

```csharp
// Command handler for internal use (tests, sagas, etc.)
public static class RemoveCouponFromCartHandler
{
    public static ProblemDetails Before(
        RemoveCouponFromCart command,  // ✅ Takes command record
        Cart? cart)
    {
        // validation...
        return WolverineContinue.NoProblems;
    }

    public static (CouponRemoved, OutgoingMessages) Handle(
        RemoveCouponFromCart command,  // ✅ Takes command record
        [WriteAggregate] Cart cart)
    {
        // returns event + integration message
    }
}

// HTTP DELETE endpoint in separate class
public static class RemoveCouponFromCartHttpEndpoint
{
    public static ProblemDetails Before(
        Guid cartId,  // ✅ Takes route parameter
        Cart? cart)
    {
        // validation...
        return WolverineContinue.NoProblems;
    }

    [WolverineDelete("/api/carts/{cartId}/apply-coupon")]
    public static (Events, OutgoingMessages) Handle(
        Guid cartId,  // ✅ Takes route parameter
        [WriteAggregate] Cart cart)
    {
        // returns event array + integration message
    }
}
```

**Key Pattern:** When a message needs both command handling AND HTTP endpoint exposure:
- Create TWO handler classes (not one)
- Command handler: accepts command record, has full validation, returns strongly-typed events
- HTTP endpoint handler: accepts route parameters, has route-level validation, returns `Events` collection

**Verification:** Test `ApplyCoupon_ThenRemove_ThenReapply_Succeeds` now passes ✅

**Files Changed:**
- `src/Shopping/Shopping/Cart/RemoveCouponFromCart.cs` - Split into two classes
- Reference pattern: `src/Shopping/Shopping/Cart/ClearCart.cs` lines 22-89

---

## What We're Still Debugging

### 🟡 Session 2: Stub Client DI Replacement Issue

**Problem:** Tests `ApplyCoupon_InvalidCoupon_Returns400WithErrorMessage` and `ApplyCoupon_NonExistentCoupon_Returns400` return 204 (success) instead of 400 (validation error).

**Expected Flow:**
1. Test calls `_fixture.StubPromotionsClient.SetInvalidCoupon("EXPIRED20", "Coupon has expired")`
2. Handler calls `await promotionsClient.ValidateCouponAsync(...)`
3. Stub returns `IsValid = false, Reason = "Coupon has expired"`
4. Handler checks `if (!validation.IsValid)` and returns `ProblemDetails { Status = 400 }`
5. Test expects 400 status with "expired" in body

**Actual Result:** Test gets 204 (No Content), suggesting handler succeeds despite invalid coupon

**Hypothesis:** The stub `IPromotionsClient` is not being used. Either:
1. The real `PromotionsClient` (HttpClient-based) is being resolved and making actual HTTP calls (which would fail/timeout)
2. OR the Alba/WebApplicationFactory DI service replacement isn't working correctly

**What We Tried:**

#### Attempt 1: Remove + Add Pattern (Failed)
```csharp
var pricingDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPricingClient));
if (pricingDescriptor != null) services.Remove(pricingDescriptor);
services.AddSingleton<IPricingClient>(StubPricingClient);
```
**Why it failed:** `ConfigureServices` callback runs BEFORE `Program.cs` completes, so the descriptor doesn't exist yet to remove.

#### Attempt 2: Replace with Single ConfigureServices (Failed)
```csharp
builder.ConfigureServices(services =>
{
    services.Replace(ServiceDescriptor.Singleton<IPromotionsClient>(StubPromotionsClient));
});
```
**Why it failed:** Same timing issue - Replace called before Program.cs adds the scoped registration.

#### Attempt 3: Replace with Separate ConfigureServices (Current, Still Failing)
```csharp
Host = await AlbaHost.For<Program>(builder =>
{
    builder.ConfigureServices(services =>
    {
        // First callback: Marten + disable transports
    });

    builder.ConfigureServices(services =>
    {
        // Second callback: Replace clients AFTER Program.cs
        services.Replace(ServiceDescriptor.Singleton<IPromotionsClient>(StubPromotionsClient));
    });
});
```
**Why it's still failing:** Unknown - needs deeper investigation

**Program.cs Registration (Line 139):**
```csharp
builder.Services.AddScoped<Shopping.Clients.IPromotionsClient, Shopping.Api.Clients.PromotionsClient>();
```

**Files Changed:**
- `tests/Shopping/Shopping.Api.IntegrationTests/TestFixture.cs` - Added `Microsoft.Extensions.DependencyInjection.Extensions` using, tried multiple DI replacement approaches

---

## Lessons Learned

### 1. Wolverine Handler Discovery with Dual-Purpose Messages

**Pattern:** Commands that are BOTH internal messages AND HTTP endpoints need separate handler classes.

**Why:** Wolverine's handler discovery system matches messages to handlers by method signature. When you have:
- A command record: `RemoveCouponFromCart(Guid CartId)`
- A handler method: `Handle(Guid cartId, [WriteAggregate] Cart cart)`

Wolverine sees the command type but can't find a handler that accepts it (the handler accepts `Guid`, not `RemoveCouponFromCart`).

**Solution Pattern (from ClearCart.cs):**
1. **Command Handler Class** - Accepts command record, used for programmatic invocation
2. **HTTP Endpoint Handler Class** - Accepts route parameters, decorated with `[WolverineDelete]` etc.

**When to Use:**
- When tests/sagas need to invoke the command programmatically via `ExecuteAndWaitAsync(command)`
- AND you also need an HTTP endpoint for REST API access

**When NOT to Use:**
- If you only need HTTP endpoint → use single handler class with route parameters (like `RemoveItemFromCart`)
- If you only need command handler → use single handler class with command record

**Code References:**
- Good example: `src/Shopping/Shopping/Cart/ClearCart.cs` (lines 22-89)
- Fixed example: `src/Shopping/Shopping/Cart/RemoveCouponFromCart.cs` (after commit a42a04b)
- HTTP-only example: `src/Shopping/Shopping/Cart/RemoveItemFromCart.cs`

### 2. Alba/WebApplicationFactory DI Service Replacement Timing

**Problem:** When using `AlbaHost.For<Program>()`, the order of DI registrations matters:
1. `builder.ConfigureServices()` callback runs
2. `Program.cs` runs and registers services
3. Application starts

**Challenge:** If `Program.cs` registers a service (e.g., `AddScoped<IPromotionsClient, PromotionsClient>`), and you try to replace it in `ConfigureServices()`, your replacement happens BEFORE the real registration, so the real one wins.

**Attempted Solutions:**
- ❌ Remove existing descriptor → descriptor doesn't exist yet
- ❌ Replace in single ConfigureServices → still gets overwritten
- ❌ Replace in second ConfigureServices → still failing (needs investigation)

**What We Still Need to Try:**
1. Check if Alba has a post-startup hook for DI replacement
2. Verify if `WebApplicationFactory<Program>` directly (not AlbaHost) has better timing control
3. Look at other BC test fixtures (Orders, Pricing) to see if they have this pattern working
4. Add logging to see which `IPromotionsClient` implementation is actually resolved
5. Check if HttpClient is silently failing/timing out when trying to connect to non-existent Promotions BC

**Files to Investigate:**
- `tests/Orders/Orders.Api.IntegrationTests/TestFixture.cs`
- `tests/Pricing/Pricing.Api.IntegrationTests/TestFixture.cs`
- Alba documentation on service replacement
- WebApplicationFactory documentation on post-startup hooks

### 3. Test Debugging Strategies

**What Worked:**
- ✅ Running single test with `--filter "FullyQualifiedName~TestName"` for fast iteration
- ✅ Using `--no-build` after first build to speed up test runs
- ✅ Reading similar working patterns (ClearCart) to understand correct structure
- ✅ Checking git history to see when code was added/changed
- ✅ Splitting complex problems into smaller pieces (handler discovery vs validation)

**What Didn't Work:**
- ❌ Assuming DI replacement works the same as in plain ASP.NET Core
- ❌ Trying multiple DI approaches without understanding the timing/lifecycle

**What We Should Try Next:**
- Add `Console.WriteLine` or logging in handler to see which client implementation is used
- Check if stub's `ValidateCouponAsync` is actually being called
- Write a minimal test that just resolves `IPromotionsClient` from DI and checks its type

---

## Technical Debt / Future Work

### High Priority (Blocking M30.1)
1. **Fix stub client DI replacement** - Tests can't validate error paths until this works
2. **Document the working pattern** - Once fixed, add to skills file with clear examples
3. **Verify all coupon operation tests pass** - Currently 3 failing, need all 11 passing

### Medium Priority (Post-M30.1)
1. **Extract Alba test fixture pattern** - Create shared base class if multiple BCs need stub replacement
2. **Add test for DI resolution** - Test that stubs are actually being used (defensive programming)
3. **Review other BC test fixtures** - Ensure consistent patterns across all integration tests

### Low Priority (Nice to Have)
1. **Consider Testcontainers for Promotions BC** - Start real Promotions.Api in container for integration tests
2. **Add Alba documentation links** - Reference Alba docs for service replacement patterns
3. **Create skill file for Alba testing** - Document Alba-specific patterns separately from TestContainers

---

## Next Steps

### Immediate (This Session)
1. ✅ Document lessons learned in this retrospective
2. 🔄 Investigate why stub client replacement isn't working
3. ⏳ Get all coupon operation tests passing
4. ⏳ Commit working test fixture changes
5. ⏳ Update skills file with handler pattern (if time permits)

### After Tests Pass
1. Run full Shopping integration test suite
2. Update M30.1 implementation status document
3. Create commit with "(M30.1) Complete Shopping BC coupon integration tests"
4. Begin next M30.1 work item (if any) or mark milestone complete

---

## Code Quality Notes

**Good Patterns We Used:**
- ✅ Followed existing ClearCart pattern for handler split
- ✅ Maintained validation in both command and HTTP endpoint handlers
- ✅ Kept event and integration message publishing consistent
- ✅ Used proper `ProblemDetails` return for validation errors

**Anti-Patterns We Avoided:**
- ❌ Didn't mix command and HTTP endpoint logic in one handler class
- ❌ Didn't remove existing validation when splitting handlers
- ❌ Didn't change handler signatures beyond what was necessary

**Areas for Improvement:**
- Test fixture DI replacement pattern needs standardization
- Need better documentation of Alba lifecycle hooks
- Should add defensive tests that verify stub usage

---

## Resources Referenced

**Skill Files:**
- `docs/skills/wolverine-message-handlers.md` - Handler patterns and return types
- `docs/skills/marten-event-sourcing.md` - Event sourcing patterns
- `docs/skills/testcontainers-integration-tests.md` - Test infrastructure patterns

**Code Examples:**
- `src/Shopping/Shopping/Cart/ClearCart.cs` - Dual handler pattern
- `src/Shopping/Shopping/Cart/RemoveItemFromCart.cs` - HTTP-only pattern
- `src/Shopping/Shopping/Cart/AddItemToCart.cs` - Validation in async handlers

**Related Issues:**
- None (this is new work for M30.1)

---

## Summary

**Progress Made:** 1 of 3 failing tests fixed (RemoveCouponFromCart handler discovery)
**Tests Status:** 8 passing, 3 failing (validation tests)
**Blocker:** Stub client DI replacement not working in Alba test fixture
**Confidence:** High for handler pattern fix, Low for DI issue (needs more investigation)

**Key Takeaway:** Wolverine handler discovery requires careful attention to method signatures and handler class organization. When a message serves both command and HTTP purposes, use separate handler classes to avoid discovery conflicts.

---

*Session ended due to timeout (59m). Investigation will continue in next session.*
