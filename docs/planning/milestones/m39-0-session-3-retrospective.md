# M39.0 Session 3 Retrospective

**Date:** 2026-04-04
**Milestone:** M39.0 — Critter Stack Idiom Refresh: Pricing BC
**Session:** Session 3 — Pricing BC Fat Endpoints → Command + Handler

---

## Baseline

- Build: 0 errors, 19 warnings (unchanged from S2 close)
- Pricing integration tests: 25/25 passing
- Three fat endpoints in `Pricing.Api/Pricing/`: `SetBasePriceEndpoint.cs` (118 lines), `SchedulePriceChangeEndpoint.cs` (152 lines), `CancelScheduledPriceChangeEndpoint.cs` (63 lines)
- All three in `Pricing.Api` assembly; `ActivateScheduledPriceChangeHandler` colocated in the schedule endpoint file

---

## Items Completed

| Item | Description |
|------|-------------|
| S3a | `SetBasePrice.cs` — command + validator + handler using Load() pattern; old fat endpoint deleted |
| S3b | `SchedulePriceChange.cs` — command + validator + handler + `ActivateScheduledPriceChange` + handler; old fat endpoint deleted |
| S3c | `CancelScheduledPriceChange.cs` — command + handler (single-method async; DELETE verb); old fat endpoint deleted |
| S3d | `ProductPrice` snapshot (`SnapshotLifecycle.Inline`) in `Program.cs`; `using Marten.Events.Projections` added |

---

## S3a: SetBasePrice — Load() Pattern

### Why `[WriteAggregate]` Cannot Be Used

`ProductPrice.StreamId(sku)` computes a deterministic UUID v5 (SHA-1 of `"pricing:{sku.ToUpperInvariant()}"` against the RFC 4122 URL namespace UUID). Wolverine's `[WriteAggregate]` resolves the stream by convention — it looks for a `{AggregateName}Id` property on the command or a matching route parameter. It cannot execute a SHA-1 hash computation from a route string. Therefore, `[WriteAggregate]` is not applicable for `ProductPrice`, and the `Load()` lifecycle method is the correct pattern.

### Handler Structure (After)

```csharp
public static class SetBasePriceHandler
{
    // LoadAsync: compute UUID v5, fetch aggregate
    public static async Task<ProductPrice?> LoadAsync(string sku, IQuerySession session, CancellationToken ct) { ... }

    // Before: status guard + floor/ceiling validation (receives loaded aggregate)
    public static ProblemDetails Before(SetBasePrice cmd, ProductPrice? price) { ... }

    // Handle: append InitialPriceSet or PriceChanged; return (IResult, OutgoingMessages)
    [WolverinePost("/api/pricing/products/{sku}/base-price")]
    public static (IResult, OutgoingMessages) Handle(string sku, SetBasePrice cmd, ProductPrice price, IDocumentSession session) { ... }
}
```

### Floor/Ceiling Validation in Before()

The floor/ceiling constraint checks were originally in `Handle()`. After refactoring, they moved to `Before()`. This works because `Before()` receives the loaded `ProductPrice?` aggregate — all state needed for the constraint validation (`price.Status`, `price.FloorPrice`, `price.CeilingPrice`, `price.BasePrice`) is available. The structure:
1. Null-check and status guard first (returns 404 / 400 for Discontinued)
2. Floor/ceiling checks conditional on `Published` status
3. `Handle()` executes only with valid preconditions already confirmed

### OutgoingMessages — Not `(Events, OutgoingMessages)`

Using `Load()` + `session.Events.Append()` means the handler owns event persistence. The `Handle()` returns `(IResult, OutgoingMessages)` — never `(Events, OutgoingMessages)`. Returning `Events` from a handler that also calls `session.Events.Append()` is Anti-Pattern #8 (double-persistence). The `session.Events.Append()` call IS the event persistence.

---

## S3b: SchedulePriceChange + ActivateScheduledPriceChange

### ScheduleAsync Stays on IMessageBus

`SchedulePriceChangeHandler.Handle()` injects `IMessageBus messaging` and calls `await messaging.ScheduleAsync(activationMessage, cmd.ScheduledFor)`. This is the one justified `IMessageBus` use — Wolverine's delayed message delivery (`ScheduleAsync`) cannot be expressed via `OutgoingMessages`. The `Handle()` return type remains `Task<(IResult, OutgoingMessages)>`.

### ActivateScheduledPriceChangeHandler — HandlerContinuation vs ProblemDetails

`ActivateScheduledPriceChangeHandler.Before()` returns `HandlerContinuation.Stop` (not `ProblemDetails`) for stale/cancelled schedule guards. This is correct because:
- `ActivateScheduledPriceChange` is a **non-HTTP message handler** (internal delayed message, not an HTTP endpoint)
- `ProblemDetails` is an HTTP concept — returning it from a non-HTTP handler would be nonsensical; there is no HTTP response to write
- `HandlerContinuation.Stop` silently discards the message, which is the correct behavior for a stale schedule (product deleted or schedule superseded after the delayed message was enqueued)

This mirrors the `SendMessageHandler.Before()` pattern from S2 (`HandlerContinuation.Stop` for non-HTTP idempotency guards).

---

## S3c: CancelScheduledPriceChange — DELETE Verb Constraint

### Discovery: DELETE + Compound Handler Pattern

An attempted refactor using `LoadAsync` + `Before` + `Handle` (compound handler) for the DELETE endpoint failed with:

```
{"title":"Invalid JSON format","status":400,"detail":"The input does not contain any JSON tokens."}
```

Root cause: Wolverine HTTP's compound handler pattern triggers body deserialization for any endpoint where a `LoadAsync` method exists in the handler class. DELETE requests carry no body. The `UseFluentValidationProblemDetailMiddleware()` pipeline reads the body before handler execution and returns 400 when no JSON is found.

### Resolution: Single-Method Async Handler for DELETE

DELETE endpoints with route-only parameters use a single-method async handler. This is consistent with the existing `RemoveItemFromCartHandler` which uses `[WriteAggregate]` (not `LoadAsync`) to load the aggregate — the compound handler pattern is compatible with `[WriteAggregate]` for DELETE, but not with `LoadAsync`. Since `ProductPrice` cannot use `[WriteAggregate]` (UUID v5 stream ID), a single-method async handler is the correct and idiomatic approach:

```csharp
[WolverineDelete("/api/pricing/products/{sku}/schedule/{scheduleId}")]
[Authorize(Policy = "PricingManager")]
public static async Task<IResult> HandleAsync(
    string sku, Guid scheduleId,
    IDocumentSession session, CancellationToken ct)
{
    // inline guard checks + session.Events.Append()
}
```

The `CancelScheduledPriceChange` record is preserved as a domain concept (available for internal bus messaging), but the HTTP handler uses direct route parameters.

---

## S3d: Snapshot Configuration

### API Used

```csharp
opts.Projections.Snapshot<ProductPrice>(SnapshotLifecycle.Inline);
```

Reference BC: Correspondence (`Snapshot<Message>(SnapshotLifecycle.Inline)`), confirmed in `src/Correspondence/Correspondence.Api/Program.cs:43`. The same API is used by Returns, Promotions, Payments, Inventory, and Listings BCs.

### Using Directive

Added `using Marten.Events.Projections;` to `Pricing.Api/Program.cs`. This was needed for `SnapshotLifecycle` — the same directive was added to Correspondence's `Program.cs` in S2.

### Why Snapshot Matters

`SchedulePriceChangeHandler` and `ActivateScheduledPriceChangeHandler` both call `session.Events.AggregateStreamAsync<ProductPrice>`. Without a snapshot, Marten replays the full event stream on every load. Products with many price changes (seasonal pricing, promotional cycles) accumulate long event streams. With `SnapshotLifecycle.Inline`, the snapshot is updated in the same transaction as each event append — always current, never stale.

### SeedPricesAsync (Not Changed)

`Program.cs` contains a development-only `SeedPricesAsync` function using `session.Events.StartStream<ProductPrice>` directly. This is accepted seed data behavior, guarded by `IsDevelopment()`, and was not changed per session scope guidance.

---

## Test Results

| Phase | Pricing Tests | Result |
|-------|--------------|--------|
| Before S3a (baseline) | 25 | ✅ All passing |
| After S3a + S3b + S3c + S3d (attempt 1) | 25 | ❌ 4 failing (CancelScheduledPriceChange compound handler DELETE issue) |
| After S3c fix (single-method async) | 25 | ✅ All passing |

Test count unchanged at 25 throughout (no new tests — per session scope).

---

## Build State at Session Close

- **Errors:** 0
- **Warnings:** 0 (domain project) / pre-existing NU1504 duplicate PackageReference warnings in unrelated projects (not introduced by this session)
- **Files deleted:** 3 (old fat endpoints in `Pricing.Api/Pricing/`)
- **Files created:** 3 (new command+handler files in `Pricing/Products/`)
- **Files modified:** 1 (`Pricing.Api/Program.cs` — snapshot + using directive)

---

## Structural Metrics

| Endpoint | Before (fat endpoint) | After (command+handler) |
|----------|----------------------|------------------------|
| SetBasePrice | 118 lines, `Pricing.Api.Pricing` namespace | `SetBasePrice.cs`: ~135 lines, `Pricing.Products` namespace, Load()/Before()/Handle() |
| SchedulePriceChange | 152 lines (includes ActivateScheduledPriceChange) | `SchedulePriceChange.cs`: ~185 lines, Load()/Before()/Handle() for both handlers |
| CancelScheduledPriceChange | 63 lines, `Pricing.Api.Pricing` | `CancelScheduledPriceChange.cs`: ~65 lines, `Pricing.Products`, single-method async |

Handler location: **`Pricing.Api/Pricing/`** (old) → **`Pricing/Products/`** (new, domain project)

---

## Key Learnings

1. **`[WriteAggregate]` vs `Load()` pattern:** `[WriteAggregate]` is for aggregates with natural IDs matching route params. `Load()` is required when the stream ID is computed (UUID v5, composite keys, etc.). Pricing is now the canonical reference for the `Load()` pattern.

2. **DELETE + compound handler limitation:** Wolverine HTTP's compound handler (`LoadAsync` + `Before` + `Handle`) triggers body deserialization regardless of HTTP verb. For DELETE endpoints with route-only inputs, a single-method async handler is correct. `[WriteAggregate]` can be used with DELETE compound handlers (Shopping's `RemoveItemFromCartHandler`), but `LoadAsync` cannot.

3. **`HandlerContinuation.Stop` for internal message handlers:** Non-HTTP handlers (delayed messages, integration events) use `HandlerContinuation` for guards. `ProblemDetails` is an HTTP-only concept.

4. **`Guid.Empty` for unresolved JWT claims:** Replaced `Guid.NewGuid()` in `SetBy`/`ScheduledBy`/`CancelledBy` fields with `Guid.Empty` + `// TODO: Extract from JWT claim`. `Guid.NewGuid()` in event fields is misleading — it suggests a real user ID.

---

## Verification Checklist

- [x] `SetBasePriceHandler` uses `LoadAsync` (not `[WriteAggregate]`)
- [x] `SetBasePriceHandler.Before()` validates floor/ceiling constraints using loaded aggregate
- [x] `SetBasePriceHandler.Handle()` returns `(IResult, OutgoingMessages)` — NOT `(Events, OutgoingMessages)`
- [x] `session.Events.Append()` in Handle() (not returning Events)
- [x] `SchedulePriceChangeHandler.Handle()` uses `IMessageBus.ScheduleAsync()` for delayed delivery
- [x] `ActivateScheduledPriceChangeHandler.Before()` returns `HandlerContinuation.Stop` (not ProblemDetails)
- [x] `ActivateScheduledPriceChangeHandler.Handle()` returns `void` (no OutgoingMessages)
- [x] `CancelScheduledPriceChangeHandler` is single-method async (DELETE verb, no body)
- [x] All `Guid.NewGuid()` placeholders replaced with `Guid.Empty` + `// TODO: Extract from JWT claim`
- [x] Old fat endpoint files deleted from `Pricing.Api/Pricing/`
- [x] `Snapshot<ProductPrice>(SnapshotLifecycle.Inline)` in `Program.cs`
- [x] `using Marten.Events.Projections;` added to `Program.cs`
- [x] HTTP URLs preserved: `POST /api/pricing/products/{sku}/base-price`, `POST /api/pricing/products/{sku}/schedule`, `DELETE /api/pricing/products/{sku}/schedule/{scheduleId}`
- [x] Build: 0 errors, no new warnings
- [x] Pricing integration tests: 25/25 passing

---

## What Remains in M39.0

- **S4:** Orders Checkout — 4 handlers using "Direct Implementation" workaround (FetchForWriting + AppendOne + SaveChangesAsync); convert to Load() pattern
- **S5 (if scoped):** Quick wins + Promotions BC cleanup
