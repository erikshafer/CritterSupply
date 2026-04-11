# M39.0 — Session 3: Pricing BC — Fat Endpoints → Command + Handler

## Where We Are

Session 2 (PR merged) is complete. Read
`docs/planning/milestones/m39-0-session-2-retrospective.md` before writing a single line.

**Session 2 result:**
- `SendMessageHandler` decomposed into `Before()` + `Handle()` using `[WriteAggregate]`
- `OperationCanceledException` guard fixed
- `Message` snapshot configured (`SnapshotLifecycle.Inline`)
- Connection string standardized to `"postgres"`
- 5/5 Correspondence tests passing, 0 errors, 19 warnings

**Correspondence BC is now clean.** Sessions 3 and 4 move on to Pricing and Orders Checkout.

---

Read before starting:
- `docs/planning/milestones/m39-0-milestone-prompt.md` — Section "Session 3" for scope and reasoning
- `docs/skills/wolverine-message-handlers.md` — specifically: §"When NOT to Use `[WriteAggregate]`", §Anti-Pattern #8 (manual load + tuple return), §Anti-Pattern #2 (manual append + return mixed)
- `docs/skills/marten-event-sourcing.md` — `Load()` pattern, snapshot configuration
- `src/Pricing/Pricing.Api/Pricing/SetBasePriceEndpoint.cs` — read fully
- `src/Pricing/Pricing.Api/Pricing/SchedulePriceChangeEndpoint.cs` — read fully (includes `ActivateScheduledPriceChangeHandler` at bottom)
- `src/Pricing/Pricing.Api/Pricing/CancelScheduledPriceChangeEndpoint.cs` — read fully
- `src/Pricing/Pricing/Products/ProductPrice.cs` — understand `StreamId(sku)` and `PriceStatus`
- `src/Pricing/Pricing.Api/Program.cs` — note snapshot is absent; note both domain and API assemblies are registered for discovery

---

## The Core Architectural Decision for This Session

**`[WriteAggregate]` cannot be used for `ProductPrice`.**

`ProductPrice` uses a deterministic UUID v5 stream ID computed by `ProductPrice.StreamId(sku)`:
SHA-1 of `"pricing:{sku.ToUpperInvariant()}"` against the RFC 4122 URL namespace UUID. Wolverine
has no way to derive this computation from a route parameter — its default resolution looks for
`{AggregateName}Id` on the command, and `[WriteAggregate("FromRoute")]` can only pass a raw
route value, not a computed hash.

**The correct pattern** is the `Load()` lifecycle method (documented in `wolverine-message-handlers.md`
§"When NOT to Use `[WriteAggregate]`" under "Loading by deterministic ID (UUID v5 from code/string)"):

```csharp
// Load: compute deterministic stream ID, fetch aggregate
public static async Task<ProductPrice?> LoadAsync(
    string sku,
    IQuerySession session,
    CancellationToken ct)
{
    var streamId = ProductPrice.StreamId(sku);
    return await session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: ct);
}

// Before: validate state (aggregate guaranteed non-null from here)
public static ProblemDetails Before(ProductPrice? price) { ... }

// Handle: business logic — NO tuple return; use session.Events.Append() directly
public static (IResult, OutgoingMessages) Handle(
    string sku,
    SetBasePriceRequest request,
    ProductPrice price,   // non-null guaranteed by Before()
    IDocumentSession session)
{
    var streamId = ProductPrice.StreamId(sku);
    var evt = /* build event */;
    session.Events.Append(streamId, evt);  // explicit append
    var outgoing = new OutgoingMessages();
    return (Results.Ok(...), outgoing);
}
```

**Critical:** When using `Load()` + manual `session.Events.Append()`, `Handle()` returns only
`OutgoingMessages` (or `(IResult, OutgoingMessages)` for HTTP endpoints) — **never** `(Events, OutgoingMessages)`.
Returning `Events` from a handler that also calls `session.Events.Append()` is Anti-Pattern #8 —
the events would be persisted twice. The `session.Events.Append()` call IS the event persistence.

---

## What This Session Does

Four items, in order:

| Item | File(s) | Description |
|------|---------|-------------|
| **S3a** | `SetBasePrice.cs` | Decompose `SetBasePriceEndpoint` — command + handler using `Load()` |
| **S3b** | `SchedulePriceChange.cs` | Decompose `SchedulePriceChangeEndpoint` + `ActivateScheduledPriceChangeHandler` |
| **S3c** | `CancelScheduledPriceChange.cs` | Decompose `CancelScheduledPriceChangeEndpoint` |
| **S3d** | `Program.cs` | `ProductPrice` snapshot config + TODO cleanup across all three handler files |

**File locations:** The new files go in `src/Pricing/Pricing/Products/`, alongside `ProductPrice.cs`,
`ProductRegistered.cs`, etc. This is vertical slice organization — command, validator, and handler
colocated in the domain project. The HTTP `[Wolverine*]` attributes live on the handler method in
the domain project; Wolverine discovers them because `opts.Discovery.IncludeAssembly(typeof(ProductPrice).Assembly)`
is registered in `Program.cs`. Delete the old endpoint files in `Pricing.Api/Pricing/` after the
new files are confirmed working.

**Preserve all existing HTTP URLs exactly:**
- `POST /api/pricing/products/{sku}/base-price`
- `POST /api/pricing/products/{sku}/schedule`
- `DELETE /api/pricing/products/{sku}/schedule/{scheduleId}`

---

## Guard Rails

1. **No change to business logic.** Floor/ceiling constraint enforcement, state-based branching
   (`Unpriced` vs `Published` for `SetBasePrice`), pending schedule conflicts, schedule ID
   validation — all must be preserved exactly. The refactor changes structure, not behavior.

2. **`OutgoingMessages` only from `Handle()`, never `(Events, OutgoingMessages)`.** Using `Load()`
    + `session.Events.Append()` means you own persistence. Do not also return `Events` — that's
      Anti-Pattern #8.

3. **`ScheduleAsync()` stays on `IMessageBus`.** The `SchedulePriceChange` endpoint currently
   calls `await messaging.ScheduleAsync(activationMessage, request.ScheduledFor)`. This is the one
   justified `IMessageBus` use per documented convention (`bus.ScheduleAsync()` for delayed delivery
   cannot be expressed via `OutgoingMessages`). Keep it. The parameter type can remain `IMessageBus`
   or `IMessageContext` — either works.

4. **Delete old endpoint files after verifying tests pass.** The `Pricing.Api/Pricing/` endpoint
   files should not coexist with the new domain files after refactoring. Delete them in the final
   commit of each item, after confirming the build compiles and tests pass.

5. **`Guid.Empty` for TODO placeholders, not `Guid.NewGuid()`.** Three command events carry
   `SetBy`, `ScheduledBy`, or `CancelledBy` fields with `Guid.NewGuid()` placeholders (pending
   JWT claim extraction). Replace with `Guid.Empty` and a comment `// TODO: Extract from JWT claim`.
   `Guid.NewGuid()` in an event field is actively misleading — it suggests a real user ID when
   there is none.

6. **Tests must pass after each commit.** Run `dotnet test Pricing.Api.IntegrationTests` after
   each item. No regressions. The Pricing test suite should pass throughout.

---

## Execution Order

```
Read all three endpoint files and ProductPrice.cs before writing any code
  ↓
S3a: SetBasePrice.cs — create in Pricing/Products/, verify, delete old file
  ↓
Run dotnet test (Pricing.Api.IntegrationTests) — confirm no regressions
  ↓
S3b: SchedulePriceChange.cs — create in Pricing/Products/ (includes ActivateScheduledPriceChange),
     verify, delete old file
  ↓
Run dotnet test — confirm no regressions
  ↓
S3c: CancelScheduledPriceChange.cs — create in Pricing/Products/, verify, delete old file
  ↓
Run dotnet test — confirm no regressions
  ↓
S3d: Program.cs snapshot config + TODO cleanup across all new files
  ↓
Run dotnet test one final time — record counts for retrospective
```

---

## S3a: `SetBasePrice`

**Current state:** `SetBasePriceEndpoint.cs` is an 85-line static endpoint class with manual
`AggregateStreamAsync()` + business logic + `session.Events.Append()` all in one method. It handles
two cases: (1) product is `Unpriced` → emit `InitialPriceSet`; (2) product is `Published` → emit
`PriceChanged` with floor/ceiling enforcement.

**Target structure in `Pricing/Products/SetBasePrice.cs`:**

```csharp
// Command
public sealed record SetBasePrice(string Sku, decimal Amount, string Currency = "USD");

// Validator (already exists as SetBasePriceValidator in the endpoint file — move here)
public sealed class SetBasePriceValidator : AbstractValidator<SetBasePrice> { ... }

public static class SetBasePriceHandler
{
    // Load: deterministic stream ID computation
    public static async Task<ProductPrice?> LoadAsync(
        string sku,
        IQuerySession session,
        CancellationToken ct)
    {
        var streamId = ProductPrice.StreamId(sku);
        return await session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: ct);
    }

    // Before: all validation/precondition checks
    public static ProblemDetails Before(SetBasePrice cmd, ProductPrice? price)
    {
        if (price is null)
            return new ProblemDetails { Detail = $"Product '{cmd.Sku}' not found in Pricing BC", Status = 404 };
        if (price.Status == PriceStatus.Discontinued)
            return new ProblemDetails { Detail = "Cannot set price for discontinued product", Status = 400 };
        // Published-specific validations (floor/ceiling) go here when status is Published
        if (price.Status == PriceStatus.Published && price.BasePrice is not null)
        {
            var newPrice = Money.Of(cmd.Amount, cmd.Currency);
            if (price.FloorPrice is not null && newPrice < price.FloorPrice)
                return new ProblemDetails { Detail = $"New price {newPrice} is below floor price {price.FloorPrice}", Status = 400 };
            if (price.CeilingPrice is not null && newPrice > price.CeilingPrice)
                return new ProblemDetails { Detail = $"New price {newPrice} exceeds ceiling price {price.CeilingPrice}", Status = 400 };
        }
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/pricing/products/{sku}/base-price")]
    [Authorize(Policy = "PricingManager")]
    public static (IResult, OutgoingMessages) Handle(
        string sku,
        SetBasePrice cmd,
        ProductPrice price,       // non-null guaranteed by Before()
        IDocumentSession session)
    {
        var streamId = ProductPrice.StreamId(sku);
        var now = DateTimeOffset.UtcNow;
        var outgoing = new OutgoingMessages();

        if (price.Status == PriceStatus.Unpriced)
        {
            var evt = new InitialPriceSet(streamId, sku.ToUpperInvariant(),
                Money.Of(cmd.Amount, cmd.Currency), null, null,
                Guid.Empty, // TODO: Extract from JWT claim
                now);
            session.Events.Append(streamId, evt);
            return (Results.Ok(new { sku = sku.ToUpperInvariant(), basePrice = new { amount = cmd.Amount, currency = cmd.Currency }, status = "Published", message = "Initial price set successfully" }), outgoing);
        }

        // Published path — floor/ceiling already validated in Before()
        var newPrice = Money.Of(cmd.Amount, cmd.Currency);
        var changeEvt = new PriceChanged(streamId, sku.ToUpperInvariant(),
            price.BasePrice!, newPrice,
            price.LastChangedAt ?? price.RegisteredAt,
            "Base price updated by PricingManager",
            Guid.Empty, // TODO: Extract from JWT claim
            now, null, null);
        session.Events.Append(streamId, changeEvt);

        return (Results.Ok(new { sku = sku.ToUpperInvariant(), oldPrice = new { amount = price.BasePrice!.Amount, currency = price.BasePrice.Currency }, newPrice = new { amount = newPrice.Amount, currency = newPrice.Currency }, message = "Base price updated successfully" }), outgoing);
    }
}
```

**Note on `IQuerySession` vs `IDocumentSession` in `LoadAsync`:** Use `IQuerySession` in `LoadAsync`
(read-only). Use `IDocumentSession` in `Handle()` (write). Both are injectable via Wolverine.

**Note on the validator:** `SetBasePriceValidator` in the old endpoint validates `SetBasePriceRequest`.
After refactoring, the command is `SetBasePrice` — rename the validator to `AbstractValidator<SetBasePrice>`
and move it to the new file. The old `SetBasePriceRequest` record is eliminated since the command
carries the same fields.

---

## S3b: `SchedulePriceChange` (+ `ActivateScheduledPriceChange`)

The current `SchedulePriceChangeEndpoint.cs` contains two handlers:
1. The HTTP endpoint for scheduling
2. `ActivateScheduledPriceChangeHandler` — the internal message handler called by the scheduled message

Both live in one file and both need the `Load()` pattern.

**Target structure in `Pricing/Products/SchedulePriceChange.cs`:**

The file should contain:
- `sealed record SchedulePriceChange(string Sku, decimal NewAmount, string Currency, DateTimeOffset ScheduledFor)` — command
- `SchedulePriceChangeValidator : AbstractValidator<SchedulePriceChange>` — moved from old file
- `SchedulePriceChangeHandler` — `LoadAsync()`, `Before()`, `Handle()` with `IMessageBus` for `ScheduleAsync()`
- `sealed record ActivateScheduledPriceChange(string Sku, Guid ScheduleId)` — internal command (not a cross-BC contract; stays in domain)
- `ActivateScheduledPriceChangeHandler` — also uses `Load()` pattern; no `[WriteAggregate]`

**`ActivateScheduledPriceChangeHandler` after refactor:**

The current handler manually loads the aggregate and calls `session.Events.Append()`. After refactoring,
it should follow the same `LoadAsync()` + `Before()` + `Handle()` structure. The `Before()` handles
the "stale message" checks:

```csharp
public static class ActivateScheduledPriceChangeHandler
{
    public static async Task<ProductPrice?> LoadAsync(
        ActivateScheduledPriceChange cmd,
        IQuerySession session, CancellationToken ct)
        => await session.Events.AggregateStreamAsync<ProductPrice>(
            ProductPrice.StreamId(cmd.Sku), token: ct);

    public static HandlerContinuation Before(
        ActivateScheduledPriceChange cmd, ProductPrice? price)
    {
        if (price is null) return HandlerContinuation.Stop; // product deleted after schedule
        if (price.PendingSchedule is null || price.PendingSchedule.ScheduleId != cmd.ScheduleId)
            return HandlerContinuation.Stop; // schedule cancelled or superseded
        return HandlerContinuation.Continue;
    }

    public static void Handle(
        ActivateScheduledPriceChange cmd,
        ProductPrice price,
        IDocumentSession session)
    {
        var streamId = ProductPrice.StreamId(cmd.Sku);
        var evt = new ScheduledPriceActivated(
            streamId, cmd.Sku.ToUpperInvariant(),
            cmd.ScheduleId, price.PendingSchedule!.ScheduledPrice,
            DateTimeOffset.UtcNow);
        session.Events.Append(streamId, evt);
        // No return value — no outgoing messages for scheduled activation
    }
}
```

---

## S3c: `CancelScheduledPriceChange`

Smallest of the three. The `Before()` method handles all the validation (not found, no schedule,
schedule ID mismatch). `Handle()` appends `ScheduledPriceChangeCancelled`.

Note: the current endpoint uses `[WolverineDelete]` (HTTP DELETE verb). Preserve this — the URL
and verb must not change: `DELETE /api/pricing/products/{sku}/schedule/{scheduleId}`.

**Target structure in `Pricing/Products/CancelScheduledPriceChange.cs`:**

```csharp
public sealed record CancelScheduledPriceChange(string Sku, Guid ScheduleId);

public static class CancelScheduledPriceChangeHandler
{
    public static async Task<ProductPrice?> LoadAsync(
        string sku,
        IQuerySession session, CancellationToken ct)
        => await session.Events.AggregateStreamAsync<ProductPrice>(
            ProductPrice.StreamId(sku), token: ct);

    public static ProblemDetails Before(
        CancelScheduledPriceChange cmd, ProductPrice? price)
    {
        if (price is null)
            return new ProblemDetails { Detail = $"Product '{cmd.Sku}' not found", Status = 404 };
        if (price.PendingSchedule is null)
            return new ProblemDetails { Detail = "No pending scheduled price change", Status = 404 };
        if (price.PendingSchedule.ScheduleId != cmd.ScheduleId)
            return new ProblemDetails { Detail = "Schedule ID does not match the pending schedule",
                Extensions = { ["expectedScheduleId"] = price.PendingSchedule.ScheduleId,
                               ["providedScheduleId"] = cmd.ScheduleId }, Status = 404 };
        return WolverineContinue.NoProblems;
    }

    [WolverineDelete("/api/pricing/products/{sku}/schedule/{scheduleId}")]
    [Authorize(Policy = "PricingManager")]
    public static (IResult, OutgoingMessages) Handle(
        string sku,
        CancelScheduledPriceChange cmd,
        ProductPrice price,
        IDocumentSession session)
    {
        var streamId = ProductPrice.StreamId(sku);
        var evt = new ScheduledPriceChangeCancelled(
            streamId, sku.ToUpperInvariant(),
            cmd.ScheduleId,
            "Cancelled by PricingManager",
            Guid.Empty, // TODO: Extract from JWT claim
            DateTimeOffset.UtcNow);
        session.Events.Append(streamId, evt);
        return (Results.Ok(new { sku = sku.ToUpperInvariant(), scheduleId = cmd.ScheduleId, message = "Scheduled price change cancelled" }), new OutgoingMessages());
    }
}
```

---

## S3d: Snapshot Configuration + TODO Cleanup

**Snapshot:** In `Pricing.Api/Program.cs`, inside the `AddMarten(opts => { })` block, add:

```csharp
opts.Projections.Snapshot<ProductPrice>(SnapshotLifecycle.Inline);
```

This is the same API used by Correspondence (`Snapshot<Message>`), Promotions (`Snapshot<Coupon>`,
`Snapshot<Promotion>`), etc. Look at the Correspondence `Program.cs` for the `using` directive
if needed (`using Marten.Events.Projections;`).

**Why it matters:** The `SchedulePriceChange` handler and the `ActivateScheduledPriceChange` handler
both load `ProductPrice` via `AggregateStreamAsync`. Without a snapshot, Marten replays the full
event stream from the beginning on every load. For products with many price changes over time,
this degrades. With `SnapshotLifecycle.Inline`, the snapshot is updated in the same transaction
as the event append — always current.

**Note on SeedPricesAsync:** `Program.cs` contains a development-only `SeedPricesAsync` function
that uses `session.Events.StartStream<ProductPrice>` directly (Anti-Pattern #9 from the audit).
This is development seed data — do not change it in this session. The pattern in seed data is an
accepted deviation; it does not affect production behavior and is guarded by `IsDevelopment()`.

**TODO cleanup:** In the three new handler files (S3a, S3b, S3c), replace all `Guid.NewGuid()`
that serve as `SetBy` / `ScheduledBy` / `CancelledBy` placeholders with `Guid.Empty` and the
comment `// TODO: Extract from JWT claim`. This is done as part of writing the files, not as a
separate commit.

---

## Mandatory Session Bookends

**First act:** `dotnet build` (0 errors, 19 warnings baseline). Run
`dotnet test Pricing.Api.IntegrationTests` — confirm passing count. Record as baseline.

**Last acts — all required:**

**1. Commit `docs/planning/milestones/m39-0-session-3-retrospective.md`**

Must cover:
- Confirmation that the `Load()` pattern (not `[WriteAggregate]`) was the correct approach for
  `ProductPrice.StreamId(sku)` — include a sentence explaining why
- Whether the `SetBasePriceHandler.Before()` method correctly moved the floor/ceiling validation
  out of `Handle()` — if the constraint checks have state dependencies (they need `price.Status`
  and `price.FloorPrice`), confirm they work in `Before()` because `Before()` receives the aggregate
- Whether `ActivateScheduledPriceChangeHandler` needed `HandlerContinuation.Stop` or `ProblemDetails`
  for its guard — and why `HandlerContinuation.Stop` is correct for a non-HTTP message handler
- How the snapshot API line was confirmed (which existing BC was used as reference)
- Test counts before and after
- Build state at session close (warnings — should not increase)
- CI run number confirming green
- What remains in M39.0 (S4: Orders Checkout, S5: Quick wins + Promotions)

**2. Update `CURRENT-CYCLE.md`**

Record S3 progress. Update Last Updated timestamp.

---

## Roles

### @PSA — Principal Software Architect
Primary owner of all four items. Pay close attention to `SetBasePriceHandler.Before()` — the
floor/ceiling validation currently lives in `Handle()` and the current check requires `price.BasePrice`
to be non-null. In `Before()`, `price` is nullable (`ProductPrice?`) — you need the null-check and
status-check first, then the floor/ceiling check only for `Published` products. The illustrative
pattern above shows this structure; verify it against the actual `ProductPrice` fields.

For `SchedulePriceChangeHandler.Handle()`, the `IMessageBus.ScheduleAsync()` call stays — inject
`IMessageBus` (or `IMessageContext`) as a `Handle()` parameter. Wolverine resolves this from DI
the same as any other dependency.

### @QAE — QA Engineer
Verify tests pass after each item (S3a, S3b, S3c individually). The Pricing integration tests
should not break if the endpoint URLs are preserved and the business logic is intact. If any test
fails, diagnose before proceeding to the next item.

---

## Commit Message Convention

```
M39.0 S3a: Pricing — SetBasePrice command+handler (Load pattern); remove fat endpoint
M39.0 S3b: Pricing — SchedulePriceChange + ActivateScheduledPriceChange command+handler
M39.0 S3c: Pricing — CancelScheduledPriceChange command+handler; remove fat endpoint
M39.0 S3d: Pricing — ProductPrice snapshot (SnapshotLifecycle.Inline)
M39.0 S3 retro: docs — session retrospective
M39.0 S3: docs — CURRENT-CYCLE.md update
```

After Session 3, the Pricing BC will demonstrate the `Load()` pattern for deterministic stream IDs —
the complement to `[WriteAggregate]` for natural IDs. This is an important reference case because
UUID v5 deterministic IDs are a common real-world pattern (used in Pricing, Promotions, and any BC
that needs idempotent stream creation from a domain key), and the correct handler structure for them
differs from the standard `[WriteAggregate]` compound handler. Pricing should show that difference
clearly.
