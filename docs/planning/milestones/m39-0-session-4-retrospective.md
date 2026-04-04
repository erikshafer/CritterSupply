# M39.0 Session 4 Retrospective

**Date:** 2026-04-04
**Milestone:** M39.0 — Critter Stack Idiom Refresh: Orders Checkout Outbox Risk Fix
**Session:** Session 4 — Orders Checkout `[WriteAggregate]` Spike + Redundant `SaveChangesAsync` Removal

---

## Baseline

- Build: 0 errors, 19 warnings (unchanged from S3 close)
- Orders integration tests: 48/48 passing
- Four Checkout handlers using "Direct Implementation" workaround (`FetchForWriting` + `AppendOne` + `SaveChangesAsync`)

---

## Items Completed

| Item | Description |
|------|-------------|
| S4a | Spike `[WriteAggregate]` on `CompleteCheckout` — confirmed working |
| S4b | Full refactor of `CompleteCheckout` to `Before()` + `Handle()` with `[WriteAggregate]`, returning `(IResult, Events, OutgoingMessages)` |
| S4c | Removed redundant `await session.SaveChangesAsync(ct)` from `ProvideShippingAddressHandler`, `SelectShippingMethodHandler`, `ProvidePaymentMethodHandler` |

---

## S4a + S4b: `[WriteAggregate]` Spike — **SUCCEEDED**

### What Was Attempted

The spike replaced `CompleteCheckoutHandler` with a compound handler:
- `Before(Checkout? checkout)` — validation (null check, IsCompleted, ShippingAddress, ShippingMethod, PaymentMethodToken)
- `Handle(Guid checkoutId, [WriteAggregate] Checkout checkout)` — business logic only; returns `(IResult, Events, OutgoingMessages)`

### Why the Spike Was Expected to Succeed

The M32.3 "Direct Implementation" comment was misleading for `CompleteCheckout`. The Anti-Pattern #10 concern (compound handler + mixed route + body params) **does not apply here** because `CompleteCheckout` has no JSON body. It is a route-only endpoint (`POST /api/checkouts/{checkoutId}/complete`). The `FetchForWriting` workaround was never needed for this handler.

The `Checkout` stream ID is a natural UUID v7 — the `checkoutId` route parameter is the stream ID directly. Wolverine's `[WriteAggregate]` resolves the stream from a route parameter by convention (`{AggregateName}Id` → `checkoutId` for `Checkout`).

### Return Type Used: `(IResult, Events, OutgoingMessages)` Triple-Tuple

The triple-tuple return type `(IResult, Events, OutgoingMessages)` was used successfully. This is the logical combination of two established patterns in the codebase:
- `(Events, OutgoingMessages)` — from `AddItemToCartHttpEndpoint.Handle()` and `RemoveItemFromCartHandler.Handle()`
- `(CreationResponse<Guid>, Events, OutgoingMessages)` — from `InitiateCheckoutHandler.Handle()` (where `CreationResponse<T>` implements `IResult`)

Wolverine HTTP routes each component of the tuple independently: `IResult` sets the HTTP response, `Events` are appended to the `Checkout` stream via `[WriteAggregate]`, and `OutgoingMessages` are enrolled in the transactional outbox.

### Outbox Risk: Now Eliminated

**Before (two-phase commit gap):**
```csharp
stream.AppendOne(checkoutEvent);
await session.SaveChangesAsync(ct);  // ← event committed to database
// ... if process fails here, event is persisted but OutgoingMessages never enrolled ...
var outgoing = new OutgoingMessages();
outgoing.Add(integrationMessage);
return (Results.Ok(new { orderId }), outgoing);  // ← Wolverine enrolls outbox here
```

**After (`[WriteAggregate]` — single transactional envelope):**
```csharp
// Handle() returns (IResult, Events, OutgoingMessages)
// Wolverine's transactional middleware commits events + enrolls outbox messages atomically
// No SaveChangesAsync() in handler code — no two-phase commit gap
```

With `[WriteAggregate]`, Wolverine's generated code appends events and enrolls `OutgoingMessages` in the same transactional middleware cycle. The `CartCheckoutCompleted` integration message (which starts the Order saga) can no longer be lost due to a failure window between `SaveChangesAsync()` and outbox enrollment.

### M32.3 Comment Updated

The misleading `/// <summary>Direct Implementation pattern — compound handler [WriteAggregate] silently fails to persist events when mixing route + body parameters (M32.3 discovery).</summary>` comment was removed from `CompleteCheckout.cs`. The handler no longer uses the Direct Implementation pattern — it uses the standard `[WriteAggregate]` compound handler.

The other three mixed handlers still use FetchForWriting (correctly, because they do have mixed route + body params), but their comments were updated from "Direct Implementation pattern — see ProvideShippingAddress.cs for rationale" to accurately describe the situation: they use FetchForWriting because of mixed parameters, and `AutoApplyTransactions()` now handles `SaveChangesAsync` automatically.

---

## S4c: Remove Redundant `SaveChangesAsync` from Three Mixed Handlers

### AutoApplyTransactions() Verification

Confirmed in `src/Orders/Orders.Api/Program.cs` line 72:
```csharp
opts.Policies.AutoApplyTransactions();
```

With this policy active, Wolverine wraps any handler injecting `IDocumentSession` in transactional middleware that calls `SaveChangesAsync()` after the handler returns. The explicit `await session.SaveChangesAsync(ct)` calls in all three mixed handlers were redundant.

### Changes Made

All three handlers had one line removed:
```csharp
// Before:
stream.AppendOne(@event);
await session.SaveChangesAsync(ct);  // ← redundant
return Results.Ok(@event);

// After:
stream.AppendOne(@event);
return Results.Ok(@event);
```

**Files changed:**
- `ProvideShippingAddress.cs` — `SaveChangesAsync` removed; comment updated
- `SelectShippingMethod.cs` — `SaveChangesAsync` removed; comment updated
- `ProvidePaymentMethod.cs` — `SaveChangesAsync` removed; comment updated

### Why FetchForWriting Is Still Correct for These Three

These handlers take a typed request record from the JSON body (`ProvideShippingAddressRequest`, `SelectShippingMethodRequest`, `ProvidePaymentMethodRequest`) in addition to the `Guid checkoutId` route parameter. Per Anti-Pattern #10 (Wolverine HTTP body deserialization in compound handlers), `[WriteAggregate]` with `LoadAsync` + `Before` + `Handle` does not work reliably when a handler mixes route params with a deserialized body. The `FetchForWriting` pattern remains correct for these three handlers. S4c only removes the redundant `SaveChangesAsync`; the pattern itself is unchanged.

---

## Test Results

| Phase | Orders Tests | Result |
|-------|-------------|--------|
| Baseline (before S4) | 48 | ✅ All passing |
| After S4a+S4b+S4c | 48 | ✅ All passing |

Test count unchanged at 48 throughout (no new tests — per session scope).

---

## Build State at Session Close

- **Errors:** 0
- **Warnings:** 19 (unchanged from session open — no new warnings introduced)
- **Files modified:** 4
  - `CompleteCheckout.cs` — full refactor (Before + Handle + WriteAggregate; -76/+62 lines; removed IDocumentSession, FetchForWriting, SaveChangesAsync, null guard return tuples)
  - `ProvideShippingAddress.cs` — removed `SaveChangesAsync`; updated comment
  - `SelectShippingMethod.cs` — removed `SaveChangesAsync`; updated comment
  - `ProvidePaymentMethod.cs` — removed `SaveChangesAsync`; updated comment

---

## Key Learnings

1. **`[WriteAggregate]` works for POST endpoints with route-only params + no body.** The M32.3 "Direct Implementation" comment on `CompleteCheckout` was accurate for the three mixed handlers but misleading when left on `CompleteCheckout`. Route-only + `[WriteAggregate]` compound handler is the clean pattern.

2. **`(IResult, Events, OutgoingMessages)` is a valid Wolverine HTTP return type.** The triple-tuple where the first element is `IResult` (not necessarily a Wolverine-specific type like `CreationResponse<T>`) is supported. `Results.Ok(new { orderId })` as the first element sets the HTTP response body while `Events` and `OutgoingMessages` are handled by Wolverine's middleware.

3. **Outbox safety requires `AutoApplyTransactions()` or `[WriteAggregate]`.** The old pattern of explicit `SaveChangesAsync()` before returning `OutgoingMessages` created a two-phase commit gap. With `[WriteAggregate]`, both event persistence and outbox enrollment happen in one transactional middleware cycle.

4. **Removing redundant `SaveChangesAsync` is a safe, non-breaking change.** All 48 tests pass before and after. `AutoApplyTransactions()` was already configured and handling persistence correctly — the explicit calls were simply harmless noise.

---

## Verification Checklist

- [x] `[WriteAggregate]` spike on `CompleteCheckout` succeeded
- [x] `CompleteCheckout` uses `Before(Checkout? checkout)` + `Handle(Guid, [WriteAggregate] Checkout)` compound pattern
- [x] `CompleteCheckout` returns `(IResult, Events, OutgoingMessages)` — no direct `session.Events.Append()` or `SaveChangesAsync()`
- [x] `CartCheckoutCompleted` integration message still published via `OutgoingMessages`
- [x] HTTP URL preserved: `POST /api/checkouts/{checkoutId}/complete`
- [x] `AutoApplyTransactions()` verified in `Orders.Api/Program.cs` before removing `SaveChangesAsync` from three mixed handlers
- [x] `SaveChangesAsync` removed from `ProvideShippingAddressHandler`, `SelectShippingMethodHandler`, `ProvidePaymentMethodHandler`
- [x] HTTP URLs preserved for all three: `/api/checkouts/{checkoutId}/shipping-address`, `/api/checkouts/{checkoutId}/shipping-method`, `/api/checkouts/{checkoutId}/payment-method`
- [x] Misleading "Direct Implementation" class comments updated or removed
- [x] `CheckoutInitiatedHandler.cs` untouched (not in S4 scope)
- [x] `Checkout` aggregate, events, and Order saga untouched (not in S4 scope)
- [x] Build: 0 errors, 19 warnings
- [x] Orders integration tests: 48/48 passing

---

## What Remains in M39.0

**S5 (Quick Wins — next session):**
- Fulfillment `StartStream` — handlers may still use `session.Events.StartStream` directly
- Listings `[WriteAggregate]` — write handlers may use FetchForWriting unnecessarily
- Promotions handler pattern — review for similar idiom drift
- Vendor Portal `AutoApplyTransactions()` — verify configuration and remove any redundant `SaveChangesAsync` calls

After S5, the three most critical idiom violations from the M39.x audit (Correspondence fat handlers, Pricing fat endpoints, Orders outbox risk) will all be resolved, along with the quick wins. Then milestone closure.
