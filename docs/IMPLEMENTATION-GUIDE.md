# Implementation Guide: Addressing Architectural Review Concerns

**Last Updated:** 2026-02-16  
**Source:** [docs/ARCHITECTURAL-REVIEW.md](./ARCHITECTURAL-REVIEW.md)

This document provides a prioritized roadmap for addressing the five architectural concerns identified in the comprehensive architectural review.

**Update (2026-02-16):** Priorities revised based on feedback. Event sourcing + Wolverine transactional outbox provides excellent message durability, so RabbitMQ infrastructure concern is about operational visibility (MEDIUM), not reliability (HIGH).

---

## Priority Matrix

| Priority | Concern                                        | Effort | Timeline                        | Status      |
|----------|------------------------------------------------|--------|---------------------------------|-------------|
| **HIGH** | #4: Limited Saga Compensation Logic            | Medium | Before Production               | 📋 Planned   |
| **MED**  | #1: Inconsistent RabbitMQ Infrastructure       | Medium | Before Operational Dashboards   | 📋 Planned   |
| **MED**  | #2: Shared Database Instance                   | High   | Before Scaling                  | 📋 Planned   |
| **MED**  | #3: Synchronous HTTP in Shopping Context       | Medium | Before High Traffic             | 📋 Planned   |
| **LOW**  | #5: BFF Coupled to Queue Names                 | Low    | Future Enhancement              | 📋 Planned   |

---

## Concern #1: Inconsistent RabbitMQ Infrastructure (MEDIUM PRIORITY)

**ADR:** [0008-rabbitmq-configuration-consistency.md](./decisions/0008-rabbitmq-configuration-consistency.md)

**Key Context:** Event sourcing + Wolverine transactional outbox provides excellent message durability. This concern is about operational visibility and explicit contracts, not reliability.

### Implementation Checklist

**Phase 1: Update Payments.Api (~1 hour)**
- [ ] Add RabbitMQ configuration section to `appsettings.json`
- [ ] Add `opts.UseRabbitMq(...)` in `Program.cs`
- [ ] Configure publishing for `PaymentCaptured` to `payments-events` exchange
- [ ] Configure publishing for `PaymentFailed` to `payments-events` exchange
- [ ] Configure publishing for `PaymentAuthorized` to `payments-events` exchange
- [ ] Configure publishing for `RefundCompleted` to `payments-events` exchange
- [ ] Configure publishing for `RefundFailed` to `payments-events` exchange
- [ ] Run integration tests to verify message publishing

**Phase 2: Update Inventory.Api (~1 hour)**
- [ ] Add RabbitMQ configuration section to `appsettings.json`
- [ ] Add `opts.UseRabbitMq(...)` in `Program.cs`
- [ ] Configure publishing for `ReservationConfirmed` to `inventory-events` exchange
- [ ] Configure publishing for `ReservationFailed` to `inventory-events` exchange
- [ ] Configure publishing for `ReservationCommitted` to `inventory-events` exchange
- [ ] Configure publishing for `ReservationReleased` to `inventory-events` exchange
- [ ] Run integration tests to verify message publishing

**Phase 3: Update Fulfillment.Api (~1 hour)**
- [ ] Add RabbitMQ configuration section to `appsettings.json`
- [ ] Add `opts.UseRabbitMq(...)` in `Program.cs`
- [ ] Configure publishing for `ShipmentDispatched` to `fulfillment-events` exchange
- [ ] Configure publishing for `ShipmentDelivered` to `fulfillment-events` exchange
- [ ] Configure publishing for `ShipmentDeliveryFailed` to `fulfillment-events` exchange
- [ ] Run integration tests to verify message publishing

**Phase 4: Refactor Shopping/Orders to Exchange Pattern (~1 hour)**
- [ ] Update `Shopping.Api/Program.cs` to publish to `shopping-events` exchange (not direct queue)
- [ ] Update `Orders.Api/Program.cs` to publish to `orders-events` exchange (not direct queue)
- [ ] Update `Orders.Api/Program.cs` to subscribe to exchanges:
  - [ ] `orders-payment-events` queue ← `payments-events` exchange
  - [ ] `orders-inventory-events` queue ← `inventory-events` exchange
  - [ ] `orders-fulfillment-events` queue ← `fulfillment-events` exchange

**Phase 5: Update Storefront.Api Subscriptions (~1 hour)**
- [ ] Update `Storefront.Api/Program.cs` to subscribe to exchanges:
  - [ ] `storefront-shopping-events` queue ← `shopping-events` exchange
  - [ ] `storefront-orders-events` queue ← `orders-events` exchange

**Phase 6: Integration Testing (~2 hours)**
- [ ] Verify message flows in RabbitMQ management UI (http://localhost:15672)
- [ ] Run end-to-end order placement test (Shopping → Orders → Payments → Inventory → Fulfillment)
- [ ] Verify all exchanges are created (payments-events, inventory-events, fulfillment-events, shopping-events, orders-events)
- [ ] Verify all queues are bound to correct exchanges
- [ ] Verify message rates and counts in RabbitMQ UI
- [ ] Run full test suite (ensure no regressions)

**Acceptance Criteria:**
- ✅ All 7 bounded context APIs have RabbitMQ explicitly configured
- ✅ All integration messages published to fanout exchanges (not direct queues)
- ✅ All exchanges visible in RabbitMQ management UI
- ✅ All integration tests passing
- ✅ End-to-end order flow works correctly

**Estimated Total Time:** 7 hours

---

## Concern #2: Shared Database Instance (MEDIUM PRIORITY)

### Decision Point

Before implementing, choose your strategy:

**Option A: Full Physical Separation (Recommended for Production)**
- Separate PostgreSQL instance per bounded context
- Best for production scalability and failure isolation
- Higher infrastructure cost

**Option B: Aggregate Cluster Separation (Pragmatic)**
- Group related contexts (e.g., Critical Path, Customer Experience, Operations)
- Lower infrastructure cost than Option A
- Better than current state

### Implementation Checklist (Option A: Full Separation)

**Phase 1: Update docker-compose.yml (~1 hour)**
- [ ] Add `postgres-orders` service (port 5431)
- [ ] Add `postgres-shopping` service (port 5432)
- [ ] Add `postgres-payments` service (port 5433)
- [ ] Add `postgres-inventory` service (port 5434)
- [ ] Add `postgres-fulfillment` service (port 5435)
- [ ] Add `postgres-customer-identity` service (port 5436)
- [ ] Add `postgres-product-catalog` service (port 5437)
- [ ] Add health checks for each PostgreSQL instance

**Phase 2: Update Connection Strings (~1 hour)**
- [ ] Update `Orders.Api/appsettings.json` → `Host=localhost;Port=5431;Database=orders;...`
- [ ] Update `Shopping.Api/appsettings.json` → `Host=localhost;Port=5432;Database=shopping;...`
- [ ] Update `Payments.Api/appsettings.json` → `Host=localhost;Port=5433;Database=payments;...`
- [ ] Update `Inventory.Api/appsettings.json` → `Host=localhost;Port=5434;Database=inventory;...`
- [ ] Update `Fulfillment.Api/appsettings.json` → `Host=localhost;Port=5435;Database=fulfillment;...`
- [ ] Update `CustomerIdentity.Api/appsettings.json` → `Host=localhost;Port=5436;Database=customers;...`
- [ ] Update `ProductCatalog.Api/appsettings.json` → `Host=localhost;Port=5437;Database=catalog;...`

**Phase 3: Update Marten Schema Configuration (~30 minutes)**
- [ ] Remove `opts.DatabaseSchemaName = Constants.Orders.ToLowerInvariant()` from Orders.Api
- [ ] Remove `opts.DatabaseSchemaName = Constants.Shopping.ToLowerInvariant()` from Shopping.Api
- [ ] Remove similar lines from other APIs (use default `public` schema since each has its own DB)

**Phase 4: Update Integration Tests (~2 hours)**
- [ ] Update `OrdersTestFixture` to use Orders-specific PostgreSQL container
- [ ] Update `ShoppingTestFixture` to use Shopping-specific PostgreSQL container
- [ ] Update other test fixtures similarly
- [ ] Ensure tests can run in parallel (different databases)

**Phase 5: Testing and Validation (~2 hours)**
- [ ] Start all PostgreSQL instances with `docker-compose --profile all up -d`
- [ ] Verify each API connects to its own database
- [ ] Run full integration test suite
- [ ] Verify database isolation (Orders DB down doesn't affect Shopping)
- [ ] Load test to verify no resource contention

**Acceptance Criteria:**
- ✅ Each bounded context has its own PostgreSQL database
- ✅ Connection pool per BC (no shared connections)
- ✅ Failure of one database doesn't affect other BCs
- ✅ All integration tests passing
- ✅ Can independently scale database resources per BC

**Estimated Total Time:** 6.5 hours

**Decision Required:** Update [ADR 0009: Database Separation Strategy](./decisions/0009-database-separation-strategy.md) before implementation.

---

## Concern #3: Synchronous HTTP in Shopping Context (MEDIUM PRIORITY)

### Decision Point

Choose your strategy:

**Option A: Event-Driven Data Replication (Recommended)**
- Shopping BC maintains read model of customer addresses
- Best for high availability and low latency
- More complex implementation

**Option B: Cache-Aside with Circuit Breaker (Pragmatic)**
- In-memory cache with HTTP fallback
- Circuit breaker prevents cascading failures
- Simpler implementation

### Implementation Checklist (Option B: Cache-Aside)

**Phase 1: Add Caching Infrastructure (~1 hour)**
- [ ] Add `Microsoft.Extensions.Caching.Memory` to Shopping project
- [ ] Create `ICustomerAddressCache` interface in Shopping domain
- [ ] Implement `CustomerAddressCache` with 5-minute TTL

**Phase 2: Add Circuit Breaker (~1 hour)**
- [ ] Add `Polly` package to Shopping.Api
- [ ] Configure circuit breaker policy for Customer Identity HTTP client
- [ ] Configure retry policy with exponential backoff
- [ ] Configure timeout policy (5 seconds)

**Phase 3: Graceful Degradation (~1 hour)**
- [ ] Update cart handlers to handle Customer Identity unavailability
- [ ] Allow cart operations to proceed even if address validation fails
- [ ] Log degraded mode warnings for operational visibility

**Phase 4: Testing (~1 hour)**
- [ ] Write integration test for circuit breaker (Customer Identity down scenario)
- [ ] Write integration test for cache hit scenario (no HTTP call)
- [ ] Write integration test for cache miss scenario (HTTP call + cache population)
- [ ] Load test to verify latency improvement

**Acceptance Criteria:**
- ✅ Shopping BC operates independently if Customer Identity BC is down
- ✅ Circuit breaker prevents cascading failures
- ✅ Cache reduces HTTP calls by 90%+ (5-minute TTL)
- ✅ All integration tests passing

**Estimated Total Time:** 4 hours

**Future Enhancement:** Migrate to Option A (event-driven replication) in Cycle 20+.

---

## Concern #4: Limited Saga Compensation Logic (HIGH PRIORITY)

### Implementation Checklist

**Phase 1: Enhance OrderDecider Compensation (~2 hours)**
- [ ] Update `OrderDecider.HandleReservationFailed()` to publish `RefundRequested` if payment captured
- [ ] Update `OrderDecider.HandlePaymentFailed()` to release inventory if reservation confirmed
- [ ] Add `OrderDecider.HandleFulfillmentFailed()` for shipment delivery failures
- [ ] Add unit tests for compensation decision logic

**Phase 2: Add Timeout Handling (~2 hours)**
- [ ] Create `TimeoutOrder` message with `ScheduledAt` property
- [ ] Update `Order.Start()` to schedule timeout (5 minutes)
- [ ] Create `Order.Handle(TimeoutOrder)` to cancel and compensate if still in `Placed` status
- [ ] Add integration test for timeout scenario

**Phase 3: Add Saga Monitoring (~1 hour)**
- [ ] Create health check endpoint for stuck sagas (orders in non-terminal states > 30 minutes)
- [ ] Add logging for compensation triggers (payment refund, inventory release)
- [ ] Add operational dashboard query for manual intervention cases

**Phase 4: Testing (~2 hours)**
- [ ] Write integration test for payment failure → inventory release compensation
- [ ] Write integration test for inventory failure → payment refund compensation
- [ ] Write integration test for fulfillment failure → full compensation
- [ ] Write integration test for timeout → automatic cancellation

**Acceptance Criteria:**
- ✅ Payment failure triggers inventory release (if reserved)
- ✅ Inventory failure triggers payment refund (if captured)
- ✅ Fulfillment failure triggers inventory release + payment refund
- ✅ Timeout after 5 minutes triggers automatic cancellation
- ✅ All compensation scenarios tested

**Estimated Total Time:** 7 hours

---

## Concern #5: BFF Coupled to Queue Names (LOW PRIORITY)

### Implementation Checklist

**Phase 1: Refactor to Exchange Pattern (~1 hour)**
- [ ] Update `Shopping.Api/Program.cs` to publish to `shopping-events` exchange (already done in Concern #1)
- [ ] Update `Orders.Api/Program.cs` to publish to `orders-events` exchange (already done in Concern #1)
- [ ] Update `Storefront.Api/Program.cs` to subscribe to exchanges (already done in Concern #1)

**Phase 2: Document Pattern (~30 minutes)**
- [ ] Add section to `docs/skills/bff-realtime-patterns.md` documenting exchange-based subscriptions
- [ ] Update `CONTEXTS.md` with new integration message flows

**Acceptance Criteria:**
- ✅ Publishers don't know about consumers (proper pub/sub)
- ✅ Multiple BFFs can subscribe to same events independently

**Estimated Total Time:** 1.5 hours

**Note:** This concern is mostly addressed by implementing Concern #1 (RabbitMQ consistency).

---

## Implementation Roadmap

### Before Production (Critical Path)

**Priority 1: Saga Compensation (Financial Risk)**
1. Concern #4: Saga Compensation Logic (7 hours) - **Prevents customer being charged without product delivery**

**Total:** 7 hours (1 day)

### Before Operational Dashboards / Polyglot Integration

**Priority 2: Messaging Visibility**
2. Concern #1: RabbitMQ Infrastructure (7 hours) - **For operational visibility, not durability (event sourcing handles that)**

**Total:** 7 hours (1 day)

### Before Horizontal Scaling

**Week 2-3:**
3. Concern #2: Database Separation (6.5 hours + ADR + testing)

**Total:** 10 hours (1.5 days)

### Before High Traffic

**Week 4:**
4. Concern #3: Synchronous Coupling (4 hours)

**Total:** 4 hours (0.5 days)

### Future Enhancement

**Cycle 20+:**
5. Concern #5: BFF Messaging Pattern (already addressed by #1)

---

## Testing Strategy

For each concern:
1. Write integration tests BEFORE implementation (BDD/TDD approach)
2. Use TestContainers for infrastructure dependencies (Postgres, RabbitMQ)
3. Run full test suite after each phase
4. Manual end-to-end testing with `.http` files
5. Load testing for performance validation

**Test Coverage Target:** 90%+ for compensation logic, 80%+ for infrastructure changes

---

## Monitoring and Validation

After implementation:
- [ ] Set up RabbitMQ monitoring dashboards (message rates, queue depths)
- [ ] Set up database connection pool monitoring
- [ ] Set up saga timeout alerts (orders stuck in non-terminal states)
- [ ] Set up compensation event logging and alerting
- [ ] Document operational runbooks for common failure scenarios

---

## Success Criteria

**Overall:**
- ✅ All 162 tests passing (97.5% → 100%)
- ✅ All architectural concerns addressed or explicitly accepted with ADRs
- ✅ Operational dashboards in place for monitoring
- ✅ Documentation updated (CONTEXTS.md, ADRs, skill guides)

**Performance:**
- ✅ Order placement latency < 500ms (P95)
- ✅ Message processing latency < 100ms (P95)
- ✅ No single point of failure in infrastructure

**Resilience:**
- ✅ System gracefully handles BC failures (circuit breakers, compensation)
- ✅ Messages never lost (transactional outbox + RabbitMQ)
- ✅ Sagas automatically compensate on failure

---

**Last Updated:** 2026-02-14  
**Maintained By:** Erik Shafer / Claude AI Assistant
