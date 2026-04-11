# M39.0 — Session 5: Quick Wins + Promotions

## Where We Are

Session 4 (PR merged) is complete. Read
`docs/planning/milestones/m39-0-session-4-retrospective.md` before writing a single line.

**Session 4 result:**
- `CompleteCheckout` fully refactored to `[WriteAggregate]` compound handler
- `(IResult, Events, OutgoingMessages)` triple-tuple confirmed as valid Wolverine HTTP return type
- Outbox risk eliminated — event append and `CartCheckoutCompleted` outbox enrollment now atomic
- Redundant `SaveChangesAsync` removed from three mixed Checkout handlers
- 48/48 Orders tests passing, 0 errors, 19 warnings

**What remains for M39.0 before milestone closure:**
This session addresses four BCs with remaining idiom drift identified in the M39.x audit.
After S5 merges, only the milestone closure session (S6) remains.

**M39.0 total sessions: 7** (S0 through S6, where S6 is documentation-only closure).

---

Read before starting:
- `docs/planning/milestones/m39-0-session-4-retrospective.md` — S4 confirmation
- `docs/planning/milestones/m39-x-planning-session.md` — Section 2 audit findings for
  Fulfillment, Listings, Promotions, Vendor Portal
- `docs/skills/wolverine-message-handlers.md` — Anti-Patterns #8, #9; `[WriteAggregate]`
  vs `Load()` pattern; `HandlerContinuation.Stop`

---

## Four Targets, Ordered by Scope

| Target | BC | What | Approach |
|--------|----|------|----------|
| **D-1** | Fulfillment | `RequestFulfillmentHandler` direct `StartStream` | Return `IStartStream` |
| **D-2** | Listings | `CreateListing` direct `StartStream` | Return `IStartStream` |
| **D-3** | Listings | 6 write handlers: manual load + append | `[WriteAggregate]` compound handler |
| **D-4** | Promotions | `RedeemCoupon`, `RevokeCoupon` — UUID v5 stream IDs | `Load()` + `Before()` + append |
| **D-5** | Promotions | `RecordPromotionRedemption` — natural UUID stream ID | `[WriteAggregate]` compound handler |
| **D-6** | Vendor Portal | `AutoApplyTransactions()` missing from `Program.cs` | Add one line; sweep `SaveChangesAsync` |

**Explicitly NOT in scope:**
- `FulfillmentRequestedHandler` — direct `session.Events.StartStream` with an idempotency guard
  (`FetchStreamStateAsync` before `StartStream`). This is an accepted deviation documented in
  the audit: the idempotency logic requires conditional stream creation that `IStartStream` cannot
  express. Do not touch it.

---

## Guard Rails

1. **Preserve all business logic exactly.** Status machine guards, error messages, integration
   message publishing — all unchanged. The refactors change structure, not behavior.

2. **Tests must pass after each commit.** Run the affected BC's integration tests after each
   commit group. No regressions permitted before moving to the next target.

3. **Know your stream ID type before choosing a pattern:**
    - Natural `Guid` ID (UUID v7 created at event time) → `[WriteAggregate]` resolves by convention
    - Deterministic `Guid` (UUID v5 from string) → `Load()` + manual `session.Events.Append()`

4. **`[WriteAggregate]` on Listings write handlers — how it resolves:** Each command has a
   `Guid ListingId` property. Wolverine resolves the `Listing` stream by matching `{AggregateName}Id`
   → `ListingId`. This is a natural UUID v7 stream ID created by `ListingStreamId.Compute()` at
   listing creation and stored on the aggregate as `Id`. `[WriteAggregate]` will find it correctly.

5. **Validation moves from `throw` to `Before()`.** The current write handlers throw exceptions
   for validation. After refactoring to compound handlers, those guards should move to `Before()`
   returning `ProblemDetails` for HTTP handlers and `HandlerContinuation.Stop` for non-HTTP
   message handlers. Exceptions are appropriate for truly unexpected conditions (infrastructure
   failures, corrupted state), not for expected business rule violations.

---

## D-1: Fulfillment — `RequestFulfillmentHandler`

**File:** `src/Fulfillment/Fulfillment/Shipments/RequestFulfillment.cs`

**Current pattern (Anti-Pattern #9):**
```csharp
public static class RequestFulfillmentHandler
{
    [WolverinePost("/api/fulfillment/shipments")]
    [Authorize]
    public static CreationResponse Handle(
        RequestFulfillment command,
        IDocumentSession session)   // ← only used for StartStream
    {
        var shipmentId = Guid.CreateVersion7();
        var @event = new FulfillmentRequested(...);
        session.Events.StartStream<Shipment>(shipmentId, @event);  // ← Anti-Pattern #9
        return new CreationResponse($"/api/fulfillment/shipments/{shipmentId}");
    }
}
```

**After refactor:**
```csharp
public static class RequestFulfillmentHandler
{
    [WolverinePost("/api/fulfillment/shipments")]
    [Authorize]
    public static (CreationResponse, IStartStream) Handle(RequestFulfillment command)
    {
        var shipmentId = Guid.CreateVersion7();
        var @event = new FulfillmentRequested(...);
        var stream = MartenOps.StartStream<Shipment>(shipmentId, @event);
        return (new CreationResponse($"/api/fulfillment/shipments/{shipmentId}"), stream);
    }
}
```

**Changes:** Remove `IDocumentSession` parameter; replace `session.Events.StartStream` with
`MartenOps.StartStream`; return `(CreationResponse, IStartStream)` tuple. Add `using Wolverine.Marten;`.

**Commit:** `M39.0 S5a: Fulfillment — RequestFulfillmentHandler IStartStream return`

---

## D-2: Listings — `CreateListingHandler`

**File:** `src/Listings/Listings/Listing/CreateListing.cs`

**Current pattern (Anti-Pattern #9):**
```csharp
session.Events.StartStream<Listing>(listingId, @event);  // ← Anti-Pattern #9
return (new CreateListingResponse(...), outgoing);
```

**Important:** `CreateListing` has complex pre-flight validation that requires `IDocumentSession`
for `session.LoadAsync<ProductSummaryView>` and `session.LoadAsync<ListingsActiveView>`. The
session cannot be removed — only the `StartStream` call changes.

**After refactor:**
```csharp
var stream = MartenOps.StartStream<Listing>(listingId, @event);
return (new CreateListingResponse(...), stream, outgoing);
```

Return type changes from `async Task<(CreateListingResponse, OutgoingMessages)>` to
`async Task<(CreateListingResponse, IStartStream, OutgoingMessages)>`. Wolverine processes
each element of the tuple: `CreateListingResponse` as a reply, `IStartStream` as stream
creation, `OutgoingMessages` for integration messages.

Note: `IDocumentSession` stays because the validation queries (`LoadAsync<ProductSummaryView>`,
`LoadAsync<ListingsActiveView>`) still need it. Use `IQuerySession` instead of `IDocumentSession`
since this handler only reads — it no longer writes to the session directly.

**Commit:** `M39.0 S5b: Listings — CreateListingHandler IStartStream return`

---

## D-3: Listings — 6 Write Handlers → `[WriteAggregate]`

**Files:** `ApproveListing.cs`, `ActivateListing.cs`, `PauseListing.cs`, `ResumeListing.cs`,
`EndListing.cs`, `SubmitListingForReview.cs`

**Current pattern in each (Anti-Pattern #2 + #8):**
```csharp
public static async Task<OutgoingMessages> Handle(ApproveListing command, IDocumentSession session)
{
    var listing = await session.Events.AggregateStreamAsync<Listing>(command.ListingId);
    if (listing is null) throw new InvalidOperationException("Listing not found");
    if (listing.Status != ListingStatus.ReadyForReview) throw new InvalidOperationException("...");
    var @event = new ListingApproved(command.ListingId, now);
    session.Events.Append(command.ListingId, @event);
    var outgoing = new OutgoingMessages();
    outgoing.Add(new IntegrationMessages.ListingApproved(...));
    return outgoing;
}
```

**Why `[WriteAggregate]` works here:** Each command (`ApproveListing`, `ActivateListing`, etc.)
has a `Guid ListingId` property. Wolverine matches `{AggregateName}Id` → `ListingId` for the
`Listing` aggregate. The stream ID is a natural UUID v7 (created by `ListingStreamId.Compute()`
at listing creation and stored as `Listing.Id`). Wolverine resolves it directly.

**Target pattern for all 6 handlers:**
```csharp
public static class ApproveListingHandler
{
    public static ProblemDetails Before(ApproveListing cmd, Listing? listing)
    {
        if (listing is null)
            return new ProblemDetails { Detail = $"Listing '{cmd.ListingId}' not found", Status = 404 };
        if (listing.Status != ListingStatus.ReadyForReview)
            return new ProblemDetails { Detail = $"Cannot approve listing in '{listing.Status}' state. Must be ReadyForReview.", Status = 409 };
        return WolverineContinue.NoProblems;
    }

    public static (Events, OutgoingMessages) Handle(
        ApproveListing cmd,
        [WriteAggregate] Listing listing)
    {
        var now = DateTimeOffset.UtcNow;
        var events = new Events();
        events.Add(new ListingApproved(cmd.ListingId, now));

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ListingApproved(
            cmd.ListingId, listing.Sku, listing.ChannelCode, listing.ProductName,
            /* productSummary?.Category — see note below */
            Price: null, now));

        return (events, outgoing);
    }
}
```

**Note on `ApproveListing` — ProductSummaryView query:** The current `ApproveListing` handler
calls `session.LoadAsync<ProductSummaryView>(listing.Sku)` to populate the `Category` field on
the `ListingApproved` integration message. After switching to `[WriteAggregate]`, there is no
`IDocumentSession` available in `Handle()`. Two options:
- **Option A:** Inject `IQuerySession` in `Handle()` alongside `[WriteAggregate] Listing listing`
  to perform the lookup — this is valid, `IQuerySession` is injectable alongside the aggregate.
- **Option B:** Accept `null` for `Category` in the integration message — the `TODO(M37.0)` comment
  in the existing code indicates this was already a partial implementation.

Choose Option A if straightforward. If it complicates the handler, choose Option B and preserve
the existing `TODO` comment noting the limitation.

**`ActivateListing` — OWN_WEBSITE fast path:** The status validation for `ActivateListing` is
more complex — it permits two valid transitions:
```csharp
var isOwnWebsiteFastPath = listing.Status == ListingStatus.Draft
    && string.Equals(listing.ChannelCode, "OWN_WEBSITE", StringComparison.OrdinalIgnoreCase);
if (listing.Status != ListingStatus.Submitted && !isOwnWebsiteFastPath)
    throw new InvalidOperationException(...);
```
This logic goes into `Before()` as a `ProblemDetails` return. The two-condition check is the same
logic, just expressed as `WolverineContinue.NoProblems` (allow) vs `ProblemDetails` (reject).

**`PauseListing`** — if this handler also exists and accepts a reason string, the compound handler
pattern applies the same way. Check the actual file signature.

**Commit convention (group related handlers):**
- `M39.0 S5c: Listings — ApproveListing + SubmitListingForReview [WriteAggregate] compound handler`
- `M39.0 S5d: Listings — ActivateListing + PauseListing + ResumeListing + EndListing [WriteAggregate] compound handler`

Run `dotnet test Listings.Api.IntegrationTests` after each commit.

---

## D-4: Promotions — `RedeemCoupon` + `RevokeCoupon` (UUID v5 — `Load()` Pattern)

**Files:** `src/Promotions/Promotions/Coupon/RedeemCouponHandler.cs`,
`src/Promotions/Promotions/Coupon/RevokeCouponHandler.cs`

**Why `[WriteAggregate]` cannot be used:** `Coupon.StreamId(cmd.CouponCode)` derives a UUID v5
from the coupon code string. Wolverine cannot compute this hash from the command's `CouponCode`
property — it only supports raw ID passthrough, not hashed derivation.

**Target pattern (same as Pricing's `SetBasePriceHandler`):**

```csharp
public static class RedeemCouponHandler
{
    public static async Task<Coupon?> LoadAsync(
        RedeemCoupon cmd,
        IQuerySession session,
        CancellationToken ct)
    {
        var streamId = Coupon.StreamId(cmd.CouponCode);
        return await session.Events.AggregateStreamAsync<Coupon>(streamId, token: ct);
    }

    public static ProblemDetails Before(RedeemCoupon cmd, Coupon? coupon)
    {
        if (coupon is null)
            return new ProblemDetails { Detail = $"Coupon '{cmd.CouponCode}' not found", Status = 404 };
        if (coupon.Status != CouponStatus.Issued)
            return new ProblemDetails { Detail = $"Cannot redeem coupon '{coupon.Code}' — current status is {coupon.Status}. Only Issued coupons can be redeemed.", Status = 409 };
        return WolverineContinue.NoProblems;
    }

    public static void Handle(
        RedeemCoupon cmd,
        Coupon coupon,            // non-null guaranteed by Before()
        IDocumentSession session)
    {
        var evt = new CouponRedeemed(
            coupon.Id, coupon.Code, coupon.PromotionId,
            cmd.OrderId, cmd.CustomerId, cmd.RedeemedAt);
        session.Events.Append(Coupon.StreamId(cmd.CouponCode), evt);
        // void return — no OutgoingMessages, no Events tuple
    }
}
```

Apply the identical structure to `RevokeCouponHandler` (validation: null check + status must not
be `Revoked` or `Expired`).

**`void` return is correct here:** `RedeemCoupon` and `RevokeCoupon` are internal commands with
no integration events to publish (the integration message is published by the handler that invokes
these, not by these handlers themselves). `void` + explicit `session.Events.Append()` is the right
pattern for a handler that only persists a domain event with no outgoing messages.

**Commits:**
- `M39.0 S5e: Promotions — RedeemCouponHandler Load/Before/Handle compound pattern`
- `M39.0 S5f: Promotions — RevokeCouponHandler Load/Before/Handle compound pattern`

---

## D-5: Promotions — `RecordPromotionRedemption` (Natural UUID — `[WriteAggregate]`)

**File:** `src/Promotions/Promotions/Promotion/RecordPromotionRedemptionHandler.cs`

**Why `[WriteAggregate]` works here:** `RecordPromotionRedemption` contains `Guid PromotionId`.
The `Promotion` aggregate uses natural UUID stream IDs (UUID v7, not v5). Wolverine resolves
`PromotionId` for `Promotion` by the `{AggregateName}Id` convention directly.

**Target pattern:**
```csharp
public static class RecordPromotionRedemptionHandler
{
    public static ProblemDetails Before(RecordPromotionRedemption cmd, Promotion? promotion)
    {
        if (promotion is null)
            return new ProblemDetails { Detail = $"Promotion '{cmd.PromotionId}' not found", Status = 404 };
        if (promotion.Status != PromotionStatus.Active)
            return new ProblemDetails { Detail = $"Cannot record redemption for promotion '{promotion.Id}' — status is {promotion.Status}. Only Active promotions accept redemptions.", Status = 409 };
        if (promotion.UsageLimit.HasValue && promotion.CurrentRedemptionCount >= promotion.UsageLimit.Value)
            return new ProblemDetails { Detail = $"Promotion '{promotion.Id}' usage limit of {promotion.UsageLimit.Value} has been reached.", Status = 409 };
        return WolverineContinue.NoProblems;
    }

    public static Events Handle(
        RecordPromotionRedemption cmd,
        [WriteAggregate] Promotion promotion)
    {
        var events = new Events();
        events.Add(new PromotionRedemptionRecorded(
            promotion.Id, cmd.OrderId, cmd.CustomerId,
            cmd.CouponCode, cmd.RedeemedAt));
        return events;
    }
}
```

The handler comment about optimistic concurrency is still valid — `[WriteAggregate]` uses Marten's
`FetchForWriting` which enforces optimistic concurrency exactly as the existing code describes.
The Wolverine retry policy on `ConcurrencyException` in `Program.cs` handles concurrent redemption
races. This behavior is **preserved**, not changed — just expressed more clearly.

**Commit:** `M39.0 S5g: Promotions — RecordPromotionRedemptionHandler [WriteAggregate] compound handler`

---

## D-6: Vendor Portal — `AutoApplyTransactions()`

**File:** `src/Vendor Portal/VendorPortal.Api/Program.cs`

The Wolverine configuration block (`builder.Host.UseWolverine(opts => { ... })`) does not contain
`opts.Policies.AutoApplyTransactions()`. Add it alongside the other `opts.Policies.*` calls.

After adding, scan the Vendor Portal handlers for any `SaveChangesAsync()` calls that are now
redundant. The BFF is mostly read-heavy, but if any write handlers call `SaveChangesAsync()`
explicitly (e.g., in `VendorProductCatalog` handlers), remove them.

**Commit:** `M39.0 S5h: Vendor Portal — add AutoApplyTransactions(); remove redundant SaveChangesAsync`

---

## Execution Order

```
S5a: Fulfillment RequestFulfillmentHandler (trivial — 5 minutes)
  → dotnet test Fulfillment.Api.IntegrationTests
S5b: Listings CreateListingHandler (IStartStream return)
  → dotnet test Listings.Api.IntegrationTests
S5c: Listings ApproveListing + SubmitListingForReview
  → dotnet test Listings.Api.IntegrationTests
S5d: Listings ActivateListing + PauseListing + ResumeListing + EndListing
  → dotnet test Listings.Api.IntegrationTests
S5e: Promotions RedeemCouponHandler
  → dotnet test Promotions.Api.IntegrationTests
S5f: Promotions RevokeCouponHandler
  → dotnet test Promotions.Api.IntegrationTests
S5g: Promotions RecordPromotionRedemptionHandler
  → dotnet test Promotions.Api.IntegrationTests
S5h: Vendor Portal AutoApplyTransactions + SaveChangesAsync sweep
  → dotnet test VendorPortal.Api.IntegrationTests
Final: dotnet test (full solution) — record all counts for retrospective
```

---

## Mandatory Session Bookends

**First act:** `dotnet build` (0 errors, 19 warnings baseline). Record passing test counts for
Fulfillment, Listings, Promotions, and Vendor Portal as the session baseline.

**Last acts — all required:**

**1. Commit `docs/planning/milestones/m39-0-session-5-retrospective.md`**

Must cover:
- For each deliverable (D-1 through D-6): what changed and any surprises
- Confirmation of `[WriteAggregate]` resolution for Listings write handlers — did Wolverine
  find `ListingId` → `Listing` stream by convention without explicit attribute?
- `ApproveListing` Option A vs B decision for `ProductSummaryView` category lookup
- Confirmation that `RedeemCoupon`/`RevokeCoupon` UUID v5 pattern was handled via `Load()` (same
  reason as Pricing — Wolverine cannot compute the hash)
- Test counts before and after per BC
- Build warnings at session close (should not increase)
- CI run number confirming green
- Explicit statement that S6 (milestone closure) is the only remaining session

**2. Update `CURRENT-CYCLE.md`**

Record S5 progress. Update the Active Milestone section and Last Updated timestamp.

---

## Roles

### @PSA — Principal Software Architect
Primary owner of all deliverables. Work through the targets in the ordered list — the Fulfillment
and Listings `StartStream` items are trivial; the Listings `[WriteAggregate]` refactors are the
most substantive. `ActivateListing.Before()` is the trickiest — translate the two-condition
OWN_WEBSITE fast path into the `ProblemDetails` return pattern clearly.

For D-4 (Promotions `Load()` pattern), check `RedeemCoupon.cs` and `RevokeCoupon.cs` command
records to confirm they have `CouponCode` (not `Code`) before writing `Coupon.StreamId(cmd.CouponCode)`.

### @QAE — QA Engineer
Verify tests after each commit group. The Listings test suite (41 tests) is the one most likely
to surface issues — the `[WriteAggregate]` refactors change how the aggregate is loaded so any
test that bypasses Wolverine's middleware and calls the handler directly may need updating.

---

## Commit Convention

```
M39.0 S5a: Fulfillment — RequestFulfillmentHandler IStartStream return
M39.0 S5b: Listings — CreateListingHandler IStartStream return
M39.0 S5c: Listings — ApproveListing + SubmitListingForReview [WriteAggregate] compound handler
M39.0 S5d: Listings — ActivateListing + PauseListing + ResumeListing + EndListing [WriteAggregate]
M39.0 S5e: Promotions — RedeemCouponHandler Load/Before/Handle
M39.0 S5f: Promotions — RevokeCouponHandler Load/Before/Handle
M39.0 S5g: Promotions — RecordPromotionRedemptionHandler [WriteAggregate] compound handler
M39.0 S5h: Vendor Portal — AutoApplyTransactions + redundant SaveChangesAsync removal
M39.0 S5 retro: docs — session retrospective
M39.0 S5: docs — CURRENT-CYCLE.md update
```

After Session 5, all implementation work in M39.0 is complete. Session 6 is milestone closure only:
retrospective document, CONTEXTS.md assessment, CURRENT-CYCLE.md milestone move.
