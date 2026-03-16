# M32.0 Session 7 Retrospective: AlertFeedView BFF Projection

**Session:** 7 of 11
**Date:** 2026-03-16
**Duration:** ~2 hours (core: 1h, tests: 1h)
**Branch:** `claude/m32-0-session-6-retrospective`
**Commits:** 2

---

## ✅ What Shipped

### AlertFeedView Projection Infrastructure

**AlertFeedView Projection:**
- ✅ `AlertFeedView.cs` — Guid-keyed document model with AlertType and AlertSeverity enums
- ✅ `AlertFeedViewProjection.cs` — MultiStreamProjection handling 4 alert types
- ✅ 4 alert types from different BCs:
  - LowStockDetected (Inventory BC) → Warning/Critical severity
  - ShipmentDeliveryFailed (Fulfillment BC) → Critical severity
  - PaymentFailed (Payments BC) → Warning/Critical based on IsRetriable
  - ReturnExpired (Returns BC) → Info severity
- ✅ Projection registered in Program.cs with inline lifecycle (zero lag)
- ✅ Severity logic implemented based on event data

**Integration Message Handlers:**
- ✅ LowStockDetectedHandler.cs → appends to event store
- ✅ ShipmentDeliveryFailedHandler.cs → appends to event store
- ✅ ReturnExpiredHandler.cs → appends to event store
- ✅ PaymentFailedHandler.cs → already existed from Session 6

**RabbitMQ Queue Configuration:**
- ✅ 3 new queues configured in Program.cs:
  - `backoffice-low-stock-detected`
  - `backoffice-shipment-delivery-failed`
  - `backoffice-return-expired`
- ✅ 1 existing queue reused: `backoffice-payment-failed` (from Session 6)
- ✅ All queues set to ProcessInline() for immediate projection updates

**HTTP Query Endpoint:**
- ✅ GetAlertFeed.cs — Query alerts with severity filtering and pagination
- ✅ AlertFeedResponse/AlertDto — Response DTOs for alert feed
- ✅ Query parameters: severity (optional), limit (optional, default 50, max 100)
- ✅ Returns unacknowledged alerts ordered by CreatedAt descending (newest first)
- ✅ Requires OperationsManager authorization policy

**Integration Tests:**
- ✅ 12 comprehensive tests covering:
  - All 4 alert types (LowStock, DeliveryFailed, PaymentFailed, ReturnExpired)
  - Severity mapping verification (Warning/Critical/Info)
  - Multiple alerts aggregation
  - Ordering (newest first)
  - Severity filtering
  - ContextData JSON serialization
  - Direct Marten document queries
- ✅ All 53 tests passing (41 from Session 6 + 12 new)

---

## 📊 Build & Test Status

**Build:** ✅ 0 errors, 7 pre-existing warnings (OrderNoteTests nullable warnings — existed before Session 7)

**Tests:** ✅ 53 passing
- 10 AdminDailyMetrics (Session 6)
- 12 AlertFeedView (Session 7)
- 31 existing (OrderNote + CS workflows)

**Test Coverage:**
- LowStock alert creation (Warning if quantity > 0, Critical if quantity = 0)
- ShipmentDeliveryFailed alert (always Critical)
- PaymentFailed alert (Warning if retriable, Critical if not)
- ReturnExpired alert (always Info)
- Multiple alerts from same/different types
- Alert ordering (newest first)
- Severity filtering
- ContextData JSON serialization

---

## 🎯 Key Decisions

### 1. **Guid-Keyed Documents (Not Date-Keyed)**

**Decision:** Use Guid document IDs for AlertFeedView (unlike AdminDailyMetrics which uses date keys)

**Rationale:**
- Each alert is a distinct entity requiring individual tracking
- Acknowledgment workflow needs alert-specific operations (acknowledge, dismiss)
- Multiple alerts can occur at the same timestamp
- Guid provides natural uniqueness without timestamp collision concerns

**Implementation:**
```csharp
public sealed class AlertFeedViewProjection : MultiStreamProjection<AlertFeedView, Guid>
{
    public AlertFeedViewProjection()
    {
        Identity<LowStockDetected>(x => Guid.NewGuid());
        Identity<ShipmentDeliveryFailed>(x => Guid.NewGuid());
        // ...
    }
}
```

**Benefits:**
- Each alert has unique ID for acknowledgment tracking
- No ID collision concerns
- Future-proof for alert lifecycle features (snooze, escalate, assign)

---

### 2. **Severity Mapping Based on Event Data**

**Decision:** Calculate severity in projection Create() methods based on event properties

**Rationale:**
- LowStock: Critical if quantity = 0 (stockout), Warning if quantity > 0 (low but available)
- DeliveryFailed: Always Critical (customer impact, immediate action needed)
- PaymentFailed: Warning if retriable (temporary issue), Critical if not (requires customer contact)
- ReturnExpired: Always Info (no immediate action, informational only)

**Implementation:**
```csharp
public AlertFeedView Create(LowStockDetected evt)
{
    return new AlertFeedView
    {
        Id = Guid.NewGuid(),
        AlertType = AlertType.LowStock,
        Severity = evt.CurrentQuantity == 0 ? AlertSeverity.Critical : AlertSeverity.Warning,
        // ...
    };
}
```

**Benefits:**
- Severity reflects business impact
- Operations team can filter by severity (Critical-only view)
- Aligns with real-world triage workflows

---

### 3. **ContextData JSON Field for Flexibility**

**Decision:** Add ContextData string field storing JSON-serialized event-specific details

**Rationale:**
- Each alert type has different relevant context (SKU+warehouse, orderId+shipmentId, etc.)
- Avoid alert-type-specific fields in document model
- Enable rich client-side rendering without schema changes
- Future-proof for additional alert types

**Implementation:**
```csharp
ContextData = System.Text.Json.JsonSerializer.Serialize(new
{
    evt.Sku,
    evt.WarehouseId,
    evt.CurrentQuantity,
    evt.ThresholdQuantity
})
```

**Benefits:**
- Single document schema for all alert types
- Extensible without migrations
- Client can parse and render alert-type-specific details

---

### 4. **Acknowledgment Tracking Fields (Deferred)**

**Decision:** Add AcknowledgedBy and AcknowledgedAt fields to document model, but defer acknowledgment workflow to Session 9

**Rationale:**
- Document model prepared for acknowledgment feature
- Session 7 focused on alert *creation* and *querying*
- Session 9 (Warehouse Clerk Dashboard) will implement acknowledgment command

**Implementation:**
```csharp
public sealed record AlertFeedView
{
    // ... core fields ...
    public Guid? AcknowledgedBy { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; init; }
}
```

**Benefits:**
- No schema migration needed when Session 9 implements acknowledgment
- Query endpoint already filters for unacknowledged alerts (AcknowledgedBy == null)
- Forward-thinking design

---

## 🧪 Testing Patterns

### 1. **Alert Type Coverage (4 Tests)**

**Pattern:** One test per alert type, verifying Create() method and severity mapping

**Examples:**
- `LowStockDetected_CreatesAlert_WithWarningSeverity_WhenQuantityAboveZero`
- `LowStockDetected_CreatesAlert_WithCriticalSeverity_WhenQuantityZero`
- `ShipmentDeliveryFailed_CreatesAlert_WithCriticalSeverity`
- `PaymentFailed_CreatesAlert_WithWarningSeverity_WhenRetriable`
- `PaymentFailed_CreatesAlert_WithCriticalSeverity_WhenNotRetriable`
- `ReturnExpired_CreatesAlert_WithInfoSeverity`

**Validates:**
- Correct AlertType enum value
- Correct Severity calculation
- OrderId populated (if applicable)
- Message contains relevant event details
- ContextData JSON serialized correctly

---

### 2. **Multi-Alert Aggregation (2 Tests)**

**Pattern:** Append multiple alert events, verify separate documents created

**Examples:**
- `MultipleAlerts_FromDifferentTypes_CreatesMultipleDocuments` (4 alerts, 4 types)
- `MultipleLowStockAlerts_CreatesSeparateDocuments` (3 alerts, same type)

**Validates:**
- Each alert gets unique Guid document ID
- No collision between same-type alerts
- All alert types present in query results

---

### 3. **Query Behavior Tests (3 Tests)**

**Pattern:** Verify ordering, filtering, and direct Marten queries

**Examples:**
- `AlertsOrdering_NewestFirst_CanBeQueriedCorrectly`
- `AlertsFiltering_BySeverity_WorksCorrectly`
- `ProjectionDocument_CanBeQueriedDirectly`

**Validates:**
- OrderByDescending(a => a.CreatedAt) sorts correctly
- Severity filtering works for Info/Warning/Critical
- Direct Marten session.Query<AlertFeedView>() works

---

### 4. **ContextData Serialization Test (1 Test)**

**Pattern:** Verify JSON serialization of event-specific context

**Example:**
- `AlertContextData_SerializesCorrectly`

**Validates:**
- ContextData contains JSON-serialized fields
- All event properties present in JSON
- JSON format parseable by client

---

## 📝 Lessons Learned

### What Went Perfectly

1. **Pattern Reuse from Session 6**
   - Same BFF-owned projection pattern
   - Same integration message handler pattern (append to event store)
   - Same inline projection lifecycle
   - **Result:** Core implementation took only 1 hour (vs 1.5h for Session 6)

2. **Integration Message Contracts Already Defined**
   - All 4 message types existed in `Messages.Contracts/`
   - No contract changes needed
   - Handlers just appended existing messages
   - **Result:** Zero contract-related issues

3. **Test Infrastructure Mature**
   - BackofficeTestFixture well-established
   - Alba + TestContainers + xUnit patterns clear
   - Copy-paste from AdminDailyMetricsTests accelerated test writing
   - **Result:** All 12 tests passing on first run

4. **Build Quality Maintained**
   - Zero new warnings introduced
   - All tests green
   - No IQueryable type issues (learned from Session 6)
   - **Result:** Build succeeded immediately after test creation

### What Was Unexpected

1. **PaymentFailed Handler Already Existed**
   - Session 6 created `PaymentFailedHandler` for AdminDailyMetrics
   - Session 7 reused same handler for AlertFeedView
   - **Discovery:** Both projections subscribe to same RabbitMQ message
   - **Insight:** Integration handlers can serve multiple projections (appending to event store makes events available to all inline projections)

2. **Identity<T>() with Guid.NewGuid() Just Works**
   - Initially concerned about Guid generation timing
   - Marten calls Identity<T>() function per event, generating unique Guid each time
   - **Validation:** 3 LowStock events → 3 separate Guid-keyed documents
   - **Confidence:** MultiStreamProjection<TDoc, Guid> pattern confirmed

3. **Severity Enum Ordering Matters for Queries**
   - Defined enum as: `Info = 0, Warning = 1, Critical = 2`
   - Enables severity-based sorting (Critical first: OrderByDescending(Severity))
   - **Bonus:** Enum values align with business priority

### What Could Be Improved

1. **No HTTP Endpoint Tests (Same as Session 6)**
   - GetAlertFeed endpoint has `[Authorize(Policy = "OperationsManager")]`
   - BackofficeTestFixture provides "cs-agent" role (CustomerService, not Operations)
   - **Deferred:** HTTP authorization testing to Session 9 (when role configuration matters)

2. **Hardcoded Message Templates**
   - Alert messages built via string interpolation in Create() methods
   - Future improvement: Message templates with localization support
   - **Mitigation:** Messages contain all relevant IDs for client-side rendering

3. **No Alert Expiration Logic**
   - Alerts accumulate indefinitely in projection
   - Future improvement: Auto-acknowledge alerts after N days
   - **Mitigation:** Unacknowledged filter reduces noise; archival strategy deferred to Phase 2+

4. **ContextData JSON Not Type-Safe**
   - Stored as `string`, parsed client-side
   - Alternative: JsonDocument or typed union (more complex)
   - **Mitigation:** Integration tests validate JSON structure

### Architectural Insights

1. **Inline Projections Enable Multi-Projection Reuse**
   - Both AdminDailyMetrics and AlertFeedView process same events
   - Integration handlers append events to store once
   - Multiple inline projections process same event in same transaction
   - **Pattern Validation:** BFF-owned projections scale horizontally

2. **Guid vs String Document IDs**
   - AdminDailyMetrics: String ID (date-keyed, one-per-day aggregation)
   - AlertFeedView: Guid ID (entity-keyed, one-per-alert tracking)
   - **Rule:** Date-keyed for temporal aggregates, Guid-keyed for entities

3. **Severity as Computed Property vs Stored Value**
   - AlertFeedView: Severity stored in document (computed during Create())
   - Alternative: Compute severity on query (more flexible, but slower)
   - **Trade-off:** Stored severity enables fast filtering, but less flexible

---

## 🔍 Code Quality Observations

### Wins

1. **Immutable Document Model:**
   - AlertFeedView is sealed record with init-only properties
   - Enums prevent invalid severity/type values
   - Nullable fields (OrderId, AcknowledgedBy) model real-world optionality

2. **Pure Projection Logic:**
   - Create() methods are pure functions (event → document)
   - No Apply() methods needed (alerts are immutable after creation)
   - Easy to test and reason about

3. **Consistent Naming:**
   - AlertType/AlertSeverity enums match AlertFeedView model
   - Handler names match event names (LowStockDetectedHandler)
   - Query endpoint name matches projection (GetAlertFeed)

4. **Comprehensive Test Coverage:**
   - 12 tests cover all 4 alert types
   - Edge cases tested (quantity = 0 vs > 0, retriable vs non-retriable)
   - Integration tests validate end-to-end flow (event → projection → query)

### Areas for Improvement (Future)

1. **Alert Deduplication:**
   - Same SKU low stock alert could fire multiple times
   - Future: Track "last alert time" per SKU, suppress duplicates within 1 hour
   - **Phase 2+ Enhancement**

2. **Alert Routing:**
   - All alerts visible to all OperationsManagers
   - Future: Route alerts by warehouse, department, or role
   - **Phase 2+ Enhancement**

3. **Alert Metrics:**
   - No tracking of alert acknowledgment latency (time to acknowledge)
   - Future: Add telemetry for ops team performance monitoring
   - **Phase 2+ Enhancement**

---

## 🚀 What's Next (Session 8)

**Scope:** SignalR Hub (Real-Time Push)

**Implementation:**
- BackofficeHub configuration
- Real-time event publishing:
  - LiveMetricUpdated (on OrderPlaced/PaymentCaptured → push to role:executive)
  - AlertCreated (on alert events → push to role:operations)
- SignalR Client transport integration tests

**Pattern:**
- Marker interface `IBackofficeWebSocketMessage`
- Discriminated union `BackofficeEvent` (LiveMetricUpdated | AlertCreated)
- Update integration handlers to publish SignalR events
- Group-based routing (`role:executive`, `role:operations`)

**Estimated Duration:** 2-3 hours

---

## 📚 References

**Created Files:**
- `src/Backoffice/Backoffice/Projections/AlertFeedView.cs`
- `src/Backoffice/Backoffice/Projections/AlertFeedViewProjection.cs`
- `src/Backoffice/Backoffice/Notifications/LowStockDetectedHandler.cs`
- `src/Backoffice/Backoffice/Notifications/ShipmentDeliveryFailedHandler.cs`
- `src/Backoffice/Backoffice/Notifications/ReturnExpiredHandler.cs`
- `src/Backoffice/Backoffice.Api/Queries/GetAlertFeed.cs`
- `tests/Backoffice/Backoffice.Api.IntegrationTests/Dashboard/AlertFeedViewTests.cs`

**Modified Files:**
- `src/Backoffice/Backoffice.Api/Program.cs` (projection registration, RabbitMQ queues)

**Commits:**
- `feat(backoffice): implement AlertFeedView projection with integration handlers` (core)
- `test(backoffice): add 12 integration tests for AlertFeedView projection` (tests)

**Planning Documents:**
- [M32.0 Milestone Plan](./m32-0-backoffice-phase-1-plan.md) (Session 7 spec)
- [Session 6 Retrospective](./m32-0-session-6-retrospective.md) (AdminDailyMetrics)

**Skill Files:**
- [Marten Event Sourcing Projections](../../skills/event-sourcing-projections.md) (MultiStreamProjection)
- [Integration Messaging](../../skills/integration-messaging.md) (RabbitMQ choreography)

**CONTEXTS.md:**
- No updates required (Backoffice ← Inventory/Fulfillment/Returns integration already documented)

---

**Session End:** 2026-03-16
**Next Session:** Session 8 (SignalR Hub for real-time push)
**Overall Progress:** 7/11 sessions complete (64%)
