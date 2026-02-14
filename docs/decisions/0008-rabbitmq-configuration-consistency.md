# ADR 0008: RabbitMQ Configuration Consistency Across Bounded Contexts

**Status:** ⚠️ Proposed

**Date:** 2026-02-14

**Context:**

Currently, only 3 of 7 bounded context APIs have RabbitMQ explicitly configured (Orders, Shopping, Storefront). The remaining contexts (Payments, Inventory, Fulfillment) rely on Wolverine's internal local queues and transactional outbox patterns without explicit RabbitMQ publishing configuration.

**Current State:**

| BC                   | RabbitMQ Enabled? | Publishes Messages?                                   |
|----------------------|-------------------|-------------------------------------------------------|
| **Orders**           | ✅ Yes             | `OrderPlaced` → `storefront-notifications`            |
| **Shopping**         | ✅ Yes             | `ItemAdded`, `ItemRemoved`, `ItemQuantityChanged`     |
| **Storefront (BFF)** | ✅ Yes             | (Consumer only)                                       |
| **Payments**         | ❌ No              | Relies on Wolverine local queues                      |
| **Inventory**        | ❌ No              | Relies on Wolverine local queues                      |
| **Fulfillment**      | ❌ No              | Relies on Wolverine local queues                      |

**Problem:**

While Wolverine's transactional outbox ensures message delivery within the .NET ecosystem, the lack of explicit RabbitMQ configuration creates several issues:

1. **Discoverability:** Other teams/services cannot see message flows in RabbitMQ management UI
2. **Observability:** No visibility into message rates, queue depths, or dead-letter queues for Payments/Inventory/Fulfillment
3. **Polyglot Integration:** Future non-.NET services cannot consume messages from these contexts
4. **Horizontal Scaling:** Unclear message routing behavior when scaling to multiple instances
5. **Operational Inconsistency:** Some BCs use RabbitMQ, others don't (confusing for operations teams)

**Decision:**

**ALL bounded context APIs will explicitly configure RabbitMQ** with fanout exchanges for domain events.

**Pattern:**
- Each BC publishes integration messages to its own exchange (e.g., `payments-events`, `inventory-events`, `fulfillment-events`)
- Consumers subscribe to exchanges via their own queues (e.g., `orders-payment-events`, `orders-inventory-events`)
- Use `ExchangeType.Fanout` for pub/sub pattern (multiple consumers can subscribe to same events)

**Implementation:**

**1. Payments.Api/Program.cs:**
```csharp
builder.Host.UseWolverine(opts =>
{
    // ... existing policies ...
    
    opts.UseRabbitMq(rabbit =>
    {
        rabbit.HostName = rabbitConfig["hostname"] ?? "localhost";
        rabbit.VirtualHost = rabbitConfig["virtualhost"] ?? "/";
        rabbit.Port = rabbitConfig.GetValue<int?>("port") ?? 5672;
        rabbit.UserName = rabbitConfig["username"] ?? "guest";
        rabbit.Password = rabbitConfig["password"] ?? "guest";
    }).AutoProvision();
    
    // Publish payment events to exchange
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
});
```

**2. Inventory.Api/Program.cs:**
```csharp
builder.Host.UseWolverine(opts =>
{
    // ... existing policies ...
    
    opts.UseRabbitMq(rabbit => { /* ... same config ... */ }).AutoProvision();
    
    // Publish inventory events to exchange
    opts.PublishMessage<Messages.Contracts.Inventory.ReservationConfirmed>()
        .ToRabbitExchange("inventory-events", ExchangeType.Fanout);
    opts.PublishMessage<Messages.Contracts.Inventory.ReservationFailed>()
        .ToRabbitExchange("inventory-events", ExchangeType.Fanout);
    opts.PublishMessage<Messages.Contracts.Inventory.ReservationCommitted>()
        .ToRabbitExchange("inventory-events", ExchangeType.Fanout);
    opts.PublishMessage<Messages.Contracts.Inventory.ReservationReleased>()
        .ToRabbitExchange("inventory-events", ExchangeType.Fanout);
});
```

**3. Fulfillment.Api/Program.cs:**
```csharp
builder.Host.UseWolverine(opts =>
{
    // ... existing policies ...
    
    opts.UseRabbitMq(rabbit => { /* ... same config ... */ }).AutoProvision();
    
    // Publish fulfillment events to exchange
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDispatched>()
        .ToRabbitExchange("fulfillment-events", ExchangeType.Fanout);
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDelivered>()
        .ToRabbitExchange("fulfillment-events", ExchangeType.Fanout);
    opts.PublishMessage<Messages.Contracts.Fulfillment.ShipmentDeliveryFailed>()
        .ToRabbitExchange("fulfillment-events", ExchangeType.Fanout);
});
```

**4. Orders.Api/Program.cs (update to subscribe to exchanges):**
```csharp
builder.Host.UseWolverine(opts =>
{
    // ... existing config ...
    
    // Subscribe to payment events
    opts.ListenToRabbitQueue("orders-payment-events")
        .FromExchange("payments-events", ExchangeType.Fanout);
    
    // Subscribe to inventory events
    opts.ListenToRabbitQueue("orders-inventory-events")
        .FromExchange("inventory-events", ExchangeType.Fanout);
    
    // Subscribe to fulfillment events
    opts.ListenToRabbitQueue("orders-fulfillment-events")
        .FromExchange("fulfillment-events", ExchangeType.Fanout);
});
```

**5. Update Shopping/Orders to use exchange pattern:**
```csharp
// Shopping.Api/Program.cs
opts.PublishMessage<Messages.Contracts.Shopping.ItemAdded>()
    .ToRabbitExchange("shopping-events", ExchangeType.Fanout);
opts.PublishMessage<Messages.Contracts.Shopping.ItemRemoved>()
    .ToRabbitExchange("shopping-events", ExchangeType.Fanout);
opts.PublishMessage<Messages.Contracts.Shopping.ItemQuantityChanged>()
    .ToRabbitExchange("shopping-events", ExchangeType.Fanout);

// Orders.Api/Program.cs
opts.PublishMessage<Messages.Contracts.Orders.OrderPlaced>()
    .ToRabbitExchange("orders-events", ExchangeType.Fanout);
```

**6. Update Storefront.Api to subscribe to exchanges:**
```csharp
// Storefront.Api/Program.cs
opts.ListenToRabbitQueue("storefront-shopping-events")
    .FromExchange("shopping-events", ExchangeType.Fanout);
opts.ListenToRabbitQueue("storefront-orders-events")
    .FromExchange("orders-events", ExchangeType.Fanout);
```

**Rationale:**

**Benefits:**
- ✅ **Observability:** All message flows visible in RabbitMQ management UI (exchanges, queues, message rates)
- ✅ **Polyglot Integration:** Non-.NET services can subscribe to domain events (Node.js, Python, Go)
- ✅ **Horizontal Scaling:** Competing consumers pattern works correctly with explicit queue configuration
- ✅ **Operational Consistency:** All BCs use same messaging pattern (easier for operations/SRE teams)
- ✅ **Dead-Letter Queues:** Can configure DLQs per queue for failed message investigation
- ✅ **Message Replay:** Can replay messages from exchanges for debugging/testing

**Trade-offs:**
- Slightly more configuration in each API's Program.cs (~15-20 lines)
- RabbitMQ becomes a hard dependency for all BCs (already true for Orders/Shopping/Storefront)
- Need to update appsettings.json with RabbitMQ configuration for Payments/Inventory/Fulfillment

**Consequences:**

**Positive:**
1. **Better Operational Visibility:** Operations teams can monitor message flows end-to-end in RabbitMQ
2. **Future-Proof:** Easy to add new consumers (e.g., Analytics BC, Vendor Portal BFF) without changing publishers
3. **Troubleshooting:** Dead-letter queues make it easier to investigate message processing failures
4. **Performance Tuning:** Can tune prefetch counts, TTL, and other RabbitMQ settings per queue

**Negative:**
1. **More Configuration:** Each API needs RabbitMQ section in appsettings.json
2. **Testing Complexity:** Integration tests need RabbitMQ container (already true for Orders/Shopping)
3. **Local Development:** Requires `docker-compose up` for RabbitMQ (already true)

**Alternatives Considered:**

**Alternative 1: Keep Current Approach (Wolverine Local Queues Only)**

**Pros:**
- No configuration changes needed
- Wolverine transactional outbox ensures delivery within .NET ecosystem

**Cons:**
- Poor observability (can't see message flows in RabbitMQ)
- Can't integrate with non-.NET services
- Inconsistent with Orders/Shopping (operational confusion)
- Harder to debug message delivery issues

**Rejected:** Inconsistency across BCs creates operational confusion and limits future integration options.

**Alternative 2: Migrate to Azure Service Bus / AWS SQS**

**Pros:**
- Managed service (no infrastructure to maintain)
- Better integration with cloud platforms

**Cons:**
- Vendor lock-in
- Higher cost than self-hosted RabbitMQ
- Not necessary for reference architecture (RabbitMQ is fine)

**Rejected:** RabbitMQ is already used and works well. No need to switch message brokers.

**References:**

- **Architectural Review:** [docs/ARCHITECTURAL-REVIEW.md](../ARCHITECTURAL-REVIEW.md) - Concern #1
- **Wolverine RabbitMQ Docs:** https://wolverine.netlify.app/guide/messaging/transports/rabbitmq.html
- **CONTEXTS.md:** Integration flows section for each BC

**Implementation Timeline:**

- **Phase 1:** Update Payments.Api with RabbitMQ (1 hour)
- **Phase 2:** Update Inventory.Api with RabbitMQ (1 hour)
- **Phase 3:** Update Fulfillment.Api with RabbitMQ (1 hour)
- **Phase 4:** Refactor Shopping/Orders to use exchanges (1 hour)
- **Phase 5:** Update Storefront.Api subscriptions (1 hour)
- **Phase 6:** Integration testing (2 hours)

**Total Effort:** ~7 hours

**Priority:** HIGH (before production deployment)

---

**Last Updated:** 2026-02-14  
**Author:** Senior Software Architect (Architectural Review Persona)  
**Reviewers:** Erik Shafer (Maintainer)
