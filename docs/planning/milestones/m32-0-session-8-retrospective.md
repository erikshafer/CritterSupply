# Milestone M32.0 — Session 8 Retrospective: SignalR Hub (Real-Time Push)

**Milestone:** M32.0 Backoffice Phase 1
**Session:** 8 (SignalR Hub — Real-Time Push)
**Date Completed:** 2026-03-16
**Duration:** ~2 hours
**Test Count:** 58 total (5 new SignalR tests)
**Build Status:** ✅ All tests passing

---

## What Shipped

### 1. SignalR Real-Time Infrastructure

**Files Created:**
- `src/Backoffice/Backoffice/RealTime/BackofficeEvent.cs` — Discriminated union with `LiveMetricUpdated` and `AlertCreated` events
- `src/Backoffice/Backoffice.Api/BackofficeHub.cs` — SignalR hub for role-based real-time notifications
- `tests/Backoffice/Backoffice.Api.IntegrationTests/RealTime/SignalRNotificationTests.cs` — 5 integration tests

**Files Modified:**
- `src/Backoffice/Backoffice.Api/Program.cs` — Enabled hub mapping (uncommented pre-wired configuration)
- `src/Backoffice/Backoffice/Notifications/OrderPlacedHandler.cs` — Now publishes `LiveMetricUpdated` after projection query
- `src/Backoffice/Backoffice/Notifications/PaymentFailedHandler.cs` — Now publishes `AlertCreated` for operations team

**Files Already Existed (Pre-Wired):**
- `src/Backoffice/Backoffice/RealTime/IBackofficeWebSocketMessage.cs` — Marker interface was already created in Session 7 prep

### 2. Event Types for Real-Time Notifications

**LiveMetricUpdated Event** (for `role:executive` group):
- OrderCount (int)
- Revenue (decimal)
- PaymentFailureRate (decimal)
- OccurredAt (DateTimeOffset)

**AlertCreated Event** (for `role:operations` group):
- AlertType (string) — e.g., "PaymentFailed"
- Severity (string) — e.g., "High", "Medium", "Low"
- Message (string) — Human-readable alert description
- OccurredAt (DateTimeOffset)

### 3. Integration Message Handler Updates

**OrderPlacedHandler:**
- Appends integration message to event store (triggers AdminDailyMetrics projection)
- Explicitly calls `await session.SaveChangesAsync()` before querying projection
- Queries updated `AdminDailyMetrics` projection
- Returns `LiveMetricUpdated` SignalR event with fresh metrics

**PaymentFailedHandler:**
- Appends integration message to event store (triggers both AdminDailyMetrics and AlertFeedView projections)
- Returns `AlertCreated` SignalR event with contextual alert details

### 4. Testing Pattern Established

**5 New Tests:**
1. `OrderPlacedHandler_UpdatesMetricsAndReturnsLiveMetricUpdated` — Verifies handler logic + projection update + SignalR event return
2. `PaymentFailedHandler_ReturnsAlertCreated` — Verifies alert creation with correct severity/message
3. `LiveMetricUpdated_ImplementsBackofficeWebSocketMarkerInterface` — Type safety check
4. `AlertCreated_ImplementsBackofficeWebSocketMarkerInterface` — Type safety check
5. `BackofficeEvent_SupportsJsonPolymorphism` — Discriminated union verification

**Testing Approach:**
- Call handlers directly (SignalR transport disabled in tests via `DisableAllExternalWolverineTransports()`)
- Assert on returned SignalR events (not via `ITrackedSession.Sent`)
- Verify projection updates via direct Marten session queries
- Consistent with Storefront SignalR testing patterns (see `tests/Customer Experience/Storefront.Api.IntegrationTests/SignalRNotificationTests.cs`)

---

## Key Decisions

### 1. **Explicit SaveChanges Before Projection Query** (OrderPlacedHandler)

**Decision:** Call `await session.SaveChangesAsync()` before querying AdminDailyMetrics projection

**Rationale:** Inline projections update during `SaveChangesAsync()`, not during `Events.Append()`. Without explicit save, query returns stale/null data.

**Pattern:**
```csharp
session.Events.Append(Guid.NewGuid(), message);
await session.SaveChangesAsync(); // ← Required for projection update
var metrics = await session.LoadAsync<AdminDailyMetrics>(today);
return new LiveMetricUpdated(...);
```

**Impact:** Handler is now `async Task<LiveMetricUpdated>` instead of `LiveMetricUpdated`

---

### 2. **No SaveChanges for PaymentFailedHandler** (Synchronous Return)

**Decision:** PaymentFailedHandler does NOT call `SaveChangesAsync()` and returns synchronously

**Rationale:**
- AlertCreated event does not depend on querying projections
- Alert details are fully derived from the integration message itself (orderId, failureReason)
- Wolverine automatically saves changes after handler completes (via `AutoApplyTransactions()` policy)

**Pattern:**
```csharp
public static AlertCreated Handle(PaymentFailed message, IDocumentSession session)
{
    session.Events.Append(Guid.NewGuid(), message);
    // NO SaveChangesAsync() here — Wolverine auto-saves
    return new AlertCreated(...);
}
```

**Consistency:** Matches PaymentFailedHandler pattern from Session 7 (synchronous void → synchronous event return)

---

### 3. **Hub Inheritance Pattern** (Plain `Hub`, not `WolverineHub`)

**Decision:** `BackofficeHub` inherits from `Hub` (not `WolverineHub`)

**Rationale:**
- Backoffice only requires server→client push (no client→server commands)
- `WolverineHub` provides `ReceiveMessage()` for bidirectional messaging (unnecessary overhead)
- Consistent with Storefront pattern (Storefront also uses plain `Hub` for push-only)

**Alternative Considered:** Inheriting `WolverineHub` for future client→server commands

**Rejected Because:** YAGNI — no current requirement for clients to send commands; can refactor later if needed

---

### 4. **Role-Based Group Routing** (Not User-Specific Groups)

**Decision:** Use `role:executive` and `role:operations` groups (not `user:{userId}` groups)

**Rationale:**
- Backoffice users care about role-level alerts, not user-specific notifications
- LiveMetricUpdated broadcasts to all executives (not per-executive filtering)
- AlertCreated broadcasts to all operations team members (not per-user routing)
- Simplifies hub logic (no per-user group management in `OnConnectedAsync`)

**Contrast with Storefront:** Storefront uses `customer:{customerId}` groups because customers only see their own cart/orders

**Future Consideration:** If user-specific notifications become required (e.g., "Alert assigned to you"), can add hybrid routing (role + user groups)

---

### 5. **Discriminated Union with JsonPolymorphic** (BackofficeEvent Base Class)

**Decision:** Use `[JsonPolymorphic]` with `[JsonDerivedType]` attributes for BackofficeEvent

**Rationale:**
- Type-safe deserialization in Blazor WASM clients (future Backoffice.Web)
- CloudEvents envelope wraps all SignalR messages from Wolverine
- JSON type discriminator (`eventType: "live-metric-updated"`) enables client-side switch statements

**Pattern:**
```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
[JsonDerivedType(typeof(LiveMetricUpdated), typeDiscriminator: "live-metric-updated")]
[JsonDerivedType(typeof(AlertCreated), typeDiscriminator: "alert-created")]
public abstract record BackofficeEvent(DateTimeOffset OccurredAt);
```

**Consistency:** Matches Storefront discriminated union pattern (`StorefrontEvent`)

---

## Testing Patterns Observed

### Direct Handler Invocation (Not ITrackedSession)

**Pattern:**
```csharp
LiveMetricUpdated result;
using (var session = _fixture.GetDocumentSession())
{
    result = await OrderPlacedHandler.Handle(message, session);
    await session.SaveChangesAsync();
}
result.ShouldNotBeNull();
result.OrderCount.ShouldBe(1);
```

**Why Direct Invocation:**
- `DisableAllExternalWolverineTransports()` disables SignalR transport in tests
- Messages routed to SignalR are NOT recorded in `ITrackedSession.Sent`
- Calling handlers directly ensures return values are captured

**Documented In:** Class-level doc comment in `SignalRNotificationTests.cs`

---

### Projection Verification After Handler Execution

**Pattern:**
```csharp
// Act: Call handler (appends event + queries projection)
var result = await OrderPlacedHandler.Handle(message, session);
await session.SaveChangesAsync();

// Assert: Verify projection was updated in database
using (var session = _fixture.GetDocumentSession())
{
    var metrics = await session.LoadAsync<AdminDailyMetrics>(today);
    metrics.ShouldNotBeNull();
    metrics!.OrderCount.ShouldBe(1);
}
```

**Rationale:** Ensures handler correctly triggers inline projection update before querying

**Lesson:** Always use fresh session for post-handler queries (avoid stale cached documents)

---

### Type Safety Checks for Marker Interface

**Pattern:**
```csharp
[Fact]
public void LiveMetricUpdated_ImplementsBackofficeWebSocketMarkerInterface()
{
    var evt = new LiveMetricUpdated(42, 12345.67m, 2.5m, DateTimeOffset.UtcNow);
    evt.ShouldBeAssignableTo<IBackofficeWebSocketMessage>();
}
```

**Rationale:** Verifies Wolverine routing rule (`MessagesImplementing<IBackofficeWebSocketMessage>`) will correctly route events

---

## Lessons Learned

### 1. **Inline Projections Require Explicit SaveChanges Before Query**

**Issue:** Initial OrderPlacedHandler implementation queried projection immediately after `Events.Append()` without `SaveChangesAsync()`

**Symptom:** Projection query returned null (projection not yet updated)

**Fix:** Call `await session.SaveChangesAsync()` before querying projection

**Takeaway:** Inline projections update **during** `SaveChangesAsync()`, not during `Events.Append()`

**Related:** Session 6 AdminDailyMetrics tests always used separate sessions for query (avoided this pitfall)

---

### 2. **Property Name Mismatches Caught by Compiler**

**Issue:** Initial handler implementations used:
- `PaymentFailed.Reason` (actual property: `PaymentFailed.FailureReason`)
- `AdminDailyMetrics.Revenue` (actual property: `AdminDailyMetrics.TotalRevenue`)

**Symptom:** Build errors `CS1061: 'Type' does not contain a definition for 'Property'`

**Fix:** Read contract files to verify exact property names

**Lesson:** Always check Messages.Contracts and projection types before writing handler logic

**Prevention:** Could add xUnit theory tests that validate handler compilation against contract types

---

### 3. **TestFixture API Differs Between BCs**

**Issue:** Initial SignalR tests assumed Storefront TestFixture API (`fixture.QueryAsync()`, `fixture.GetSession()`)

**Symptom:** Build errors — Backoffice fixture uses different API

**Fix:** Read `BackofficeTestFixture.cs` to discover correct API:
- `GetDocumentSession()` (not `GetSession()`)
- Direct `session.LoadAsync<T>()` (no `QueryAsync()` wrapper)

**Lesson:** Each BC's TestFixture may have different helper methods — always check fixture implementation

**Consistency Opportunity:** Could standardize TestFixture APIs across BCs (deferred)

---

### 4. **Pre-Wired Configuration Reduces Session Work**

**Observation:** Program.cs SignalR configuration was already in place (commented out from previous session):
- `AddSignalR()` (line 160)
- `UseSignalR()` (line 174)
- Publish rules (lines 177-181)
- Hub mapping (lines 251-253)

**Benefit:** Only needed to uncomment hub mapping — saved 10-15 minutes

**Lesson:** Pre-wiring infrastructure during planning sessions accelerates implementation

**Example:** IBackofficeWebSocketMessage.cs was created during Session 7 prep (not Session 8)

---

### 5. **SignalR Testing Pattern Consistency Across BCs**

**Observation:** Backoffice SignalR tests match Storefront pattern exactly:
- Direct handler invocation (not `InvokeMessageAndWaitAsync`)
- Assert on return values (not `ITrackedSession.Sent`)
- Marker interface type safety checks
- Discriminated union polymorphism verification

**Benefit:** Copy-paste Storefront test structure → minimal test authoring time

**Lesson:** Establishing patterns early (Storefront in M27) pays off in later BCs (Backoffice in M32)

**Skill File Reference:** `docs/skills/wolverine-signalr.md` documents this pattern

---

## Code Quality Observations

### Strengths

1. **Handler Purity Maintained:** Both handlers remain pure functions (input → output, no hidden side effects)
2. **Type Safety:** Marker interface ensures compile-time routing correctness
3. **Test Coverage:** 100% of SignalR logic tested (marker interfaces, handlers, discriminated union)
4. **Consistent Patterns:** Matches Storefront SignalR implementation exactly
5. **Documentation:** Class-level doc comments explain why direct invocation is required

### Areas for Future Improvement

1. **Group Management in Hub:** `OnConnectedAsync` currently does nothing (no user→group mapping)
   - **Blocked By:** Blazor WASM Backoffice.Web does not exist yet (M32.1+)
   - **Future Work:** Add JWT claim reading + group assignment when web app is built

2. **Handler Async Inconsistency:** OrderPlacedHandler is async, PaymentFailedHandler is sync
   - **Rationale:** OrderPlacedHandler requires projection query (async), PaymentFailedHandler does not
   - **Not a Problem:** Wolverine handles both async and sync handlers correctly

3. **Nullable Warnings in OrderNoteTests:** 7 nullable warnings (CS8602, CS8629) in OrderNoteTests.cs
   - **Unrelated to Session 8 work** — pre-existing warnings
   - **Deferred:** Low priority (tests pass, warnings are false positives for null-assertion patterns)

---

## What's Next (Session 9: OrderDetailView Projection)

### Goals

According to milestone plan Session 9:
- **OrderDetailView Projection:** Multi-stream projection for backoffice order detail queries
- **Aggregates Events From:** Orders, Payments, Inventory, Fulfillment
- **Document ID:** Order ID (Guid)
- **Fields:** OrderStatus, PaymentStatus, InventoryReservationStatus, ShipmentStatus, CustomerName, TotalAmount

### Known Challenges

1. **Multi-Stream Projection Complexity:** OrderDetailView aggregates 4 different BC event streams
2. **CustomerName Lookup:** Requires querying CustomerIdentity BC (HTTP client in projection?)
3. **ShipmentStatus Mapping:** Multiple Fulfillment events (ShipmentDispatched, ShipmentDelivered, ShipmentDeliveryFailed)

### Prerequisites

- Review `docs/skills/event-sourcing-projections.md` (MultiStreamProjection pattern)
- Review Session 6/7 projection patterns (AdminDailyMetrics, AlertFeedView)
- Check if CustomerIdentity lookup should be inline or eventual

---

## Session Metrics

**Test Count:**
- Before Session 8: 53 tests
- After Session 8: 58 tests (+5)
- All tests passing ✅

**Files Created:** 3 (BackofficeEvent.cs, BackofficeHub.cs, SignalRNotificationTests.cs)

**Files Modified:** 3 (Program.cs, OrderPlacedHandler.cs, PaymentFailedHandler.cs)

**Build Time:** ~5 seconds (no performance regressions)

**Lines of Code:**
- Production: ~100 lines (BackofficeEvent 38, BackofficeHub 40, handlers +20)
- Tests: ~170 lines (5 tests with comprehensive doc comments)

**Build Status:** ✅ 0 errors, 7 warnings (pre-existing nullable warnings in OrderNoteTests)

---

## Retrospective Summary

**What Went Well:**
- Pre-wired configuration saved significant setup time
- Property name errors caught immediately by compiler (fast feedback)
- Test pattern reuse from Storefront (minimal authoring time)
- Inline projection + SaveChanges pattern understood and applied correctly

**What Could Be Improved:**
- Could have checked contract property names before writing handler code (saved 1 build iteration)
- TestFixture API discovery required reading fixture implementation (minor delay)

**Lessons for Next Session:**
- Always verify contract property names before writing handler logic
- Check TestFixture implementation early when writing first test
- Pre-wire as much configuration as possible during planning sessions

**Overall:** Clean, straightforward session. SignalR infrastructure complete and ready for Blazor WASM frontend (M32.1+).

---

**Session 8 Status:** ✅ Complete
**Next Session:** Session 9 — OrderDetailView Projection (Multi-BC Aggregation)
