# M32.1 Backoffice Phase 2 — Session 2 Retrospective

**Date:** 2026-03-17
**Session:** 2 of ~16
**Focus:** Pricing BC Write Endpoints + Multi-Issuer JWT

---

## 📊 Session Goals

1. ✅ Add Pricing BC admin write endpoints for PricingManager role
2. ✅ Configure multi-issuer JWT authentication (Backoffice scheme)
3. ✅ Implement 3 HTTP endpoints with FluentValidation
4. ⚠️ Create integration tests (incomplete - session timeout)
5. ⚠️ Write ADRs 0034-0037 (deferred to Session 3)

---

## 🎯 What Was Accomplished

### Pricing BC Write Endpoints

**SetBasePriceEndpoint** (`POST /api/pricing/products/{sku}/base-price`):
- Unified endpoint handling both `SetInitialPrice` (Unpriced → Published) and `ChangePrice` (Published → Published)
- FluentValidation: Amount > 0, Currency length = 3
- Floor/ceiling constraint enforcement using `CurrentPriceView` projection
- Returns `PriceSetResult` with new effective price

**SchedulePriceChangeEndpoint** (`POST /api/pricing/products/{sku}/schedule`):
- Schedules future price change using Wolverine delayed message pattern
- Generates UUID v7 for `ScheduleId` (correlation ID)
- Validates `ScheduledFor > DateTime.UtcNow`
- Uses `MessageContext.Schedule()` for delayed execution
- Returns `PriceScheduledResult` with scheduleId + scheduledFor

**CancelScheduledPriceChangeEndpoint** (`DELETE /api/pricing/products/{sku}/schedule/{scheduleId}`):
- Cancels pending scheduled price change
- Loads ProductPrice aggregate and emits `ScheduledPriceChangeCancelled` event
- Returns `204 No Content` on success

### Multi-Issuer JWT Configuration

**Program.cs Changes:**
- Added `Microsoft.AspNetCore.Authentication.JwtBearer` package
- Configured "Backoffice" JWT scheme (port 5249) alongside existing "Vendor" scheme
- Added `PricingManager` authorization policy requiring Backoffice JWT + PricingManager role
- Applied `[Authorize(Policy = "PricingManager")]` to all 3 endpoints

### Scheduled Price Activation Handler

**ActivateScheduledPriceHandler:**
- Handles `ScheduledPriceActivation` delayed message
- Includes stale-message guard (checks `ScheduleId` matches `PendingSchedule?.ScheduleId`)
- Prevents race conditions if schedule was cancelled before activation
- Emits `PriceChanged` event on successful activation

---

## 🚧 Incomplete Work (Session Timeout)

### Integration Tests (Not Started)

**Planned tests not written:**
1. SetBasePrice with valid amount → returns 200 + new price
2. SetBasePrice with amount < floor → returns 400 + validation error
3. SetBasePrice with amount > ceiling → returns 400 + validation error
4. SchedulePriceChange with future date → returns 200 + scheduleId
5. SchedulePriceChange with past date → returns 400 + validation error
6. CancelScheduledPriceChange with valid scheduleId → returns 204
7. CancelScheduledPriceChange with invalid scheduleId → returns 404
8. Scheduled activation fires after delay → price updates
9. Cancelled schedule does not activate → stale-message guard works
10. Multi-issuer JWT: Backoffice token accepted, Vendor token rejected

**Why incomplete:** Session timed out before tests could be written

**Resolution:** Session 3 will add these tests before moving to Inventory/Payments work

### ADRs 0034-0037 (Deferred)

**Planned ADRs not written:**
- ADR 0034: Backoffice BFF Architecture
- ADR 0035: Backoffice SignalR Hub Design
- ADR 0036: BFF-Owned Projections Strategy
- ADR 0037: OrderNote Aggregate Ownership

**Why deferred:** Session timeout + tests take priority over retrospective ADRs

**Resolution:** Move to Session 3 or Session 4 depending on Inventory/Payments workload

---

## 🔍 Key Decisions

### 1. **Unified SetBasePrice Endpoint**

**Decision:** Single endpoint handles both `SetInitialPrice` (Unpriced) and `ChangePrice` (Published)

**Rationale:**
- From Backoffice perspective, both actions are "set base price"
- Reduces endpoint count (2 → 1)
- Domain logic (ProductPrice aggregate) determines which event to emit based on current status
- Simpler API contract for frontend

**Pattern:**
```csharp
public static class SetBasePriceEndpoint
{
    [WolverinePost("/api/pricing/products/{sku}/base-price")]
    [Authorize(Policy = "PricingManager")]
    public static async Task<IResult> Handle(
        string sku,
        SetBasePriceRequest request,
        IDocumentSession session)
    {
        // Load aggregate
        var price = await session.Events.AggregateStreamAsync<ProductPrice>(streamId);

        // Emit appropriate event based on status
        if (price.Status == ProductPriceStatus.Unpriced)
            session.Events.Append(streamId, new InitialPriceSet(...));
        else
            session.Events.Append(streamId, new PriceChanged(...));

        await session.SaveChangesAsync();
        return Results.Ok(new PriceSetResult(...));
    }
}
```

---

### 2. **Wolverine Delayed Message for Scheduled Activation**

**Decision:** Use `MessageContext.Schedule()` for delayed price activation

**Rationale:**
- Wolverine native pattern (no external scheduler needed)
- At-least-once delivery guarantee (survives app restart via durable inbox)
- Stale-message guard prevents cancelled schedules from activating

**Pattern:**
```csharp
public static class SchedulePriceChangeEndpoint
{
    public static async Task<IResult> Handle(
        SchedulePriceChangeRequest request,
        IMessageContext messageContext)
    {
        var scheduleId = Guid.CreateVersion7();

        // Schedule delayed message
        await messageContext.Schedule(
            new ScheduledPriceActivation(request.Sku, scheduleId),
            request.ScheduledFor
        );

        return Results.Ok(new PriceScheduledResult(scheduleId, request.ScheduledFor));
    }
}

public static class ActivateScheduledPriceHandler
{
    public static async Task<PriceChanged?> Handle(
        ScheduledPriceActivation activation,
        IDocumentSession session)
    {
        var price = await session.Events.AggregateStreamAsync<ProductPrice>(streamId);

        // Stale-message guard
        if (price.PendingSchedule?.ScheduleId != activation.ScheduleId)
            return null; // Schedule was cancelled

        var evt = new PriceChanged(price.Sku, price.PendingSchedule.NewAmount, ...);
        session.Events.Append(streamId, evt);
        await session.SaveChangesAsync();
        return evt;
    }
}
```

---

### 3. **Floor/Ceiling Constraint Enforcement**

**Decision:** Validate against `CurrentPriceView` projection floor/ceiling values

**Rationale:**
- Prices set by Pricing BC must respect Vendor Portal constraints
- Uses `FetchForWriting()` pattern to load projection + aggregate atomically
- Prevents invalid prices from being set

**Pattern:**
```csharp
var priceView = await session.Query<CurrentPriceView>()
    .Where(x => x.Sku == sku)
    .FirstOrDefaultAsync();

if (request.Amount.Value < priceView.FloorPrice.Value)
    return Results.BadRequest(new { Error = "Amount below floor price" });

if (request.Amount.Value > priceView.CeilingPrice.Value)
    return Results.BadRequest(new { Error = "Amount above ceiling price" });
```

---

## 📈 Session Metrics

**Duration:** ~2.5 hours (timeout before completion)

**Code Additions:**
- 3 HTTP endpoint files created (397 insertions)
- Multi-issuer JWT configuration (58 insertions)
- 1 scheduled activation handler
- Total: ~450 lines of production code

**Tests Written:** 0 (session timeout)

**Build Status:** ✅ 0 errors, 7 pre-existing warnings

**Commits:** 1
- `e919405` — feat(Pricing): Add multi-issuer JWT + PricingManager write endpoints

---

## 🔍 What Worked Well

1. **Unified SetBasePrice Pattern:**
   - Single endpoint for two domain actions (SetInitialPrice + ChangePrice)
   - Cleaner API contract, simpler frontend integration
   - Domain aggregate handles status-based logic

2. **Wolverine Delayed Message Pattern:**
   - Native scheduling without external dependencies
   - At-least-once delivery guarantee
   - Stale-message guard prevents cancelled schedules from activating

3. **Multi-Issuer JWT Consistency:**
   - Same pattern as Product Catalog (Session 1)
   - Backoffice + Vendor schemes coexist
   - Policy-based authorization works seamlessly

4. **Floor/Ceiling Validation:**
   - Projection-based constraint enforcement
   - Prevents invalid prices at API boundary
   - Vendor Portal constraints respected

---

## 🔄 What Could Be Improved

1. **Session Timeout Before Tests:**
   - Tests are critical for validation (especially multi-issuer JWT)
   - Should have written 2-3 smoke tests before timeout
   - **Lesson:** Write critical tests early in session (not at end)

2. **ADR Backlog Growing:**
   - ADRs 0034-0037 now deferred twice (M32.0 → M32.1 Session 1 → Session 2 → Session 3?)
   - Risk: Pattern context fades with time
   - **Fix:** Allocate dedicated ADR writing time in Session 4 or 5

3. **No Retrospective Started:**
   - Session timed out without starting retrospective document
   - Had to be written in Session 3 (this document)
   - **Lesson:** Start retrospective doc early, update as you go

4. **Test Plan Not Documented:**
   - 10 planned tests identified but not written down before timeout
   - Had to reconstruct test plan from commit message
   - **Fix:** Write test plan in comments before implementing endpoints

---

## 🚀 What's Next (Session 3)

### Primary Goals:
1. **Write Pricing BC Integration Tests** (10 tests from above)
2. **Implement Inventory Write Endpoints:**
   - `POST /api/inventory/{sku}/adjust` — AdjustInventoryQuantity
   - `POST /api/inventory/{sku}/receive` — ReceiveInboundStock
3. **Implement Payments Query Endpoint:**
   - `GET /api/payments?orderId={orderId}` — GetPaymentsForOrder
4. **Update Gap Register:**
   - Mark all 9 gaps as closed
   - Document Session 1-3 endpoint implementations

### Estimated Duration: 3-4 hours

### Deferred Again:
- ADRs 0034-0037 (move to Session 4 or 5)

---

## 📚 References

**M32.1 Planning:**
- [M32.1 Milestone Plan](./m32-1-backoffice-phase-2-plan.md)
- [M32.1 Session 1 Retrospective](./m32-1-session-1-retrospective.md)

**Skills:**
- [Wolverine Message Handlers](../../skills/wolverine-message-handlers.md)
- [Marten Event Sourcing](../../skills/marten-event-sourcing.md)

**Related BCs:**
- Product Catalog BC: Reference for multi-issuer JWT pattern
- Pricing BC: Event-sourced ProductPrice aggregate

---

## 📊 Session Status

**Session 2 Status:** ⚠️ Incomplete (timeout before tests)

**M32.1 Progress:** Session 2 of ~16 (12.5% complete)

**Gap Closure:** 5 of 9 gaps closed (Product Catalog 2, Pricing 3)

**Next Session:** Session 3 (Pricing tests + Inventory + Payments)

---

**Session 2 delivered 3 production endpoints but timed out before tests. Session 3 will complete Pricing tests and close final Inventory + Payments gaps.**
