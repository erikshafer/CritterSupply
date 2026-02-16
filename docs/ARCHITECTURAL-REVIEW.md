# Architectural Review: CritterSupply Event-Driven System

**Review Date:** 2026-02-14  
**Reviewer Persona:** Senior Software Architect with 15+ years in Event-Driven Systems, DDD, CQRS, and Event Sourcing  
**Review Scope:** Bounded Context Design, Service Communication Patterns, Physical/Logical Separation

---

## Executive Summary

CritterSupply demonstrates a **well-architected event-driven e-commerce system** leveraging the Critter Stack (Wolverine + Marten). The codebase exhibits strong fundamentals: clear bounded context boundaries, event sourcing for core aggregates, transactional messaging patterns, and a pragmatic hybrid of orchestration and choreography.

However, as the system approaches production readiness, **five architectural concerns** warrant attention. These are not fundamental flaws requiring rewrites, but rather areas where current design decisions could lead to operational challenges, scalability bottlenecks, or increased coupling as the system evolves.

### Overall Grade: **B+ (85/100)**

**Strengths:**
- ✅ Clean bounded context separation with well-defined responsibilities
- ✅ Event sourcing implemented correctly with Marten snapshots
- ✅ Transactional outbox pattern used consistently (excellent message durability)
- ✅ Order saga properly orchestrates complex workflows
- ✅ Separate schemas per bounded context (logical database separation)

**Areas for Improvement:**
- ⚠️ Inconsistent messaging infrastructure (operational visibility gap, not a durability issue)
- ⚠️ Shared database instance (single point of failure, operational bottlenecks)
- ⚠️ Synchronous HTTP coupling between Shopping and Customer Identity
- ⚠️ Limited error handling and compensation in saga workflows
- ⚠️ BFF real-time messaging pattern tightly coupled to specific queue names

---

## Concern #1: Inconsistent Messaging Infrastructure (MEDIUM PRIORITY)

### The Issue

Only **3 of 7** bounded context APIs have RabbitMQ enabled:

| BC                   | RabbitMQ Enabled? | Publishes Messages? | Can Receive Messages? |
|----------------------|-------------------|---------------------|-----------------------|
| **Orders**           | ✅ Yes             | `OrderPlaced`       | ✅ Yes (via Wolverine) |
| **Shopping**         | ✅ Yes             | `ItemAdded`, `ItemRemoved`, `ItemQuantityChanged` | ✅ Yes (via Wolverine) |
| **Storefront (BFF)** | ✅ Yes             | None                | ✅ Yes (`storefront-notifications` queue) |
| **Payments**         | ❌ **No**          | None configured     | ⚠️ **Limited** (Wolverine local handlers only) |
| **Inventory**        | ❌ **No**          | None configured     | ⚠️ **Limited** (Wolverine local handlers only) |
| **Fulfillment**      | ❌ **No**          | None configured     | ⚠️ **Limited** (Wolverine local handlers only) |
| **CustomerIdentity** | ❌ **No**          | N/A                 | N/A (HTTP-only service) |

**Current State:** Payments, Inventory, and Fulfillment contexts rely on **Wolverine's internal local queues** and transactional outbox, but **do not explicitly publish integration messages to RabbitMQ**. 

**Important Note:** These bounded contexts use **event sourcing with Marten**, which provides excellent resilience. Events are durably persisted to PostgreSQL, and Wolverine's `UseDurableOutboxOnAllSendingEndpoints()` ensures messages survive failures. The concern here is **not about message durability** (which is solid), but about **operational visibility and explicit contracts**.

### Why This Matters

1. **Observability Gap:** RabbitMQ management UI doesn't show message flows for Payments → Orders, Inventory → Orders, Fulfillment → Orders (Wolverine handles it internally, but operations teams lose visibility)
2. **Polyglot Integration:** Future non-.NET services cannot consume messages from Payments/Inventory/Fulfillment (no RabbitMQ exchanges exposed)
3. **Operational Discoverability:** Other teams/services cannot easily see what messages these contexts publish without inspecting .NET code
4. **Horizontal Scaling Clarity:** While Wolverine handles scaling correctly, explicit RabbitMQ configuration makes message distribution patterns more obvious
5. **Standardization:** Inconsistent messaging approach across BCs (some use RabbitMQ, others don't) can confuse operations teams

### The Evidence

**Payments.Api/Program.cs:**
```csharp
builder.Host.UseWolverine(opts =>
{
    // ... handler discovery ...
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
    
    // ❌ NO RabbitMQ configuration
    // ❌ NO PublishMessage<...> routing
});
```

**Orders.Api/Program.cs (for comparison):**
```csharp
builder.Host.UseWolverine(opts =>
{
    // ... handler discovery ...
    
    // ✅ RabbitMQ explicitly configured
    opts.UseRabbitMq(rabbit => { /* ... */ }).AutoProvision();
    
    // ✅ Message publishing explicitly routed
    opts.PublishMessage<Messages.Contracts.Orders.OrderPlaced>()
        .ToRabbitQueue("storefront-notifications");
});
```

### Recommendation

**Action:** Enable RabbitMQ for Payments, Inventory, and Fulfillment APIs with explicit message routing.

**Priority:** MEDIUM (before polyglot integration or operational dashboards)

**Key Insight:** The event-sourced foundation with Marten + Wolverine transactional outbox provides excellent message durability. This recommendation is about making implicit messaging contracts explicit for operational visibility and future extensibility, not about fixing a reliability issue.

**Implementation:**

1. **Payments.Api/Program.cs:**
   ```csharp
   opts.UseRabbitMq(rabbit =>
   {
       rabbit.HostName = rabbitConfig["hostname"] ?? "localhost";
       rabbit.VirtualHost = rabbitConfig["virtualhost"] ?? "/";
       rabbit.Port = rabbitConfig.GetValue<int?>("port") ?? 5672;
       rabbit.UserName = rabbitConfig["username"] ?? "guest";
       rabbit.Password = rabbitConfig["password"] ?? "guest";
   }).AutoProvision();
   
   // Publish payment lifecycle messages
   opts.PublishMessage<Messages.Contracts.Payments.PaymentCaptured>()
       .ToRabbitExchange("payments-events", ExchangeType.Fanout);
   opts.PublishMessage<Messages.Contracts.Payments.PaymentFailed>()
       .ToRabbitExchange("payments-events", ExchangeType.Fanout);
   opts.PublishMessage<Messages.Contracts.Payments.PaymentAuthorized>()
       .ToRabbitExchange("payments-events", ExchangeType.Fanout);
   opts.PublishMessage<Messages.Contracts.Payments.RefundCompleted>()
       .ToRabbitExchange("payments-events", ExchangeType.Fanout);
   opts.PublishMessage<Messages.Contracts.Payments.RefundFailed>()
       .ToRabbitExchange("payments-events", ExchangeType.Fanout);
   ```

2. **Inventory.Api/Program.cs:**
   ```csharp
   opts.UseRabbitMq(rabbit => { /* ... */ }).AutoProvision();
   
   opts.PublishMessage<Messages.Contracts.Inventory.ReservationConfirmed>()
       .ToRabbitExchange("inventory-events", ExchangeType.Fanout);
   opts.PublishMessage<Messages.Contracts.Inventory.ReservationFailed>()
       .ToRabbitExchange("inventory-events", ExchangeType.Fanout);
   opts.PublishMessage<Messages.Contracts.Inventory.ReservationCommitted>()
       .ToRabbitExchange("inventory-events", ExchangeType.Fanout);
   opts.PublishMessage<Messages.Contracts.Inventory.ReservationReleased>()
       .ToRabbitExchange("inventory-events", ExchangeType.Fanout);
   ```

3. **Fulfillment.Api/Program.cs:**
   ```csharp
   opts.UseRabbitMq(rabbit => { /* ... */ }).AutoProvision();
   
   opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDispatched>()
       .ToRabbitExchange("fulfillment-events", ExchangeType.Fanout);
   opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
       .ToRabbitExchange("fulfillment-events", ExchangeType.Fanout);
   opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDeliveryFailed>()
       .ToRabbitExchange("fulfillment-events", ExchangeType.Fanout);
   ```

4. **Update Orders.Api to subscribe to exchanges instead of queues:**
   ```csharp
   // Orders subscribes to payment events
   opts.ListenToRabbitQueue("orders-payment-events")
       .FromExchange("payments-events", ExchangeType.Fanout);
   
   // Orders subscribes to inventory events
   opts.ListenToRabbitQueue("orders-inventory-events")
       .FromExchange("inventory-events", ExchangeType.Fanout);
   
   // Orders subscribes to fulfillment events
   opts.ListenToRabbitQueue("orders-fulfillment-events")
       .FromExchange("fulfillment-events", ExchangeType.Fanout);
   ```

**Benefits:**
- ✅ Explicit message flows visible in RabbitMQ management UI
- ✅ Other services can subscribe to payment/inventory/fulfillment events
- ✅ Better operational visibility (dead-letter queues, retry policies)
- ✅ Supports horizontal scaling with competing consumers pattern
- ✅ Future-proof for polyglot services (Node.js monitoring dashboard, Python analytics)

**Trade-offs:**
- Slightly more configuration in each API's Program.cs (15-20 lines)
- RabbitMQ becomes a hard dependency (but it already is for Orders/Shopping/Storefront)

---

## Concern #2: Shared Database Instance (MEDIUM PRIORITY)

### The Issue

All bounded contexts use **a single shared PostgreSQL instance** on `localhost:5433`. While each context has its own **schema** (logical separation), they share the same **database** and **connection pool** (physical coupling).

**Current Architecture:**
```
postgres:5433/postgres
├── Schema: orders        (Orders BC)
├── Schema: shopping      (Shopping BC)
├── Schema: payments      (Payments BC)
├── Schema: inventory     (Inventory BC)
├── Schema: fulfillment   (Fulfillment BC)
├── Schema: customers     (Customer Identity BC - EF Core)
└── Schema: productcatalog (Product Catalog BC)
```

**Connection String (All APIs):**
```json
{
  "ConnectionStrings": {
    "marten": "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres"
  }
}
```

### Why This Matters

1. **Single Point of Failure:** If PostgreSQL crashes, **all bounded contexts** are down simultaneously
2. **Resource Contention:** A heavy query in Product Catalog BC can starve Orders BC of connections
3. **Deployment Coupling:** Schema migrations for Inventory BC require coordination with Orders BC (shared database downtime)
4. **Blast Radius:** A bug in one BC (e.g., table lock in Inventory) can block transactions in other BCs
5. **Operational Complexity:** Database backups/restores affect all BCs (can't restore just Payments data without affecting Orders)
6. **Scalability Bottleneck:** Can't independently scale database resources per BC (e.g., Orders needs 10x read replicas, Fulfillment needs 2x write capacity)

### The Evidence

**Orders.Api/appsettings.json:**
```json
{
  "ConnectionStrings": {
    "marten": "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres"
  }
}
```

**Shopping.Api/appsettings.json:**
```json
{
  "ConnectionStrings": {
    "marten": "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres"
  }
}
```

*(Same connection string repeated across all 7 APIs)*

### Recommendation

**Action:** Migrate to separate PostgreSQL databases per bounded context (or at least per aggregate cluster).

**Priority:** MEDIUM (before horizontal scaling)

**Implementation Strategy:**

**Option A: Full Physical Separation (Ideal for Production)**
```yaml
# docker-compose.yml
services:
  postgres-orders:
    image: postgres:latest
    ports: ["5431:5432"]
    environment:
      POSTGRES_DB: orders
  
  postgres-shopping:
    image: postgres:latest
    ports: ["5432:5432"]
    environment:
      POSTGRES_DB: shopping
  
  postgres-payments:
    image: postgres:latest
    ports: ["5433:5432"]
    environment:
      POSTGRES_DB: payments
  
  postgres-inventory:
    image: postgres:latest
    ports: ["5434:5432"]
    environment:
      POSTGRES_DB: inventory
  
  # ... etc
```

**Connection Strings (per API):**
- **Orders.Api:** `Host=localhost;Port=5431;Database=orders;...`
- **Shopping.Api:** `Host=localhost;Port=5432;Database=shopping;...`
- **Payments.Api:** `Host=localhost;Port=5433;Database=payments;...`

**Option B: Aggregate Cluster Separation (Pragmatic for Near-Term)**

Group related contexts sharing similar operational characteristics:

```yaml
# Critical Path Cluster (Orders + Payments + Inventory)
postgres-critical:
  ports: ["5431:5432"]

# Customer Experience Cluster (Shopping + Customer Identity + Catalog)
postgres-customer:
  ports: ["5432:5432"]

# Operational Cluster (Fulfillment + future Returns/Notifications)
postgres-operations:
  ports: ["5433:5432"]
```

**Benefits:**
- ✅ Independent failure domains (Orders BC can run if Fulfillment DB is down)
- ✅ Independent scalability (scale Orders DB without affecting Catalog)
- ✅ Independent deployments (schema migrations don't block other BCs)
- ✅ Better security isolation (Fulfillment can't query Orders data)
- ✅ Clearer operational ownership (DBA teams can own specific databases)

**Trade-offs:**
- More infrastructure to manage (7 PostgreSQL instances instead of 1)
- Higher resource usage in local development (recommendation: use docker-compose profiles)
- Slightly more complex docker-compose.yml

**Defensive Position:**

If you choose to keep the shared database:
1. **Document the decision** in an ADR (e.g., `ADR 0008: Shared Database for Development Simplicity`)
2. **Add monitoring** for connection pool exhaustion per schema
3. **Implement circuit breakers** to prevent cascading failures
4. **Create database-level resource quotas** per schema (if PostgreSQL version supports it)

---

## Concern #3: Synchronous HTTP Coupling in Shopping Context (MEDIUM PRIORITY)

### The Issue

**Shopping BC** makes **synchronous HTTP calls** to **Customer Identity BC** during cart operations:

**Evidence:**
```csharp
// Shopping.Api/Program.cs
builder.Services.AddHttpClient("CustomerIdentity", client =>
{
    var customerIdentityBaseUrl = builder.Configuration.GetValue<string>("CustomerIdentity:BaseUrl")
                                  ?? "http://localhost:5002";
    client.BaseAddress = new Uri(customerIdentityBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

This creates **tight runtime coupling**: Shopping BC cannot function if Customer Identity BC is down or slow.

### Why This Matters

1. **Availability Coupling:** If Customer Identity BC is down, Shopping BC cannot validate customer addresses (cart operations fail)
2. **Latency Propagation:** Slow address lookups in Customer Identity (e.g., database query slow) block cart operations
3. **Cascading Failures:** If Customer Identity BC is overwhelmed with requests, Shopping BC requests time out (10-second timeout)
4. **Violates Bounded Context Autonomy:** Shopping BC should be able to operate independently during Customer Identity BC maintenance windows

### The Evidence

**Shopping BC currently uses HTTP for:**
- Validating customer addresses during checkout (synchronous call to Customer Identity API)

**CONTEXTS.md explicitly documents this:**
> Shopping -.->|Product Details| Catalog  
> Orders -.->|Customer Snapshot| CustomerID

However, Orders BC does the right thing—it queries Customer Identity **during checkout completion** (acceptable synchronous call), while Shopping BC does it **during cart operations** (higher frequency, higher risk).

### Recommendation

**Action:** Replace synchronous HTTP calls with asynchronous integration messages or cached data replication.

**Priority:** MEDIUM (before high-traffic scenarios)

**Implementation Strategy:**

**Option A: Event-Driven Data Replication (Preferred)**

1. **Customer Identity BC publishes domain events:**
   ```csharp
   // Messages.Contracts/CustomerIdentity/CustomerAddressAdded.cs
   public sealed record CustomerAddressAdded(
       Guid CustomerId,
       Guid AddressId,
       string AddressLine1,
       string City,
       string PostalCode,
       string Country);
   ```

2. **Shopping BC subscribes and maintains a read model:**
   ```csharp
   // Shopping/CustomerAddresses/CustomerAddressProjection.cs
   public class CustomerAddressProjection
   {
       public void Handle(CustomerAddressAdded message)
       {
           // Store address in Shopping BC's read model
           // Shopping can now validate addresses without calling Customer Identity
       }
   }
   ```

**Option B: Cache-Aside Pattern with Short TTL**

```csharp
// Shopping.Api/Program.cs
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICustomerAddressCache, CustomerAddressCache>();

public class CustomerAddressCache
{
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    
    public async Task<CustomerAddress?> GetAddressAsync(Guid customerId, Guid addressId)
    {
        var cacheKey = $"address:{customerId}:{addressId}";
        
        if (_cache.TryGetValue(cacheKey, out CustomerAddress? cached))
            return cached;
        
        // Fall back to HTTP (with circuit breaker)
        var address = await FetchFromCustomerIdentityAsync(customerId, addressId);
        
        _cache.Set(cacheKey, address, TimeSpan.FromMinutes(5)); // Short TTL
        return address;
    }
}
```

**Option C: Accept the Coupling (Defensive Position)**

If you decide synchronous HTTP is acceptable:

1. **Add circuit breaker** (using Polly):
   ```csharp
   builder.Services.AddHttpClient("CustomerIdentity")
       .AddPolicyHandler(Policy
           .Handle<HttpRequestException>()
           .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));
   ```

2. **Add retry with exponential backoff:**
   ```csharp
   .AddPolicyHandler(Policy
       .Handle<HttpRequestException>()
       .WaitAndRetryAsync(3, retryAttempt => 
           TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));
   ```

3. **Provide graceful degradation:**
   ```csharp
   public async Task<CartValidationResult> ValidateCart(Guid cartId)
   {
       try
       {
           var address = await _customerIdentityClient.GetAddressAsync(...);
           return new CartValidationResult { IsValid = true, Address = address };
       }
       catch (HttpRequestException)
       {
           // Customer Identity is down - allow cart operations to proceed
           return new CartValidationResult { IsValid = true, Address = null };
       }
   }
   ```

**Benefits (Option A: Event-Driven Replication):**
- ✅ Shopping BC operates independently of Customer Identity BC availability
- ✅ Faster address lookups (local read model, no network calls)
- ✅ Aligns with event-driven architecture principles
- ✅ Eventually consistent (acceptable for cart operations)

**Trade-offs:**
- Slightly more complexity (maintain address read model in Shopping BC)
- Eventual consistency (address updates take 1-2 seconds to propagate)

---

## Concern #4: Limited Saga Compensation Logic (LOW-MEDIUM PRIORITY)

### The Issue

The **Order saga** orchestrates complex workflows across Payments, Inventory, and Fulfillment, but has **limited compensation (rollback) logic** for unhappy path scenarios.

**Current Saga Compensation:**
- ✅ Payment failure triggers `ReservationReleaseRequested` (compensates inventory)
- ❌ Inventory failure does NOT trigger payment refund (if payment was captured first)
- ❌ Fulfillment failure does NOT trigger inventory release + payment refund
- ❌ No timeout handling (saga could wait indefinitely for PaymentCaptured)

### Why This Matters

1. **Financial Risk:** If payment is captured but inventory fails, customer is charged but order can't be fulfilled (manual intervention required)
2. **Inventory Leaks:** If fulfillment fails after inventory is committed, stock remains locked indefinitely
3. **Stuck Sagas:** If Payments BC never responds (network partition), saga waits forever in `Placed` status
4. **Operational Burden:** Support team must manually identify and compensate failed orders

### The Evidence

**Order.cs saga handlers:**
```csharp
// ✅ Payment failure compensates inventory
public OutgoingMessages Handle(PaymentFailed message)
{
    var decision = OrderDecider.HandlePaymentFailed(this, message, DateTimeOffset.UtcNow);
    // ... applies status change ...
    
    // Returns ReservationReleaseRequested if inventory was reserved
    var outgoing = new OutgoingMessages();
    foreach (var msg in decision.Messages) outgoing.Add(msg);
    return outgoing;
}

// ❌ Inventory failure does NOT compensate payment
public void Handle(ReservationFailed message)
{
    var decision = OrderDecider.HandleReservationFailed(this, message);
    // ... only sets Status = InventoryFailed ...
    // No RefundRequested message published
}
```

### Recommendation

**Action:** Enhance saga with comprehensive compensation logic and timeout handling.

**Priority:** LOW-MEDIUM (before production, but not blocking initial launch)

**Implementation Strategy:**

1. **Add Compensation Decision Methods to OrderDecider:**

```csharp
// OrderDecider.cs
public static OrderDecision HandleReservationFailed(Order order, ReservationFailed message)
{
    var messages = new List<object>();
    
    // If payment was already captured, initiate refund (compensation)
    if (order.IsPaymentCaptured)
    {
        messages.Add(new Messages.Contracts.Payments.RefundRequested(
            order.Id,
            order.TotalAmount,
            "Order cancelled due to insufficient inventory"));
    }
    
    return new OrderDecision(
        Status: OrderStatus.Cancelled,
        Messages: messages);
}
```

2. **Add Timeout Handling with Wolverine's Scheduled Messages:**

```csharp
// Order.cs
public static (Order, IntegrationMessages.OrderPlaced, TimeoutOrder) Start(
    Messages.Contracts.Shopping.CheckoutCompleted message)
{
    var (order, orderPlaced) = OrderDecider.Start(command, DateTimeOffset.UtcNow);
    
    // Schedule timeout message (5 minutes for payment + inventory)
    var timeout = new TimeoutOrder(order.Id)
        .ScheduledAt(DateTimeOffset.UtcNow.AddMinutes(5));
    
    return (order, orderPlaced, timeout);
}

public void Handle(TimeoutOrder timeout)
{
    // If still in Placed status after 5 minutes, cancel and compensate
    if (Status == OrderStatus.Placed)
    {
        Status = OrderStatus.Cancelled;
        // Trigger compensation (release inventory, refund payment if captured)
    }
}
```

3. **Add Fulfillment Failure Compensation:**

```csharp
public OutgoingMessages Handle(FulfillmentMessages.ShipmentDeliveryFailed message)
{
    var decision = OrderDecider.HandleShipmentDeliveryFailed(this, message);
    
    // If delivery failed multiple times, cancel order and trigger full compensation
    if (decision.ShouldCancel)
    {
        var outgoing = new OutgoingMessages();
        outgoing.Add(new ReservationReleaseRequested(Id, ReservationIds.Keys.ToList()));
        outgoing.Add(new RefundRequested(Id, TotalAmount, "Order cancelled due to delivery failure"));
        return outgoing;
    }
    
    return new OutgoingMessages();
}
```

**Benefits:**
- ✅ Automatic compensation for failed orders (no manual intervention)
- ✅ Prevents financial discrepancies (customer charged but no product delivered)
- ✅ Prevents inventory leaks (stock released if order can't be fulfilled)
- ✅ Better customer experience (automatic refunds for failed orders)

**Trade-offs:**
- More complex saga logic (more state transitions and decision paths)
- Need to carefully test compensation scenarios (integration tests critical)

**Defensive Position:**

If you defer comprehensive compensation:
1. **Add monitoring alerts** for orders stuck in non-terminal states (Placed, PendingPayment) for > 30 minutes
2. **Create operational runbook** for manual compensation procedures
3. **Log compensation events** prominently for forensic analysis

---

## Concern #5: BFF Real-Time Messaging Coupled to Queue Names (LOW PRIORITY)

### The Issue

The **Customer Experience BFF (Storefront)** hardcodes the RabbitMQ queue name `"storefront-notifications"` in multiple places, creating tight coupling between the BFF and upstream bounded contexts.

**Evidence:**

**Orders.Api publishes to "storefront-notifications":**
```csharp
// Orders.Api/Program.cs
opts.PublishMessage<Messages.Contracts.Orders.OrderPlaced>()
    .ToRabbitQueue("storefront-notifications");
```

**Shopping.Api publishes to "storefront-notifications":**
```csharp
// Shopping.Api/Program.cs
opts.PublishMessage<Messages.Contracts.Shopping.ItemAdded>()
    .ToRabbitQueue("storefront-notifications");
opts.PublishMessage<Messages.Contracts.Shopping.ItemRemoved>()
    .ToRabbitQueue("storefront-notifications");
opts.PublishMessage<Messages.Contracts.Shopping.ItemQuantityChanged>()
    .ToRabbitQueue("storefront-notifications");
```

**Storefront.Api listens to "storefront-notifications":**
```csharp
// Storefront.Api/Program.cs
opts.ListenToRabbitQueue("storefront-notifications");
```

### Why This Matters

1. **Publishing Context Knows About Consumer:** Orders and Shopping contexts should NOT know that "storefront" is consuming their events (violation of publisher/subscriber pattern)
2. **Scalability Bottleneck:** All storefront notifications flow through a single queue (can't independently scale different event types)
3. **Future BFF Additions:** If you add a "Vendor Portal BFF" or "Mobile BFF", Orders/Shopping must be updated to publish to new queues
4. **Message Type Filtering:** Storefront receives ALL events on one queue, must filter client-side (inefficient)

### Recommendation

**Action:** Migrate to exchange-based pub/sub pattern with fanout exchanges.

**Priority:** LOW (cosmetic improvement, but good practice for future scalability)

**Implementation Strategy:**

**Step 1: Orders/Shopping publish to exchanges (not direct queues):**

```csharp
// Orders.Api/Program.cs
opts.PublishMessage<Messages.Contracts.Orders.OrderPlaced>()
    .ToRabbitExchange("orders-events", ExchangeType.Fanout);

// Shopping.Api/Program.cs
opts.PublishMessage<Messages.Contracts.Shopping.ItemAdded>()
    .ToRabbitExchange("shopping-events", ExchangeType.Fanout);
opts.PublishMessage<Messages.Contracts.Shopping.ItemRemoved>()
    .ToRabbitExchange("shopping-events", ExchangeType.Fanout);
opts.PublishMessage<Messages.Contracts.Shopping.ItemQuantityChanged>()
    .ToRabbitExchange("shopping-events", ExchangeType.Fanout);
```

**Step 2: Storefront subscribes to exchanges:**

```csharp
// Storefront.Api/Program.cs
opts.ListenToRabbitQueue("storefront-shopping-events")
    .FromExchange("shopping-events", ExchangeType.Fanout);

opts.ListenToRabbitQueue("storefront-orders-events")
    .FromExchange("orders-events", ExchangeType.Fanout);
```

**Step 3: Future Vendor Portal BFF subscribes independently:**

```csharp
// VendorPortal.Api/Program.cs (future)
opts.ListenToRabbitQueue("vendor-orders-events")
    .FromExchange("orders-events", ExchangeType.Fanout); // Same exchange, different queue
```

**Benefits:**
- ✅ Publishers don't know about consumers (proper pub/sub pattern)
- ✅ Multiple BFFs can subscribe to same events independently
- ✅ Easier to add message filtering with topic exchanges later
- ✅ Better scalability (each BFF has its own queue, independent message rates)

**Trade-offs:**
- Slightly more RabbitMQ configuration (exchanges + queues instead of just queues)
- Need to update all 3 APIs (Orders, Shopping, Storefront)

---

## Additional Observations (Strengths to Preserve)

### ✅ Excellent Documentation Structure

- **CONTEXTS.md** is comprehensive and serves as the architectural source of truth
- **ADRs** document key decisions (checkout migration, EF Core for Customer Identity, SSE over SignalR)
- **Cycle-based planning** provides clear project history and rationale

**Recommendation:** Continue maintaining CONTEXTS.md religiously as the system evolves.

---

### ✅ Proper Event Sourcing Implementation

- Aggregates use `Create()` and `Apply()` methods correctly
- Marten snapshots configured for performance (`SnapshotLifecycle.Inline`)
- Events are immutable records with clear naming (past tense)
- No business logic in event handlers (projection logic only)

**Recommendation:** Keep this pattern consistent as new aggregates are added.

---

### ✅ Transactional Outbox Pattern

All APIs consistently use:
```csharp
opts.Policies.AutoApplyTransactions();
opts.Policies.UseDurableLocalQueues();
opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
```

This ensures **exactly-once delivery semantics** (no duplicate messages, no lost messages).

**Recommendation:** Ensure all future BCs adopt this pattern.

---

### ✅ Clear Saga vs. Aggregate Distinction

- **Order saga** orchestrates long-running workflows (Payments + Inventory + Fulfillment)
- **Checkout aggregate** encapsulates transactional consistency within Orders BC
- **Cart aggregate** manages shopping session lifecycle within Shopping BC

**Recommendation:** Document saga patterns in a skill guide (similar to `skills/marten-event-sourcing.md`).

---

## Recommendations Summary

| Priority | Concern                                        | Effort | Risk if Not Addressed       |
|----------|------------------------------------------------|--------|-----------------------------|
| **HIGH** | #4: Limited Saga Compensation Logic            | Medium | Financial risk, stuck sagas, manual cleanup |
| **MED**  | #1: Inconsistent RabbitMQ Infrastructure       | Medium | Operational visibility gap, polyglot integration blocked |
| **MED**  | #2: Shared Database Instance                   | High   | Single point of failure, resource contention |
| **MED**  | #3: Synchronous HTTP in Shopping Context       | Medium | Availability coupling, cascading failures |
| **LOW**  | #5: BFF Coupled to Queue Names                 | Low    | Publisher/subscriber violation, future BFF complexity |

---

## Conclusion

CritterSupply demonstrates a **solid foundation** for event-driven architecture with the Critter Stack. The bounded context boundaries are well-defined, event sourcing is correctly implemented, and the saga pattern is appropriately used for orchestration.

The five concerns identified are **not blockers** but rather **evolutionary improvements** that should be addressed as the system scales:

1. **Before Production:** Address concern #4 (saga compensation logic) - highest financial risk
2. **Before Horizontal Scaling:** Address concerns #1 (RabbitMQ standardization for operational visibility), #2 (database separation), and #3 (synchronous coupling mitigation)
3. **Future Enhancement:** Address concern #5 (BFF messaging pattern - already partially addressed by #1)

**Key Insight on Event Sourcing:** The event-sourced architecture with Marten + Wolverine transactional outbox provides excellent message durability. Concerns about messaging infrastructure are about operational visibility and explicit contracts, not reliability.

**Overall Assessment:** This codebase is ready for production with minor enhancements. The architectural patterns are sound, and the concerns identified are typical of early-stage microservices systems that can be addressed incrementally.

---

**Reviewer Signature:**  
*Senior Software Architect (Persona)*  
*Specialization: Event-Driven Systems, DDD, CQRS, Event Sourcing*  
*Date: 2026-02-14*
